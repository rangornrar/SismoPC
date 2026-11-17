using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SismoPC.Core.Model;
using SismoPC.Ui.Services;
using Windows.ApplicationModel.DataTransfer;

namespace SismoPC.Ui.Pages;

/// <summary>Matériel : inventaire complet et santé par composant, copie de l'inventaire (US1, US6).</summary>
public sealed partial class HardwarePage : Page
{
    private static readonly string[] TypeOrder = ["Processeur", "Carte graphique", "Mémoire", "Carte mère", "BIOS", "Écran", "Réseau", "Système", "Disque"];

    public HardwarePage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Root.Children.Clear();

        var data = await DataAccess.ReadAsync(store => (Components: store.ReadComponents(), Disks: store.ReadLatestDiskHealth()));
        var components = data.Components ?? [];
        var disks = data.Disks ?? [];

        var copy = new Button { Content = "Copier l'inventaire" };
        copy.Click += (_, _) =>
        {
            var package = new DataPackage();
            package.SetText(InventoryText(components));
            Clipboard.SetContent(package);
            copy.Content = "Inventaire copié";
        };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(copy, 1);
        copy.VerticalAlignment = VerticalAlignment.Bottom;
        header.Children.Add(UiKit.Header("Matériel", components.Count == 0
            ? "Inventaire pas encore disponible : le service ne l'a pas encore réalisé."
            : "Inventaire mis à jour au démarrage du service, puis chaque jour"));
        header.Children.Add(copy);
        Root.Children.Add(header);

        var cards = components
            .Where(c => c.Type != "Disque")
            .OrderBy(c => Rank(c.Type))
            .Select(ComponentCard)
            .ToList<UIElement>();
        if (cards.Count > 0)
        {
            Root.Children.Add(UiKit.Columns(2, cards, 20));
        }

        Root.Children.Add(StorageCard(components.Where(c => c.Type == "Disque").ToList(), disks));
    }

    private static int Rank(string type)
    {
        var i = Array.IndexOf(TypeOrder, type);
        return i < 0 ? TypeOrder.Length : i;
    }

    private static Border ComponentCard(HardwareComponent c)
    {
        var panel = UiKit.Stack(8,
            UiKit.Text(c.Type.ToUpper(UiKit.Fr), "CaptionTextBlockStyle", secondary: true),
            UiKit.Text(c.Name, "SubtitleTextBlockStyle"));
        foreach (var (label, value) in c.Details)
        {
            panel.Children.Add(UiKit.Field(label, value));
        }

        return UiKit.Card(panel);
    }

    private Border StorageCard(IReadOnlyList<HardwareComponent> drives, IReadOnlyList<DiskHealth> health)
    {
        var panel = UiKit.Stack(10, UiKit.SectionTitle("Stockage"));

        foreach (var disk in health)
        {
            var model = drives.FirstOrDefault(d => d.Name.Contains(disk.Name, StringComparison.OrdinalIgnoreCase)
                                                   || disk.Name.Contains(d.Name, StringComparison.OrdinalIgnoreCase));
            var state = disk.PredictsFailure ? Status.Critical : disk.WearPercent >= 80 ? Status.Warning : Status.Good;
            var label = state switch { Status.Critical => "Risque de panne", Status.Warning => "Usé", _ => "Bonne santé" };

            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            head.Children.Add(UiKit.Text(disk.Name, "BodyStrongTextBlockStyle"));
            head.Children.Add(UiKit.Pill(label, state, this));
            panel.Children.Add(head);
            panel.Children.Add(UiKit.Field("Type", disk.IsSsd ? "SSD" : "Disque dur"));
            if (model is not null && model.Details.TryGetValue("Capacité", out var size))
            {
                panel.Children.Add(UiKit.Field("Capacité", size));
            }

            if (disk.WearPercent is { } wear)
            {
                panel.Children.Add(UiKit.Field("Usure", UiKit.Percent(wear)));
            }

            if (disk.TemperatureC is { } temp)
            {
                panel.Children.Add(UiKit.Field("Température", UiKit.Celsius(temp)));
            }

            if (disk.PowerOnHours is { } hours)
            {
                panel.Children.Add(UiKit.Field("Fonctionnement", $"{hours:N0} h".Replace(",", " ", StringComparison.Ordinal)));
            }
        }

        if (health.Count == 0)
        {
            panel.Children.Add(UiKit.Text("L'état SMART des disques sera lu par le service (droits administrateur nécessaires).", secondary: true));
        }

        panel.Children.Add(UiKit.Text("Espace libre", "BodyStrongTextBlockStyle"));
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var free = 100.0 * drive.AvailableFreeSpace / drive.TotalSize;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(UiKit.Field(drive.Name.TrimEnd('\\'),
                $"{UiKit.Gigabytes(drive.AvailableFreeSpace)} libres sur {UiKit.Gigabytes(drive.TotalSize)} ({UiKit.Percent(free)})"));
            if (free < 10)
            {
                row.Children.Add(UiKit.Pill("Attention", Status.Warning, this));
            }

            panel.Children.Add(row);
        }

        return UiKit.Card(panel);
    }

    private static string InventoryText(IReadOnlyList<HardwareComponent> components)
    {
        var sb = new StringBuilder("Inventaire SismoPC\r\n\r\n");
        foreach (var c in components)
        {
            sb.Append(c.Type).Append(" : ").Append(c.Name).Append("\r\n");
            foreach (var (label, value) in c.Details)
            {
                sb.Append("  ").Append(label).Append(" : ").Append(value).Append("\r\n");
            }
        }

        return sb.ToString();
    }
}
