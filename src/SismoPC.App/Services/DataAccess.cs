using SismoPC.Data;

namespace SismoPC.Ui.Services;

/// <summary>
/// Accès en lecture seule à la base du service. L'interface n'écrit jamais dedans :
/// les modifications passent par le canal nommé (<see cref="ServiceClient"/>).
/// </summary>
public static class DataAccess
{
    public static string DatabasePath { get; } = Path.Combine(
        Environment.GetEnvironmentVariable("SISMOPC_DATA_DIRECTORY") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SismoPC"), "sismopc.db");

    public static bool DatabaseExists => File.Exists(DatabasePath);

    /// <summary>Exécute une lecture sur un fil d'arrière-plan ; renvoie la valeur par défaut si la base est absente ou illisible.</summary>
    public static Task<T?> ReadAsync<T>(Func<SismoStore, T> read) => Task.Run(() =>
    {
        if (!DatabaseExists)
        {
            return default;
        }

        try
        {
            using var store = new SismoStore(DatabasePath, readOnly: true);
            return read(store);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            return default;
        }
    });
}
