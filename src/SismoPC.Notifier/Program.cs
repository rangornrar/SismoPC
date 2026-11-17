using System.IO.Pipes;
using System.Text;
using SismoPC.Core.Ipc;
using SismoPC.Notifier;

// SismoPC.Notifier (ADR-4) : programme sans fenêtre, lancé à l'ouverture de session.
// Il s'abonne aux événements du service et les affiche en notifications Windows.

using var instance = new Mutex(initiallyOwned: true, @"Local\SismoPC.Notifier", out var isFirst);
if (!isFirst)
{
    return; // Déjà lancé dans cette session.
}

while (true)
{
    try
    {
        using var pipe = new NamedPipeClientStream(".", PipeProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000).ConfigureAwait(false);

        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: -1, leaveOpen: true);
        var writer = new StreamWriter(pipe, new UTF8Encoding(false), bufferSize: -1, leaveOpen: true) { AutoFlush = true };
        await using (writer.ConfigureAwait(false))
        {
            await writer.WriteLineAsync(PipeProtocol.Serialize(new PipeRequest(RequestTypes.Subscribe))).ConfigureAwait(false);

            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (PipeProtocol.Deserialize<PipeEvent>(line) is { } e)
                {
                    Toasts.Show(e);
                }
            }
        }
    }
    catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
    {
        // Service arrêté ou en cours de redémarrage : nouvelle tentative plus tard.
    }

    await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
}
