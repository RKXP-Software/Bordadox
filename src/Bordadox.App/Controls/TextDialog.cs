using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Bordadox.App.Editor;

namespace Bordadox.App.Controls;

/// <summary>Janela para criar ou editar um texto: conteúdo, fonte, altura, estilo e prévia.</summary>
public sealed class TextDialog : Window
{
    private readonly TextBox _text = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Height = 66,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Padding = new Thickness(3),
        FontSize = 14,
    };
    private readonly FontPicker _font = new();
    private readonly TextBox _height = new() { Width = 64, Padding = new Thickness(3), TextAlignment = TextAlignment.Right };
    private readonly CheckBox _bold = new() { Content = "Negrito", Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _italic = new() { Content = "Itálico", Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _preview = new() { FontSize = 26, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 84 };
    private readonly TextBlock _warning = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x5A, 0x00)), TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 3, 0, 0) };
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };

    public TextSpec? Result { get; private set; }

    public static TextSpec? Ask(Window owner, string title, TextSpec initial)
    {
        var dialog = new TextDialog(owner, title, initial);
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public TextDialog(Window? owner, string title, TextSpec initial)
    {
        Title = title;
        Owner = owner;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _text.Text = initial.Text;
        _font.SelectedFont = initial.FontFamily;
        _height.Text = initial.HeightMm.ToString("0.##", CultureInfo.CurrentCulture);
        _bold.IsChecked = initial.Bold;
        _italic.IsChecked = initial.Italic;

        var ok = new Button { Content = "OK", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = false };
        var cancel = new Button { Content = "Cancelar", Width = 90, IsCancel = true };
        ok.Click += (_, _) => Confirm();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal };
        sizeRow.Children.Add(_height);
        sizeRow.Children.Add(new TextBlock { Text = "mm", Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        sizeRow.Children.Add(_bold);
        sizeRow.Children.Add(_italic);

        var previewBox = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF4, 0xEC)),
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6, 10, 6),
            MinHeight = 56,
            Child = _preview,
        };

        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(_error);
        panel.Children.Add(Label("Texto (Enter cria uma nova linha · Ctrl+Enter confirma)"));
        panel.Children.Add(_text);
        panel.Children.Add(Label("Fonte"));
        panel.Children.Add(_font);
        panel.Children.Add(Label("Altura das letras maiúsculas"));
        panel.Children.Add(sizeRow);
        panel.Children.Add(_warning);
        panel.Children.Add(Label("Prévia da fonte"));
        panel.Children.Add(previewBox);
        panel.Children.Add(buttons);
        Content = panel;

        _text.TextChanged += (_, _) => UpdatePreview();
        _font.FontChosen += _ => UpdatePreview();
        _bold.Click += (_, _) => UpdatePreview();
        _italic.Click += (_, _) => UpdatePreview();
        _height.TextChanged += (_, _) => UpdateWarning();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { Confirm(); e.Handled = true; }
        };
        Loaded += (_, _) => { _text.Focus(); _text.SelectAll(); };

        UpdatePreview();
        UpdateWarning();
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, Foreground = Brushes.DimGray, FontSize = 11, Margin = new Thickness(0, 10, 0, 3) };

    private void UpdatePreview()
    {
        _preview.Text = _text.Text.Trim().Length > 0 ? _text.Text : "Prévia do texto";
        _preview.Opacity = _text.Text.Trim().Length > 0 ? 1 : 0.4;
        if (_font.SelectedFont.Length > 0) _preview.FontFamily = FontPicker.FamilyFor(_font.SelectedFont);
        _preview.FontWeight = _bold.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
        _preview.FontStyle = _italic.IsChecked == true ? FontStyles.Italic : FontStyles.Normal;
    }

    private void UpdateWarning()
    {
        if (!TextSpec.TryParseHeight(_height.Text, out var h)) { _warning.Text = ""; return; }
        _warning.Text = h < 5
            ? "Letras abaixo de 5 mm: use fontes simples e fortes (sem serifa fina nem estilo cursivo)."
            : h < 6 ? "5 a 6 mm é o mínimo confortável para a maioria das fontes." : "";
    }

    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(_text.Text)) { Fail("Digite o texto."); _text.Focus(); return; }
        if (!TextSpec.TryParseHeight(_height.Text, out var height))
        {
            Fail($"A altura deve estar entre {TextSpec.MinHeightMm:0} e {TextSpec.MaxHeightMm:0} mm.");
            _height.Focus();
            _height.SelectAll();
            return;
        }
        if (_font.SelectedFont.Length == 0) { Fail("Escolha uma fonte."); return; }

        Result = new TextSpec(_text.Text.TrimEnd('\r', '\n'), _font.SelectedFont, height, _bold.IsChecked == true, _italic.IsChecked == true);

        var prefs = UserPrefs.Current;
        prefs.LastFont = Result.FontFamily;
        prefs.LastHeight = Result.HeightMm;
        prefs.LastBold = Result.Bold;
        prefs.LastItalic = Result.Italic;
        prefs.Save();

        DialogResult = true;
    }

    private void Fail(string message) => _error.Text = message;

    /// <summary>Para testes visuais: mostra a janela (e, se pedido, a lista de fontes aberta) e salva imagens.</summary>
    internal static void CaptureTo(Window owner, string path, bool withFontList)
    {
        var dialog = new TextDialog(owner, "Texto", new TextSpec("Bordadox\nbordados", "Arial", 8, false, false));
        dialog.Show();
        if (withFontList) dialog._font.OpenPopup();
        dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            dialog.UpdateLayout();
            Render((FrameworkElement)dialog.Content, path);
            if (withFontList) Render(dialog._font.PopupContent, Path.ChangeExtension(path, null) + "_lista.png");
            dialog.Close();
            owner.Close();
        });
    }

    private static void Render(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var bmp = new RenderTargetBitmap(Math.Max(1, (int)element.ActualWidth), Math.Max(1, (int)element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(element);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
