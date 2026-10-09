using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Bordadox.Core.Design;

namespace Bordadox.App.Controls;

/// <summary>
/// Painel de propriedades montado por reflexão. Usa [Category], [DisplayName], [Browsable] e [Range].
/// Com vários objetos do mesmo tipo selecionados, a edição vale para todos.
/// </summary>
public sealed class PropertyEditor : ContentControl
{
    private object[] _targets = Array.Empty<object>();

    /// <summary>Chamado antes de alterar um valor (para registrar o desfazer) e depois de aplicar.</summary>
    public event Action? BeforeChange;
    public event Action? AfterChange;

    public void SetTargets(IEnumerable<object> targets)
    {
        _targets = targets.ToArray();
        Rebuild();
    }

    public void Refresh() => Rebuild();

    private void Rebuild()
    {
        var panel = new StackPanel { Margin = new Thickness(4, 0, 4, 8) };
        Content = panel;
        if (_targets.Length == 0) return;

        var type = _targets[0].GetType();
        if (_targets.Any(t => t.GetType() != type))
        {
            panel.Children.Add(Hint("Objetos de tipos diferentes selecionados."));
            return;
        }

        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.CanRead && p.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false)
            .Where(p => p.PropertyType == typeof(double) || p.PropertyType == typeof(bool) ||
                        p.PropertyType == typeof(string) || p.PropertyType.IsEnum)
            .GroupBy(p => p.GetCustomAttribute<CategoryAttribute>()?.Category ?? "Geral");

        foreach (var group in props)
        {
            panel.Children.Add(new TextBlock
            {
                Text = group.Key,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                Margin = new Thickness(2, 10, 2, 4),
            });
            foreach (var prop in group) panel.Children.Add(MakeRow(prop));
        }
    }

    private static TextBlock Hint(string text) =>
        new() { Text = text, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };

    private FrameworkElement MakeRow(PropertyInfo prop)
    {
        var label = prop.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? prop.Name;
        var values = _targets.Select(t => prop.GetValue(t)).ToList();
        bool mixed = values.Distinct().Count() > 1;

        if (prop.PropertyType == typeof(bool))
        {
            var cb = new CheckBox
            {
                Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
                IsChecked = mixed ? null : (bool)values[0]!,
                Margin = new Thickness(4, 3, 4, 3),
            };
            cb.Click += (_, _) => Apply(prop, cb.IsChecked == true);
            return cb;
        }

        var hint = prop.GetCustomAttribute<EditorHintAttribute>()?.Kind;
        if (hint == "multiline" && prop.PropertyType == typeof(string))
        {
            var box = new TextBox
            {
                Text = mixed ? "" : (string?)values[0] ?? "",
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 54,
                MaxHeight = 140,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                ToolTip = "Enter cria uma nova linha. A alteração vale ao sair da caixa.",
            };
            box.LostKeyboardFocus += (_, _) => Apply(prop, box.Text);
            var stack = new StackPanel { Margin = new Thickness(4, 2, 4, 4) };
            stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });
            stack.Children.Add(box);
            return stack;
        }

        var grid = new Grid { Margin = new Thickness(4, 2, 4, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(prop.PropertyType == typeof(string) ? 120 : 80) });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 6, 0) };
        grid.Children.Add(text);

        FrameworkElement editor;
        if (prop.PropertyType.IsEnum)
        {
            var combo = new ComboBox();
            foreach (var v in Enum.GetValues(prop.PropertyType))
            {
                var desc = prop.PropertyType.GetField(v.ToString()!)?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? v.ToString();
                combo.Items.Add(new ComboBoxItem { Content = desc, Tag = v });
            }
            if (!mixed) combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, values[0]));
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is ComboBoxItem { Tag: { } v }) Apply(prop, v);
            };
            editor = combo;
        }
        else if (prop.PropertyType == typeof(double))
        {
            var range = prop.GetCustomAttribute<RangeAttribute>() ?? new RangeAttribute(double.MinValue, double.MaxValue, 0.1);
            var box = new TextBox
            {
                Text = mixed ? "" : Format((double)values[0]!),
                TextAlignment = TextAlignment.Right,
                ToolTip = $"De {Format(range.Min)} a {Format(range.Max)}. Roda do mouse ou setas ajustam.",
            };
            void Commit()
            {
                if (TryParse(box.Text, out var v))
                {
                    v = Math.Clamp(v, range.Min, range.Max);
                    box.Text = Format(v);
                    Apply(prop, v);
                }
            }
            void Step(int dir)
            {
                var cur = TryParse(box.Text, out var v) ? v : (double)values[0]!;
                box.Text = Format(Math.Clamp(Math.Round((cur + dir * range.Step) / range.Step) * range.Step, range.Min, range.Max));
                Commit();
            }
            box.LostKeyboardFocus += (_, _) => Commit();
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
                else if (e.Key == Key.Up) { Step(1); e.Handled = true; }
                else if (e.Key == Key.Down) { Step(-1); e.Handled = true; }
            };
            box.MouseWheel += (_, e) => { if (box.IsKeyboardFocusWithin) { Step(e.Delta > 0 ? 1 : -1); e.Handled = true; } };
            editor = box;
        }
        else if (hint == "font")
        {
            var picker = new FontPicker { SelectedFont = mixed ? "" : (string?)values[0] ?? "" };
            picker.FontChosen += name => Apply(prop, name);
            editor = picker;
        }
        else
        {
            var box = new TextBox { Text = mixed ? "" : (string?)values[0] ?? "" };
            box.LostKeyboardFocus += (_, _) => Apply(prop, box.Text);
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Apply(prop, box.Text); e.Handled = true; } };
            editor = box;
        }
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        return grid;
    }

    private void Apply(PropertyInfo prop, object value)
    {
        if (_targets.All(t => Equals(prop.GetValue(t), value))) return;
        BeforeChange?.Invoke();
        foreach (var t in _targets) prop.SetValue(t, value);
        AfterChange?.Invoke();
    }

    private static string Format(double v) => v.ToString("0.###", CultureInfo.CurrentCulture);

    private static bool TryParse(string s, out double v) =>
        double.TryParse(s.Replace('.', ','), NumberStyles.Float, new CultureInfo("pt-BR"), out v) ||
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}
