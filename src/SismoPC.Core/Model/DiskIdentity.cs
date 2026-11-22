namespace SismoPC.Core.Model;

public static class DiskIdentity
{
    public static string Label(string model, string uniqueId, string serial, string deviceId)
    {
        var identity = !string.IsNullOrWhiteSpace(serial) ? serial.Trim()
            : !string.IsNullOrWhiteSpace(uniqueId) ? uniqueId.Trim() : "port " + deviceId;
        return $"{model} [{identity}]";
    }
}
