using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using SismoPC.Core.Ipc;

namespace SismoPC.Notifier;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var suffix = Environment.GetEnvironmentVariable("SISMOPC_PIPE_NAME") is { Length: > 0 } name ? "." + name : "";
        using var instance = new Mutex(initiallyOwned: true, @"Local\SismoPC.Notifier" + suffix, out var first);
        if (!first) return;
        ApplicationConfiguration.Initialize();
        using var context = new NotificationContext();
        System.Windows.Forms.Application.Run(context);
    }
}

internal sealed class NotificationContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30000 };
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _token;
    private readonly Task _subscription;
    private bool _polling;

    public NotificationContext()
    {
        _token = _stop.Token;
        var path = Path.Combine(AppContext.BaseDirectory, "SismoPC.ico");
        _icon = new NotifyIcon { Icon = File.Exists(path) ? new Icon(path) : SystemIcons.Information, Text = "SismoPC · surveillance locale", Visible = true, ContextMenuStrip = _menu };
        _icon.DoubleClick += (_, _) => Open("home");
        _menu.Items.Add("Ouvrir SismoPC", null, (_, _) => Open("home"));
        _menu.Items.Add("Mon PC vient de ralentir", null, (_, _) => Open("incident"));
        _menu.Items.Add("Mon bilan des 7 derniers jours", null, (_, _) => Open("reports"));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Fermer l'icône et les notifications", null, (_, _) => ExitThread());
        _timer.Tick += async (_, _) => await RefreshStatusAsync();
        _timer.Start();
        _subscription = Task.Run(() => SubscribeAsync(_token));
        _ = RefreshStatusAsync();
    }

    private void Open(string page)
    {
        try
        {
            var app = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "App", "SismoPC.exe"));
            Process.Start(File.Exists(app)
                ? new ProcessStartInfo(app, $"sismopc://{page}") { UseShellExecute = true }
                : new ProcessStartInfo($"sismopc://{page}") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        { _icon.Text = "SismoPC · ouverture indisponible"; }
    }

    private async Task RefreshStatusAsync()
    {
        if (_polling || _token.IsCancellationRequested) return;
        _polling = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_token);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var pipe = new NamedPipeClientStream(".", PipeProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            await writer.WriteLineAsync(PipeProtocol.Serialize(new PipeRequest(RequestTypes.Status)));
            var response = await PipeProtocol.ReadFrameAsync(reader, timeout.Token);
            var status = response is null ? null : PipeProtocol.Deserialize<LiveStatus>(response);
            var label = status is null || DateTimeOffset.UtcNow - status.At > TimeSpan.FromSeconds(60) ? "collecte indisponible" : status.ScoreLabel;
            var text = "SismoPC · " + label;
            if (!_token.IsCancellationRequested) _icon.Text = text[..Math.Min(63,text.Length)];
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { if (!_token.IsCancellationRequested) _icon.Text = "SismoPC · collecte indisponible"; }
        finally { _polling = false; }
    }

    private static async Task SubscribeAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(5000, token).ConfigureAwait(false);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                await writer.WriteLineAsync(PipeProtocol.Serialize(new PipeRequest(RequestTypes.Subscribe))).ConfigureAwait(false);
                while (await PipeProtocol.ReadFrameAsync(reader, token).ConfigureAwait(false) is { } line)
                    if (PipeProtocol.Deserialize<PipeEvent>(line) is { } e) Toasts.Show(e);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or OperationCanceledException or System.Text.Json.JsonException) { }
            try { await Task.Delay(TimeSpan.FromSeconds(10), token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop(); _timer.Dispose(); _stop.Cancel();
            _icon.Visible = false; _icon.Dispose(); _menu.Dispose();
            _ = _subscription.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
        }
        base.Dispose(disposing);
    }
}
