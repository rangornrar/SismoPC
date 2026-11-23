namespace SismoPC.Ui.Services;

/// <summary>Profil WebView2 et diagnostic propres à l'utilisateur, indépendants du dossier d'installation.</summary>
internal static class PreviewProfile
{
    private static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SismoPC");
    public static string DirectoryPath => Path.Combine(Root, "WebView2");
    public static string LogPath => Path.Combine(Root, "logs", "preview.log");

    public static void LogFailure(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            // Aucun contenu de rapport ni nom de programme n'est enregistré dans ce diagnostic.
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {exception.GetType().Name} 0x{exception.HResult:X8}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
