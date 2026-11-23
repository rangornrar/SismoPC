using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using SismoPC.Core.Ipc;

namespace SismoPC.Service;

/// <summary>
/// Canal nommé « SismoPC » (ADR-5). Accès réservé au compte système, aux administrateurs et à l'utilisateur
/// connecté à la session. Il n'accepte que des requêtes prévues : jamais l'exécution d'un programme.
/// </summary>
public sealed partial class PipeServer(EventHub hub, StatusProvider status, ILogger<PipeServer> logger) : BackgroundService
{
    private readonly SemaphoreSlim _clients = new(16);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = CreatePipe();
            }
            catch (IOException ex)
            {
                LogPipeError(logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                break;
            }

            if (!_clients.Wait(0, CancellationToken.None)) { await pipe.DisposeAsync().ConfigureAwait(false); continue; }
            _ = Task.Run(async () =>
            {
                try { await HandleAsync(pipe, stoppingToken).ConfigureAwait(false); }
                catch (Exception ex) { LogPipeError(logger, ex); }
                finally { _clients.Release(); }
            }, CancellationToken.None);
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        // Le créateur doit pouvoir ouvrir les instances suivantes, y compris en exécution console.
        if (WindowsIdentity.GetCurrent().User is { } owner)
            security.AddAccessRule(new PipeAccessRule(owner, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.InteractiveSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            PipeProtocol.PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    private async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: -1, leaveOpen: true);
            var writer = new StreamWriter(pipe, new UTF8Encoding(false), bufferSize: -1, leaveOpen: true) { AutoFlush = true };
            await using (writer.ConfigureAwait(false))
            {
                try
                {
                    using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    while (true)
                    {
                        requestTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                        var line = await PipeProtocol.ReadFrameAsync(reader, requestTimeout.Token).ConfigureAwait(false);
                        if (line is null) break;
                        var request = Parse(line);
                        switch (request?.Type)
                        {
                            case RequestTypes.Ping:
                                await writer.WriteLineAsync("{\"type\":\"pong\"}").ConfigureAwait(false);
                                break;

                            case RequestTypes.Status:
                                await writer.WriteLineAsync(status.Current is { } s
                                    ? PipeProtocol.Serialize(s)
                                    : "{\"type\":\"starting\"}").ConfigureAwait(false);
                                break;

                            case RequestTypes.MarkIncident:
                                var at = DateTimeOffset.UtcNow;
                                using (var store = new SismoPC.Data.SismoStore(SismoPaths.Database))
                                    store.AddSystemEvent(at, "manual", "Ralentissement signalé par l'utilisateur");
                                await writer.WriteLineAsync(PipeProtocol.Serialize(new { type = "ok", at })).ConfigureAwait(false);
                                break;

                            case RequestTypes.SetSetting:
                                await writer.WriteLineAsync(TrySetSetting(request!.Key, request.Value)
                                    ? "{\"type\":\"ok\"}"
                                    : "{\"type\":\"refused\"}").ConfigureAwait(false);
                                break;

                            case RequestTypes.IgnoreEpisode:
                                await writer.WriteLineAsync(TryIgnoreEpisode(request!.Key)
                                    ? "{\"type\":\"ok\"}"
                                    : "{\"type\":\"refused\"}").ConfigureAwait(false);
                                break;

                            case RequestTypes.Subscribe:
                                await foreach (var e in hub.Subscribe(cancellationToken).ConfigureAwait(false))
                                {
                                    await writer.WriteLineAsync(PipeProtocol.Serialize(e)).ConfigureAwait(false);
                                }

                                return;

                            default:
                                await writer.WriteLineAsync("{\"type\":\"error\"}").ConfigureAwait(false);
                                break;
                        }
                    }
                }
                catch (IOException)
                {
                    // Client déconnecté.
                }
                catch (OperationCanceledException)
                {
                    // Arrêt du service.
                }
            }
        }
    }

    /// <summary>Seules les clés de <see cref="SettingKeys.Editable"/> avec « true » ou « false » sont acceptées.</summary>
    private static bool TrySetSetting(string? key, string? value)
    {
        if (key is null || !SettingKeys.Editable.Contains(key) || value is not ("true" or "false"))
        {
            return false;
        }

        using var store = new SismoPC.Data.SismoStore(SismoPaths.Database);
        store.SetSetting(key, value);
        if (key == SettingKeys.NotifyWeekly && value == "true")
            store.SetSetting("weekly.last", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }

    private static bool TryIgnoreEpisode(string? key)
    {
        if (!long.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id))
        {
            return false;
        }

        using var store = new SismoPC.Data.SismoStore(SismoPaths.Database);
        store.IgnoreEpisode(id);
        return true;
    }

    private static PipeRequest? Parse(string line)
    {
        try
        {
            return PipeProtocol.Deserialize<PipeRequest>(line);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Canal nommé indisponible")]
    private static partial void LogPipeError(ILogger logger, Exception ex);
}
