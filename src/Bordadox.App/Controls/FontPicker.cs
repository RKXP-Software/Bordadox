using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Bordadox.App.Text;

namespace Bordadox.App.Controls;

/// <summary>
/// Seletor de fonte: um botão com o nome da fonte atual que abre uma lista com busca.
/// A lista é virtualizada (só desenha o que aparece) e cada nome é mostrado na própria fonte.
/// </summary>
public sealed class FontPicker : ContentControl
{
    private static readonly Dictionary<string, FontFamily> Families = new(StringComparer.OrdinalIgnoreCase);

    private readonly Button _button = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6, 3, 6, 3) };
    private readonly TextBlock _label = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly Popup _popup = new() { StaysOpen = false, Placement = PlacementMode.Bottom };
    private readonly TextBox _search = new() { Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(3) };
    private readonly ListBox _list = new() { Height = 260 };
    private readonly TextBlock _count = new() { Margin = new Thickness(0, 4, 0, 0), Foreground = Brushes.Gray, FontSize = 11 };
    private readonly Border _popupBox = new();
    private string _selected = "";
    private bool _refreshing;

    /// <summary>O usuário escolheu uma fonte na lista.</summary>
    public event Action<string>? FontChosen;

    public FontPicker()
    {
        var arrow = new TextBlock { Text = "▾", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var dock = new DockPanel();
        DockPanel.SetDock(arrow, Dock.Right);
        dock.Children.Add(arrow);
        dock.Children.Add(_label);
        _button.Content = dock;
        _button.Click += (_, _) => OpenPopup();
        Content = _button;

        // Cada item usa a própria fonte; só os itens visíveis são criados (virtualização).
        var template = new DataTemplate();
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding("."));
        text.SetBinding(TextBlock.FontFamilyProperty, new Binding(".") { Converter = NameToFontFamily.Instance });
        text.SetValue(TextBlock.FontSizeProperty, 16.0);
        text.SetValue(TextBlock.MarginProperty, new Thickness(4, 1, 4, 1));
        // Altura fixa: fontes com linha muito alta (as árabes, por exemplo) não esticam a lista.
        text.SetValue(TextBlock.LineHeightProperty, 22.0);
        text.SetValue(TextBlock.LineStackingStrategyProperty, LineStackingStrategy.BlockLineHeight);
        template.VisualTree = text;
        _list.ItemTemplate = template;
        VirtualizingPanel.SetIsVirtualizing(_list, true);
        VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(_list, true);

        var panel = new StackPanel { MinWidth = 300 };
        panel.Children.Add(new TextBlock { Text = "Buscar fonte (ex.: \"arial black\")", Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(_search);
        panel.Children.Add(_list);
        panel.Children.Add(_count);
        _popupBox.Child = panel;
        _popupBox.Background = Brushes.White;
        _popupBox.BorderBrush = Brushes.Gray;
        _popupBox.BorderThickness = new Thickness(1);
        _popupBox.Padding = new Thickness(6);
        _popup.Child = _popupBox;
        _popup.PlacementTarget = _button;

        _search.TextChanged += (_, _) => Refresh();
        _search.PreviewKeyDown += Search_KeyDown;
        _list.PreviewMouseLeftButtonUp += List_MouseUp;
        _list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _list.SelectedItem is string name) { Choose(name); e.Handled = true; }
            else if (e.Key == Key.Escape) { _popup.IsOpen = false; e.Handled = true; }
        };
    }

    public string SelectedFont
    {
        get => _selected;
        set
        {
            _selected = value ?? "";
            _label.Text = _selected.Length > 0 ? _selected : "(vários)";
            if (_selected.Length > 0) _label.FontFamily = FamilyFor(_selected);
        }
    }

    /// <summary>Conteúdo da lista aberta (usado para tirar capturas de tela de teste).</summary>
    internal FrameworkElement PopupContent => _popupBox;

    internal void OpenPopup()
    {
        _popupBox.Width = Math.Max(ActualWidth, 320);
        _search.Text = "";
        _popup.IsOpen = true;
        Refresh();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            _search.Focus();
            if (_list.SelectedItem != null) _list.ScrollIntoView(_list.SelectedItem);
        });
    }

    private void Refresh()
    {
        var names = FontCatalog.Names;
        var items = FontCatalog.Search(names, _search.Text);
        _refreshing = true;
        _list.ItemsSource = items;
        var current = items.FindIndex(n => n.Equals(_selected, StringComparison.OrdinalIgnoreCase));
        _list.SelectedIndex = _search.Text.Length == 0 ? current : (items.Count > 0 ? 0 : -1);
        _refreshing = false;
        _count.Text = $"{items.Count:N0} de {names.Count:N0} fontes" + (FontCatalog.IsScanComplete ? "" : " — verificando as fontes em segundo plano...");
    }

    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _list.Focus();
                if (_list.SelectedIndex < 0 && _list.Items.Count > 0) _list.SelectedIndex = 0;
                (_list.ItemContainerGenerator.ContainerFromIndex(Math.Max(0, _list.SelectedIndex)) as ListBoxItem)?.Focus();
                e.Handled = true;
                break;
            case Key.Enter:
                if (_list.SelectedItem is string first) Choose(first);
                e.Handled = true;
                break;
            case Key.Escape:
                _popup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void List_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_refreshing) return;
        var item = (e.OriginalSource as DependencyObject)?.FindParent<ListBoxItem>();
        if (item?.DataContext is string name) Choose(name);
    }

    private void Choose(string name)
    {
        SelectedFont = name;
        _popup.IsOpen = false;
        FontChosen?.Invoke(name);
    }

    /// <summary>FontFamily guardada por nome (criar uma por item seria caro).</summary>
    public static FontFamily FamilyFor(string name)
    {
        lock (Families)
        {
            if (!Families.TryGetValue(name, out var family))
                Families[name] = family = new FontFamily(name);
            return family;
        }
    }

    private sealed class NameToFontFamily : IValueConverter
    {
        public static readonly NameToFontFamily Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is string name ? FamilyFor(name) : DependencyProperty.UnsetValue;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}

internal static class VisualTreeExtensions
{
    public static T? FindParent<T>(this DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T t) return t;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }
}
