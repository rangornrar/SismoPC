namespace SismoPC.Service;

/// <summary>Emplacements des données : C:\ProgramData\SismoPC.</summary>
public static class SismoPaths
{
    public static string DataDirectory { get; } =
        Environment.GetEnvironmentVariable("SISMOPC_DATA_DIRECTORY") is { Length: > 0 } custom ? Path.GetFullPath(custom) :
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SismoPC");

    public static string Database => Path.Combine(DataDirectory, "sismopc.db");
}
