using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Bordadox.App.Controls;

/// <summary>Janela com os atalhos do teclado, agrupados por área do programa (Ajuda › Atalhos do teclado).</summary>
public static class ShortcutsDialog
{
    public static void Show(Window owner)
    {
        var list = new ListView { ItemsSource = Shortcuts.All, Margin = new Thickness(10), SelectionMode = SelectionMode.Single };
        list.Items.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Shortcuts.Entry.Group)));
        list.GroupStyle.Add(new GroupStyle { HeaderTemplate = BuildHeaderTemplate() });
        list.View = BuildGrid();

        var win = new Window
        {
            Title = "Atalhos do teclado",
            Content = list,
            Owner = owner,
            Width = 560,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        win.PreviewKeyDown += (_, e) => { if (e.Key is Key.Escape or Key.F1) win.Close(); };
        win.ShowDialog();
    }

    private static GridView BuildGrid()
    {
        var grid = new GridView();
        grid.Columns.Add(new GridViewColumn
        {
            Header = "Função",
            Width = 300,
            DisplayMemberBinding = new Binding(nameof(Shortcuts.Entry.Function)),
        });
        grid.Columns.Add(new GridViewColumn
        {
            Header = "Atalho",
            Width = 210,
            DisplayMemberBinding = new Binding(nameof(Shortcuts.Entry.Keys)),
        });
        return grid;
    }

    private static DataTemplate BuildHeaderTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        text.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        text.SetValue(TextBlock.FontSizeProperty, 14.0);
        text.SetValue(TextBlock.MarginProperty, new Thickness(0, 10, 0, 4));
        return new DataTemplate { VisualTree = text };
    }
}
