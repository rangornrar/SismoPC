using System.IO.Pipes;
using System.Text;
using SismoPC.Core.Ipc;

namespace SismoPC.Ui.Services;

/// <summary>Client du canal nommé du service : état en direct, réglages, épisodes ignorés.</summary>
public static class ServiceClient
{
    private const int ConnectTimeoutMs = 1500;

    public static async Task<LiveStatus?> GetStatusAsync()
    {
        var line = await SendAsync(new PipeRequest(RequestTypes.Status)).ConfigureAwait(false);
        if (line is null || line.Contains("\"type\"", StringComparison.Ordinal))
        {
            return null; // Service absent ou encore en démarrage.
        }

        try
        {
            return PipeProtocol.Deserialize<LiveStatus>(line);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public static async Task<bool> PingAsync() =>
        (await SendAsync(new PipeRequest(RequestTypes.Ping)).ConfigureAwait(false))?.Contains("pong", StringComparison.Ordinal) == true;

    public static async Task<bool> SetSettingAsync(string key, bool value) =>
        (await SendAsync(new PipeRequest(RequestTypes.SetSetting, key, value ? "true" : "false")).ConfigureAwait(false))
            ?.Contains("ok", StringComparison.Ordinal) == true;

    public static async Task<bool> IgnoreEpisodeAsync(long id) =>
        (await SendAsync(new PipeRequest(RequestTypes.IgnoreEpisode, id.ToString(System.Globalization.CultureInfo.InvariantCulture))).ConfigureAwait(false))
            ?.Contains("ok", StringComparison.Ordinal) == true;

    private static async Task<string?> SendAsync(PipeRequest request)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var pipe = new NamedPipeClientStream(".", PipeProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(ConnectTimeoutMs, timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: -1, leaveOpen: true);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), bufferSize: -1, leaveOpen: true) { AutoFlush = true };
            await using (writer.ConfigureAwait(false))
            {
                await writer.WriteLineAsync(PipeProtocol.Serialize(request)).ConfigureAwait(false);
                return await PipeProtocol.ReadFrameAsync(reader, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
