using System.Text.Json;
using System.Text.Json.Serialization;
using SismoPC.Core.Health;

namespace SismoPC.Core.Ipc;

/// <summary>
/// Protocole du canal nommé entre le service et ses clients (interface, Notifier) :
/// une requête JSON par ligne, une ou plusieurs réponses JSON par ligne.
/// </summary>
public static class PipeProtocol
{
    public const int MaxFrameLength = 16384;

    public static async Task<string?> ReadFrameAsync(TextReader reader, CancellationToken token)
    {
        var buffer = new char[1];
        var line = new System.Text.StringBuilder();
        while (await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false) != 0)
        {
            if (buffer[0] == '\n') return line.ToString().TrimEnd('\r');
            if (line.Length >= MaxFrameLength) throw new IOException("Trame IPC trop longue");
            line.Append(buffer[0]);
        }
        return null;
    }

    public static string PipeName => Environment.GetEnvironmentVariable("SISMOPC_PIPE_NAME") ?? "SismoPC";

    /// <summary>Identifiant d'application Windows, utilisé pour les notifications et le raccourci du menu Démarrer.</summary>
    public const string AppUserModelId = "rangornrar.SismoPC";

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    public static T? Deserialize<T>(string line) => JsonSerializer.Deserialize<T>(line, Json);
}

/// <summary>Types de requêtes acceptées par le service.</summary>
public static class RequestTypes
{
    public const string Ping = "ping";
    public const string Status = "status";
    public const string Subscribe = "subscribe";
    public const string SetSetting = "setSetting";
    public const string MarkIncident = "markIncident";

    public const string IgnoreEpisode = "ignoreEpisode";
}

/// <summary>Réglages modifiables depuis l'interface. Toute autre clé est refusée par le service.</summary>
public static class SettingKeys
{
    public const string NotifyAnomaly = "notify.anomaly";
    public const string NotifyWeekly = "notify.weekly";

    public const string NotifySlowdown = "notify.slowdown";

    public static IReadOnlySet<string> Editable { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        NotifyAnomaly,
        NotifySlowdown,
        NotifyWeekly,
    };
}

/// <summary>Requête d'un client ; <paramref name="Key"/> et <paramref name="Value"/> servent à « setSetting ».</summary>
public sealed record PipeRequest(string Type, string? Key = null, string? Value = null);

/// <summary>Événement poussé aux abonnés : épisode, anomalie ou alerte matérielle.</summary>
/// <param name="Kind">« alert », « slowdown » ou « anomaly ».</param>
/// <param name="Severity">« critical », « warning » ou « info ».</param>
/// <param name="EpisodeId">Identifiant de l'épisode en base, pour ouvrir son détail.</param>
public sealed record PipeEvent(string Kind, string Severity, string Title, string Message, long? EpisodeId = null, string? Target = null);

/// <summary>État en direct du PC, renvoyé à la requête « status ».</summary>
public sealed record LiveStatus(
    DateTimeOffset At,
    IReadOnlyDictionary<string, double> Metrics,
    int Score,
    string ScoreLabel,
    IReadOnlyList<Penalty> Penalties,
    bool Learning,
    DateTimeOffset LearningEndsAt,
    bool TemperaturesAvailable);
