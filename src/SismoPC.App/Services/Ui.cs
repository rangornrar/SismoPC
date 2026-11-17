using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SismoPC.Ui.Services;

public enum Status
{
    Good,
    Warning,
    Critical,
    Info,
}

/// <summary>Couleurs du design system SismoPC (maquettes validées), en thème clair et sombre.</summary>
public static class Palette
{
    private static bool IsDark(FrameworkElement? element) =>
        (element?.ActualTheme ?? (Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light)) == ElementTheme.Dark;

    public static Color Hex(uint rgb) => ColorHelper.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static Color Accent(FrameworkElement? e) => IsDark(e) ? Hex(0x4CC2D1) : Hex(0x0E6F80);

    public static Color StatusColor(Status status, FrameworkElement? e) => (status, IsDark(e)) switch
    {
        (Status.Good, false) => Hex(0x0F7B0F),
        (Status.Good, true) => Hex(0x6CCB5F),
        (Status.Warning, false) => Hex(0x9D5D00),
        (Status.Warning, true) => Hex(0xFCE100),
        (Status.Critical, false) => Hex(0xC42B1C),
        (Status.Critical, true) => Hex(0xFF99A4),
        (_, false) => Hex(0x0E6F80),
        (_, true) => Hex(0x4CC2D1),
    };

    /// <summary>Couleurs des courbes : processeur, mémoire, disque, température (maquette Chronologie).</summary>
    public static Color Series(int index, FrameworkElement? e)
    {
        uint[] light = [0x0E6F80, 0x6B4FBB, 0xB4570B, 0xB42318];
        uint[] dark = [0x4CC2D1, 0xB9A6F2, 0xF4A462, 0xFF8A80];
        return Hex((IsDark(e) ? dark : light)[index % light.Length]);
    }
}

/// <summary>Petits constructeurs d'éléments d'interface, pour garder les pages lisibles.</summary>
public static class Ui
{
    public static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static TextBlock Text(string text, string style = "BodyTextBlockStyle", bool secondary = false)
    {
        var t = new TextBlock
        {
            Text = text,
            Style = (Style)Application.Current.Resources[style],
            TextWrapping = TextWrapping.Wrap,
        };
        if (secondary)
        {
            t.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }

        return t;
    }

    public static TextBlock SectionTitle(string text) => Text(text, "SubtitleTextBlockStyle");

    public static StackPanel Header(string title, string subtitle)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(Text(title, "TitleTextBlockStyle"));
        panel.Children.Add(Text(subtitle, secondary: true));
        return panel;
    }

    public static Border Card(UIElement child) => new()
    {
        Style = (Style)Application.Current.Resources["CardStyle"],
        Child = child,
    };

    public static StackPanel Stack(double spacing = 8, params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = spacing };
        foreach (var c in children)
        {
            panel.Children.Add(c);
        }

        return panel;
    }

    /// <summary>Pastille d'état colorée (OK, Attention, Critique…). La couleur n'est jamais seule : le texte dit l'état.</summary>
    public static Border Pill(string text, Status status, FrameworkElement? themeSource)
    {
        var color = Palette.StatusColor(status, themeSource);
        var dot = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(color) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(dot);
        row.Children.Add(label);
        return new Border
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(36, color.R, color.G, color.B)),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(10, 2, 10, 2),
            Child = row,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
    }

    /// <summary>Grille de N colonnes égales ; les éléments sont placés de gauche à droite puis ligne par ligne.</summary>
    public static Grid Columns(int count, IEnumerable<UIElement> items, double spacing = 12)
    {
        var grid = new Grid { ColumnSpacing = spacing, RowSpacing = spacing };
        for (var c = 0; c < count; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var i = 0;
        foreach (var item in items)
        {
            if (i % count == 0)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            Grid.SetColumn((FrameworkElement)item, i % count);
            Grid.SetRow((FrameworkElement)item, i / count);
            grid.Children.Add(item);
            i++;
        }

        return grid;
    }

    /// <summary>Ligne « libellé : valeur » pour les fiches.</summary>
    public static Grid Field(string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = Text(label, "CaptionTextBlockStyle", secondary: true);
        var v = Text(value);
        Grid.SetColumn(v, 1);
        grid.Children.Add(l);
        grid.Children.Add(v);
        return grid;
    }

    public static InfoBar Banner(string title, string message, InfoBarSeverity severity) => new()
    {
        Title = title,
        Message = message,
        Severity = severity,
        IsOpen = true,
        IsClosable = false,
    };

    public static string Percent(double v) => string.Format(Fr, "{0:0} %", v);

    public static string Celsius(double v) => string.Format(Fr, "{0:0} °C", v);

    public static string Gigabytes(double bytes) => string.Format(Fr, "{0:0} Go", bytes / 1e9);

    public static string When(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        var today = DateTimeOffset.Now.Date;
        return local.Date == today ? $"Aujourd'hui, {local:HH:mm}"
            : local.Date == today.AddDays(-1) ? $"Hier, {local:HH:mm}"
            : local.ToString("ddd d MMM, HH:mm", Fr);
    }
}
