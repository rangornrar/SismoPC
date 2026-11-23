using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SismoPC.Core.Model;
using SismoPC.Core.Reporting;
using SismoPC.Ui.Services;

namespace SismoPC.Ui.Pages;

public sealed record ReportSelection(DateTimeOffset From, DateTimeOffset To, string Title);

public sealed partial class ReportsPage : Page, IDisposable
{
    private readonly WebView2 _preview = new() { Height = 700 };
    private readonly InfoBar _message = new() { IsOpen = false, IsClosable = false };
    private readonly CheckBox _machine = new() { Content = "Nom de l'ordinateur" };
    private readonly CheckBox _programs = new() { Content = "Noms des programmes" };
    private readonly CheckBox _identifiers = new() { Content = "Identifiants matériels" };
    private readonly CheckBox _hardware = new() { Content = "Configuration matérielle" };
    private readonly ComboBox _period = new() { ItemsSource = new[] { "7 derniers jours", "24 dernières heures", "30 derniers jours" }, SelectedIndex = 0, MinWidth = 220 };
    private readonly Button _pdf = new() { Content = "Enregistrer le PDF", IsEnabled = false };
    private readonly Button _html = new() { Content = "Enregistrer en HTML", IsEnabled = false };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _stop;
    private bool _disposed;
    private bool _exporting;
    private bool _rendering;
    private bool _pending;
    private bool _ready;
    private string? _document;
    private ReportSelection? _selection;

    public ReportsPage()
    {
        InitializeComponent();
        _stop = _lifetime.Token;
        var root = new StackPanel { Spacing = 18, Padding = new Thickness(32,28,32,40), MaxWidth = 1100 };
        Content = new ScrollViewer { Content = root };
        root.Children.Add(UiKit.Header("Un rapport clair, prêt à partager", "Vérifiez l'aperçu et les informations incluses avant de créer le document."));
        root.Children.Add(_period);
        root.Children.Add(UiKit.Card(UiKit.Stack(6, UiKit.SectionTitle("Vous choisissez ce qui sera partagé"),
            UiKit.Text("Les informations ci-dessous sont masquées par défaut. Le rapport est créé sur ce PC ; aucun envoi n'est effectué.", secondary: true),
            _machine, _programs, _hardware, _identifiers)));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        _pdf.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        actions.Children.Add(_pdf); actions.Children.Add(_html);
        root.Children.Add(actions); root.Children.Add(_message); root.Children.Add(_preview);
        foreach (var box in new[] { _machine, _programs, _hardware, _identifiers })
        {
            box.Checked += async (_, _) => await RefreshAsync();
            box.Unchecked += async (_, _) => await RefreshAsync();
        }
        _period.SelectionChanged += async (_, _) => await RefreshAsync();
        _pdf.Click += async (_, _) => await ExportAsync(true);
        _html.Click += async (_, _) => await ExportAsync(false);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _selection = e.Parameter as ReportSelection;
        if (_selection is not null) _period.Visibility = Visibility.Collapsed;
        await RefreshAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Dispose();
        base.OnNavigatedFrom(e);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _preview.Close();
        _lifetime.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RefreshAsync()
    {
        if (_stop.IsCancellationRequested) return;
        if (_rendering) { _pending = true; return; }
        _rendering = true;
        _pdf.IsEnabled = _html.IsEnabled = false;
        try
        {
            do
            {
                _pending = false;
                var now = DateTimeOffset.UtcNow;
                var selection = _selection ?? new ReportSelection(_period.SelectedIndex == 1 ? now.AddHours(-24) : now.AddDays(_period.SelectedIndex == 2 ? -30 : -7), now, "Bilan de votre PC");
                var options = new ReportOptions(_machine.IsChecked == true, _programs.IsChecked == true, _identifiers.IsChecked == true, _hardware.IsChecked == true);
                _message.IsOpen = true; _message.Severity = InfoBarSeverity.Informational; _message.Title = "Préparation de l'aperçu…"; _message.Message = "";
                var content = await DataAccess.ReadAsync(store => new ReportContent(selection.From, selection.To, now,
                    typeof(App).Assembly.GetName().Version?.ToString(3) ?? "", Environment.MachineName,
                    store.ReadOpenAlerts(), store.ReadEpisodes(selection.From, selection.To).Select(e => e.Episode).ToList(),
                    store.ReadObservations(selection.From, selection.To), store.ReadComponents(),
                    new[] { (MetricKind.CpuPercent,"Processeur","%"), (MetricKind.MemoryUsedPercent,"Mémoire utilisée","%"), (MetricKind.DiskActivePercent,"Disque actif","%"), (MetricKind.CpuTemperatureC,"Température du processeur","°C") }
                        .Select(r => new ReportCurve(r.Item2, r.Item3, store.ReadSeries(r.Item1, selection.From, selection.To).Select(p => new TimedValue(p.Timestamp,p.Max,p.Resolution)).ToList())).ToList(),
                    store.ReadSystemEvents(selection.From, selection.To).Count(e => e.Kind == "manual"), selection.Title));
                if (_stop.IsCancellationRequested) return;
                if (_pending) continue;
                if (content is null) { _document = null; _message.Title = "Historique indisponible"; _message.Message = "Le rapport sera disponible lorsque le service aura créé une base lisible."; return; }
                _document = ReportDocument.Html(content, options);
                try
                {
                    await _preview.EnsureCoreWebView2Async();
                    _preview.CoreWebView2.Settings.IsScriptEnabled = false;
                    var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void Completed(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
                    {
                        if (args.IsSuccess) loaded.TrySetResult(); else loaded.TrySetException(new IOException("Aperçu non chargé."));
                    }
                    var core = _preview.CoreWebView2;
                    core.NavigationCompleted += Completed;
                    try { _preview.NavigateToString(_document); await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15), _stop); }
                    finally { core.NavigationCompleted -= Completed; }
                    _ready = true;
                    _message.IsOpen = false;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _ready = false;
                    _message.Severity = InfoBarSeverity.Warning;
                    _message.Title = "Aperçu PDF indisponible";
                    _message.Message = "Vérifiez le runtime Microsoft Edge WebView2. L'export HTML reste disponible pour ouvrir et imprimer le document dans votre navigateur.";
                }
            } while (_pending);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _rendering = false;
            _pdf.IsEnabled = _ready && _document is not null && !_stop.IsCancellationRequested;
            _html.IsEnabled = _document is not null && !_stop.IsCancellationRequested;
        }
    }

