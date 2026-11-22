using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SismoPC.Ui.Services;

/// <summary>Met à jour les valeurs en conservant les contrôles déjà affichés.</summary>
internal static class LiveRefresh
{
    public static void Update(Panel current, Panel incoming)
    {
        var index = 0;
        while (incoming.Children.Count > 0)
        {
            var next = incoming.Children[0];
            incoming.Children.RemoveAt(0);
            if (index == current.Children.Count) current.Children.Add(next);
            else if (!Merge(current.Children[index], next))
            {
                current.Children.RemoveAt(index);
                current.Children.Insert(index, next);
            }
            index++;
        }
        while (current.Children.Count > index) current.Children.RemoveAt(current.Children.Count - 1);
    }

    private static bool Merge(UIElement current, UIElement incoming)
    {
        if (current.GetType() != incoming.GetType()) return false;
        switch (current)
        {
            case TextBlock text when incoming is TextBlock next:
                if (text.Text != next.Text) text.Text = next.Text;
                text.Foreground = next.Foreground;
                return true;
            case InfoBar bar when incoming is InfoBar next:
                bar.Title = next.Title;
                bar.Message = next.Message;
                bar.Severity = next.Severity;
                bar.IsOpen = next.IsOpen;
                return true;
            case Microsoft.UI.Xaml.Controls.Primitives.ButtonBase button when incoming is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase next:
                if (!Equals(button.Tag, next.Tag)) return false;
                if (button.Content is UIElement oldContent && next.Content is UIElement newContent)
                {
                    next.Content = null;
                    if (!Merge(oldContent, newContent)) button.Content = newContent;
                    return true;
                }
                if (button.Content is string label && next.Content is string newLabel && label == newLabel) return true;
                return false;
            case ProgressRing ring when incoming is ProgressRing next:
                ring.Value = next.Value;
                ring.IsActive = next.IsActive;
                ring.Foreground = next.Foreground;
                return true;
            case Panel panel when incoming is Panel next:
                // Une grille de composition différente doit être remplacée avec ses définitions.
                if (panel is Grid grid && next is Grid newGrid &&
                    (grid.RowDefinitions.Count != newGrid.RowDefinitions.Count || grid.ColumnDefinitions.Count != newGrid.ColumnDefinitions.Count)) return false;
                Update(panel, next);
                return true;
            case Border border when incoming is Border next:
                border.Background = next.Background;
                border.BorderBrush = next.BorderBrush;
                var child = next.Child;
                next.Child = null;
                if (border.Child is null || child is null || !Merge(border.Child, child)) border.Child = child;
                return true;
            default:
                // Conserver les nouveaux handlers des boutons (identifiant d'épisode notamment).
                return false;
        }
    }
}
