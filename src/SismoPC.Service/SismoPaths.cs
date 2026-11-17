namespace SismoPC.Service;

/// <summary>Emplacements des données : C:\ProgramData\SismoPC.</summary>
public static class SismoPaths
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SismoPC");

    public static string Database => Path.Combine(DataDirectory, "sismopc.db");
}
