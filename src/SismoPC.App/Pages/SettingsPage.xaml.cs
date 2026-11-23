using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SismoPC.Core.Health;
using SismoPC.Core.Ipc;
using SismoPC.Ui.Services;

namespace SismoPC.Ui.Pages;

/// <summary>Paramètres : état du service, notifications, détection, conservation, calcul du score (US10).</summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Root.Children.Clear();
        Root.Children.Add(UiKit.Header("Paramètres", "Réglages de la collecte, de la détection et des alertes"));

        var statusTask = ServiceClient.GetStatusAsync();
        var settings = await DataAccess.ReadAsync(store => (
            Anomaly: store.GetSetting(SettingKeys.NotifyAnomaly),
            Slowdown: store.GetSetting(SettingKeys.NotifySlowdown),
            Weekly: store.GetSetting(SettingKeys.NotifyWeekly),
            Size: store.DatabaseSizeBytes()));
        var status = await statusTask;

        var left = UiKit.Stack(20, ServiceCard(status), DetectionCard(status), ScoreCard());
        var right = UiKit.Stack(20,
            NotificationsCard(settings.Anomaly != "false", settings.Slowdown == "true", settings.Weekly == "true", status is not null),
            RetentionCard(settings.Size));
        var grid = new Grid { ColumnSpacing = 20 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        Root.Children.Add(grid);
    }

    private Border ServiceCard(LiveStatus? status)
    {
        var state = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        state.Children.Add(UiKit.Pill(status is null ? "Ne répond pas" : "Actif", status is null ? Status.Critical : Status.Good, this));
        var panel = UiKit.Stack(10, UiKit.SectionTitle("Service de collecte"), state);
        panel.Children.Add(UiKit.Text(status is null
            ? "Le service SismoPC ne répond pas. Il démarre normalement avec Windows."
            : "Démarre avec Windows et enregistre toutes les 5 secondes, même sans session ouverte.", secondary: true));
        if (status is { TemperaturesAvailable: false })
        {
            panel.Children.Add(UiKit.Banner("Températures indisponibles",
                "La lecture des capteurs nécessite le pilote PawnIO, proposé par l'installeur de SismoPC.",
                InfoBarSeverity.Informational));
        }

        return UiKit.Card(panel);
    }

    private static Border DetectionCard(LiveStatus? status)
    {
        var learning = status switch
        {
            null => "—",
            { Learning: true } => $"En cours, jusqu'au {status.LearningEndsAt.ToLocalTime().ToString("d MMMM", UiKit.Fr)}",
            _ => $"Terminé le {status.LearningEndsAt.ToLocalTime().ToString("d MMMM", UiKit.Fr)}",
        };

        return UiKit.Card(UiKit.Stack(10,
            UiKit.SectionTitle("Détection"),
            UiKit.Field("Seuil de saturation", "90 % (processeur, disque, mémoire) ou 50 ms de latence disque"),
            UiKit.Field("Durée minimale", "30 s pour un ralentissement, 5 min pour une anomalie"),
            UiKit.Field("Apprentissage", learning),
            UiKit.Text("Ces seuils seront réglables dans une prochaine version.", "CaptionTextBlockStyle", secondary: true)));
    }

    private static Border ScoreCard()
    {
        var panel = UiKit.Stack(8, UiKit.SectionTitle("Calcul du score de santé"),
            UiKit.Text("100 points, moins les pénalités suivantes. Le score n'est jamais négatif.", secondary: true));
        panel.Children.Add(UiKit.Field("Alerte critique", $"−{HealthScoreCalculator.CriticalAlertPoints} chacune"));
        panel.Children.Add(UiKit.Field("Alerte « attention »", $"−{HealthScoreCalculator.WarningAlertPoints} chacune"));
        panel.Children.Add(UiKit.Field("Anomalie en cours", $"−{HealthScoreCalculator.AnomalyPoints} chacune"));
        panel.Children.Add(UiKit.Field("Ralentissement (7 jours)", $"−{HealthScoreCalculator.SlowdownPoints}, au plus −{HealthScoreCalculator.SlowdownCap}"));
        panel.Children.Add(UiKit.Field("Indicateur dégradé de plus de 20 %", $"−{HealthScoreCalculator.DegradedTrendPoints} chacun"));
        panel.Children.Add(UiKit.Text("Pendant l'apprentissage, seules les alertes matérielles comptent.", "CaptionTextBlockStyle", secondary: true));
        return UiKit.Card(panel);
    }

    private static Border NotificationsCard(bool anomalies, bool slowdowns, bool weekly, bool serviceAvailable)
    {
        var panel = UiKit.Stack(6, UiKit.SectionTitle("Notifications"));
        panel.Children.Add(UiKit.Text("Les alertes matérielles (température, risque de panne, usure, espace disque) sont toujours notifiées.", secondary: true));
        panel.Children.Add(Toggle("Anomalies", "Comportement inhabituel pour ce PC", SettingKeys.NotifyAnomaly, anomalies, serviceAvailable));
        panel.Children.Add(Toggle("Ralentissements", "Une notification au plus par heure", SettingKeys.NotifySlowdown, slowdowns, serviceAvailable));
        panel.Children.Add(Toggle("Bilan hebdomadaire", "Une notification discrète pour ouvrir votre bilan des 7 derniers jours", SettingKeys.NotifyWeekly, weekly, serviceAvailable));
        return UiKit.Card(panel);
    }

    private static ToggleSwitch Toggle(string header, string description, string key, bool isOn, bool enabled)
    {
        var toggle = new ToggleSwitch
        {
            Header = UiKit.Stack(0, UiKit.Text(header), UiKit.Text(description, "CaptionTextBlockStyle", secondary: true)),
            IsOn = isOn,
            IsEnabled = enabled,
        };
        var reverting = false;
        toggle.Toggled += async (_, _) =>
        {
            if (reverting)
            {
                return;
            }

            if (!await ServiceClient.SetSettingAsync(key, toggle.IsOn))
            {
                // Le service a refusé ou ne répond pas : on revient en arrière, sans renvoyer de requête.
                reverting = true;
                toggle.IsOn = !toggle.IsOn;
                reverting = false;
            }
        };
        return toggle;
    }

    private static Border RetentionCard(long sizeBytes) => UiKit.Card(UiKit.Stack(10,
        UiKit.SectionTitle("Conservation de l'historique"),
        UiKit.Field("Détail (toutes les 5 s)", "7 jours"),
        UiKit.Field("Résumé à la minute", "90 jours"),
        UiKit.Field("Résumé à l'heure", "2 ans"),
        UiKit.Field("Base de données", $"{DataAccess.DatabasePath} · {sizeBytes / 1e6:0} Mo"),
        UiKit.Text("Toutes les données restent sur ce PC. Rien n'est envoyé sur internet.", "CaptionTextBlockStyle", secondary: true)));
}