    private async Task ExportAsync(bool pdf)
    {
        if (_document is null || App.Window is null || _exporting) return;
        _exporting = true;
        _period.IsEnabled = _machine.IsEnabled = _programs.IsEnabled = _identifiers.IsEnabled = _hardware.IsEnabled = false;
        _pdf.IsEnabled = _html.IsEnabled = false;
        try
        {
            var path = SaveDialog.Pick(WinRT.Interop.WindowNative.GetWindowHandle(App.Window), pdf ? "pdf" : "html");
            if (path is null || _stop.IsCancellationRequested) return;
            if (pdf)
            {
                var settings = _preview.CoreWebView2.Environment.CreatePrintSettings();
                settings.PageWidth = 210 / 25.4;
                settings.PageHeight = 297 / 25.4;
                settings.ShouldPrintBackgrounds = true;
                settings.ShouldPrintHeaderAndFooter = true;
                settings.HeaderTitle = "SismoPC · Rapport local";
                settings.FooterUri = "";
                if (!await _preview.CoreWebView2.PrintToPdfAsync(path, settings)) throw new IOException("Le moteur PDF n'a pas terminé l'export.");
            }
            else await File.WriteAllTextAsync(path, _document, _stop);
            _message.Severity = InfoBarSeverity.Success; _message.Title = "Rapport enregistré"; _message.Message = path; _message.IsOpen = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            _message.Severity = InfoBarSeverity.Error; _message.Title = "Le rapport n'a pas pu être enregistré"; _message.Message = "Choisissez un dossier accessible et réessayez."; _message.IsOpen = true;
        }
        finally
        {
            _exporting = false;
            _period.IsEnabled = _machine.IsEnabled = _programs.IsEnabled = _identifiers.IsEnabled = _hardware.IsEnabled = true;
            _pdf.IsEnabled = _ready && !_stop.IsCancellationRequested; _html.IsEnabled = !_stop.IsCancellationRequested;
        }
    }
}
