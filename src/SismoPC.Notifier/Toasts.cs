using System.Security;
using SismoPC.Core.Ipc;
using Windows.UI.Notifications;

namespace SismoPC.Notifier;

/// <summary>
/// Affiche une notification Windows. Un clic ouvre l'interface par le protocole « sismopc:// »,
/// enregistré par l'installeur, directement sur le détail de l'épisode (US7).
/// </summary>
internal static class Toasts
{
    public static string BuildXml(PipeEvent e)
    {
        var launch = e.EpisodeId is { } id ? $"sismopc://episode/{id}" : e.Target == "reports" ? "sismopc://reports" : "sismopc://home";
        var scenario = e.Severity == "critical" ? " scenario=\"urgent\"" : string.Empty;
        return $"""
            <toast activationType="protocol" launch="{SecurityElement.Escape(launch)}"{scenario}>
              <visual>
                <binding template="ToastGeneric">
                  <text>{SecurityElement.Escape(e.Kind == "alert" ? "SismoPC · alerte matérielle" : e.Title)}</text>
                  <text>{SecurityElement.Escape(e.Kind is "alert" or "anomaly" or "slowdown" ? "Une observation mérite votre attention. Ouvrez SismoPC pour consulter le détail." : e.Message)}</text>
                  <text placement="attribution">SismoPC</text>
                </binding>
              </visual>
            </toast>
            """;
    }

    public static void Show(PipeEvent e)
    {
        try
        {
            var xml = new Windows.Data.Xml.Dom.XmlDocument();
            xml.LoadXml(BuildXml(e));
            ToastNotificationManager.CreateToastNotifier(PipeProtocol.AppUserModelId).Show(new ToastNotification(xml));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException or UnauthorizedAccessException)
        {
            // Raccourci du menu Démarrer absent (identifiant d'application inconnu) : notification impossible.
        }
    }
}
