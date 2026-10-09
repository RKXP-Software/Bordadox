using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Bordadox.App.Digitize;
using Bordadox.Core.Design;
using Bordadox.Core.Digitize;
using Bordadox.Core.Machine;
using Bordadox.Core.Stitches;

namespace Bordadox.App.Controls;

/// <summary>O que a janela devolve quando o usuário confirma.</summary>
/// <param name="Result">Objetos gerados; null quando a imagem só vira fundo (<paramref name="BackgroundOnly"/>).</param>
public sealed record DigitizeOutcome(DigitizeResult? Result, double WidthMm, double HeightMm, int StitchCount, byte[]? ReferencePng, double ReferenceAspect,
    bool BackgroundOnly = false);

/// <summary>
/// Janela "Digitalizar imagem": prévia com as cores do bordado, tamanho, número de cores e avisos de risco.
/// A prévia é calculada em segundo plano numa versão reduzida; a digitalização completa só roda ao confirmar.
/// </summary>
public sealed class DigitizeDialog : Window
{
    // Acima disto o projeto fica pesado: pede confirmação do usuário antes de entrar no desenho.
    private const int HeavyObjects = 300;
    private const int HeavyStitches = 120_000;

    private readonly LoadedImage _loaded;
    private readonly ImageAnalysis _analysis;
    private readonly Hoop _hoop;
    private readonly BitmapSource _original;

    private readonly Image _image = CreatePreviewImage();
    private readonly RadioButton _showPreview = new() { Content = "Cores do bordado", IsChecked = true, Margin = new Thickness(0, 0, 14, 0), GroupName = "view" };
    private readonly RadioButton _showOriginal = new() { Content = "Imagem original", GroupName = "view" };
    private readonly TextBox _width = new() { Width = 70, TextAlignment = TextAlignment.Right, Padding = new Thickness(3) };
    private readonly TextBox _height = new() { Width = 70, TextAlignment = TextAlignment.Right, Padding = new Thickness(3) };
    private readonly TextBlock _sizeNote = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly CheckBox _autoColors = new() { Content = "Automático (une cores parecidas)", IsChecked = true };
    private readonly Slider _colors = new() { Minimum = 2, Maximum = 12, Value = 8, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 190 };
    private readonly TextBlock _colorsLabel = new() { Width = 70, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _mergeShades = new()
    {
        Content = new TextBlock { Text = "Unir tons do mesmo degradê (ex.: dourado metálico vira uma cor só)", TextWrapping = TextWrapping.Wrap },
        IsChecked = true,
    };
    private readonly CheckBox _removeBg = new() { Content = "Remover o fundo", IsChecked = true };
    private readonly CheckBox _enclosedBg = new()
    {
        Content = new TextBlock { Text = "Áreas internas da cor do fundo viram tecido, sem ponto (miolo do O, do A...)", TextWrapping = TextWrapping.Wrap },
        IsChecked = true,
    };
    private readonly NumberBox _detail = new() { Minimum = 0, Maximum = 100, Step = 0.1, Value = 0.6 };
    private readonly TextBlock _detailLabel = new() { Text = " mm", VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _fillMode = new();
    private readonly CheckBox _keepReference = new() { Content = "Manter a imagem como fundo de referência" };
    // Zoom e deslocamento da prévia (a imagem ocupa a área inteira; a escala é em torno do cursor).
    private readonly ScaleTransform _zoom = new(1, 1);
    private readonly TranslateTransform _pan = new();
    private readonly TextBlock _zoomLabel = new() { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray };
    private Border _previewBorder = null!;
    private Point? _dragStart;
    private const double MaxZoom = 24;

    private readonly CheckBox _bgOnly = new()
    {
        Content = new TextBlock { Text = "Só usar como fundo (não gerar bordado), para desenhar por cima", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold },
    };
    private readonly List<UIElement> _digitizeOnly = new(); // controles que só valem quando se gera bordado
    private readonly StackPanel _swatches = new();
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };

    private readonly Border _riskBox = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(10), Margin = new Thickness(0, 10, 0, 0), BorderThickness = new Thickness(1) };
    private readonly CheckBox _riskAck = new() { Margin = new Thickness(0, 8, 0, 0), FontWeight = FontWeights.SemiBold, Content = new TextBlock { Text = "Entendo os riscos e quero continuar mesmo assim", TextWrapping = TextWrapping.Wrap } };

    private readonly StackPanel _progressPanel = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 10 };
    private readonly TextBlock _progressText = new() { Margin = new Thickness(0, 4, 0, 0), FontSize = 11 };
    private readonly Button _ok = new() { Content = "Digitalizar", Width = 110, Margin = new Thickness(0, 0, 8, 0), FontWeight = FontWeights.SemiBold };
    private readonly Button _cancel = new() { Content = "Cancelar", Width = 90 };
    private readonly Panel _options = new StackPanel();

    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _runCts;
    private int _previewVersion;
    private double _aspect = 1;          // altura / largura em uso (do desenho sem a margem, ou da imagem inteira no modo "só fundo")
    private double _fullAspect = 1;      // altura / largura da imagem inteira
    private double _contentAspect = 1;   // altura / largura do desenho, sem a margem de fundo (vem da prévia)
    private bool _backgroundOnly;
    private bool _syncing;
    private bool _userSized;             // o usuário já mexeu no tamanho: não reajusta sozinho
    private bool _busy;
    private BitmapSource? _previewBitmap;
    private double _estimatedStitches;
    private bool _previewReady;

    public DigitizeOutcome? Outcome { get; private set; }

    public static DigitizeOutcome? Ask(Window owner, LoadedImage image, Hoop hoop)
    {
        var dialog = new DigitizeDialog(owner, image, hoop);
        return dialog.ShowDialog() == true ? dialog.Outcome : null;
    }

    public DigitizeDialog(Window? owner, LoadedImage image, Hoop hoop)
    {
        _loaded = image;
        _hoop = hoop;
        _analysis = ImageAnalyzer.Analyze(image.Image);
        _original = ImageLoader.ToBitmap(image.Image.Limit(800));
        _aspect = _fullAspect = _contentAspect = image.Image.Height / (double)image.Image.Width;

        Title = $"Digitalizar imagem — {image.Name}";
        Owner = owner;
        Width = 1020;
        Height = 700;
        MinWidth = 860;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _fillMode.Items.Add(new ComboBoxItem { Content = "Automático (satin nas partes estreitas)", Tag = FillMode.Auto });
        _fillMode.Items.Add(new ComboBoxItem { Content = "Só tatami", Tag = FillMode.Tatami });
        _fillMode.Items.Add(new ComboBoxItem { Content = "Só satin", Tag = FillMode.Satin });
        _fillMode.SelectedIndex = 0;

        BuildLayout();
        WireEvents();

        // Tamanho inicial: 100 mm, ou o máximo que cabe no bastidor.
        FitToHoop(maxWidth: 100);
        ShowRiskBoxIfNeeded();
        UpdateOkEnabled();
        _image.Source = _original;
        _debounce.Tick += (_, _) => { _debounce.Stop(); RefreshPreview(); };
        Loaded += (_, _) => RefreshPreview();
        Closed += (_, _) => { _previewCts?.Cancel(); _runCts?.Cancel(); _debounce.Stop(); };
    }

    // ---------- Layout ----------

    private void BuildLayout()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });

        // Esquerda: prévia sobre fundo xadrez (mostra o que é transparente/sem ponto).
        var left = new DockPanel { Margin = new Thickness(12) };
        var toggles = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        toggles.Children.Add(_showPreview);
        toggles.Children.Add(_showOriginal);
        DockPanel.SetDock(toggles, Dock.Bottom);
        left.Children.Add(toggles);
        _previewBorder = new Border { Background = Checker(), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = _image, ClipToBounds = true };
        _image.RenderTransform = new TransformGroup { Children = { _zoom, _pan } };
        _image.RenderTransformOrigin = new Point(0.5, 0.5);
        _previewBorder.MouseWheel += Preview_MouseWheel;
        _previewBorder.MouseLeftButtonDown += Preview_MouseDown;
        _previewBorder.MouseMove += Preview_MouseMove;
        _previewBorder.MouseLeftButtonUp += (_, _) => { _dragStart = null; _previewBorder.ReleaseMouseCapture(); _previewBorder.Cursor = null; };
        _previewBorder.ToolTip = "Roda do mouse: zoom · arraste: mover · duplo-clique: ajustar à janela";
        left.Children.Add(_previewBorder);
        root.Children.Add(left);

        // Botões de zoom ao lado das opções de visualização.
        Button ZoomButton(string text, string tip, Action action)
        {
            var b = new Button { Content = text, Width = 30, Margin = new Thickness(4, 0, 0, 0), ToolTip = tip, Focusable = false };
            b.Click += (_, _) => action();
            return b;
        }
        toggles.Children.Add(new TextBlock { Text = "      Zoom:", VerticalAlignment = VerticalAlignment.Center });
        toggles.Children.Add(ZoomButton("−", "Diminuir o zoom", () => ZoomBy(1 / 1.25, null)));
        toggles.Children.Add(ZoomButton("+", "Aumentar o zoom", () => ZoomBy(1.25, null)));
        var fitButton = ZoomButton("Ajustar", "Mostrar a imagem inteira", ResetZoom);
        fitButton.Width = 58;
        toggles.Children.Add(fitButton);
        toggles.Children.Add(_zoomLabel);

        // Direita: opções.
        var right = new DockPanel { Margin = new Thickness(0, 12, 12, 12) };
        Grid.SetColumn(right, 1);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(_ok);
        buttons.Children.Add(_cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        right.Children.Add(buttons);

        var panel = (StackPanel)_options;
        panel.Children.Add(_bgOnly);
        panel.Children.Add(Header("Tamanho do desenho"));
        var size = new StackPanel { Orientation = Orientation.Horizontal };
        size.Children.Add(_width);
        size.Children.Add(new TextBlock { Text = " × ", VerticalAlignment = VerticalAlignment.Center });
        size.Children.Add(_height);
        size.Children.Add(new TextBlock { Text = " mm", VerticalAlignment = VerticalAlignment.Center });
        var fit = new Button { Content = "Ajustar ao bastidor", Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
        fit.Click += (_, _) => FitToHoop(maxWidth: double.MaxValue);
        size.Children.Add(fit);
        panel.Children.Add(size);
        panel.Children.Add(_sizeNote);

        panel.Children.Add(Header("Cores"));
        panel.Children.Add(_autoColors);
        _mergeShades.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(_mergeShades);
        var colorsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        colorsRow.Children.Add(new TextBlock { Text = "Até ", VerticalAlignment = VerticalAlignment.Center });
        colorsRow.Children.Add(_colors);
        colorsRow.Children.Add(_colorsLabel);
        panel.Children.Add(colorsRow);

        panel.Children.Add(Header("Fundo"));
        panel.Children.Add(_removeBg);
        _enclosedBg.Margin = new Thickness(0, 4, 0, 0);
        panel.Children.Add(_enclosedBg);

        panel.Children.Add(Header("Detalhes e costura"));
        var detailRow = new StackPanel { Orientation = Orientation.Horizontal };
        detailRow.Children.Add(new TextBlock { Text = "Ignorar < ", VerticalAlignment = VerticalAlignment.Center });
        detailRow.Children.Add(_detail);
        detailRow.Children.Add(_detailLabel);
        panel.Children.Add(detailRow);
        panel.Children.Add(new TextBlock { Text = "Tipo de preenchimento", Margin = new Thickness(0, 8, 0, 3), Foreground = Brushes.DimGray, FontSize = 11 });
        panel.Children.Add(_fillMode);
        _keepReference.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(_keepReference);

        panel.Children.Add(Header("Resultado previsto"));
        panel.Children.Add(_swatches);
        panel.Children.Add(_summary);

        // Tudo isto some do jogo no modo "só fundo": nada é digitalizado.
        _digitizeOnly.AddRange(new UIElement[] { _autoColors, _mergeShades, _colors, _removeBg, _enclosedBg, _detail, _fillMode, _keepReference, _swatches });

        _riskBox.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xE0));
        _riskBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x50));
        panel.Children.Add(_riskBox);

        _progressPanel.Children.Add(_progress);
        _progressPanel.Children.Add(_progressText);
        panel.Children.Add(_progressPanel);

        right.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel, Padding = new Thickness(0, 0, 8, 0) });
        root.Children.Add(right);
        Content = root;
    }

    private static Image CreatePreviewImage()
    {
        var image = new Image { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality); // ampliação suave, sem "degraus" de pixel
        return image;
    }

    private static TextBlock Header(string text) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 5), Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)) };

    private static Brush Checker()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        var grey = new GeometryGroup();
        grey.Children.Add(new RectangleGeometry(new Rect(0, 0, 8, 8)));
        grey.Children.Add(new RectangleGeometry(new Rect(8, 8, 8, 8)));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4)), null, grey));
        var brush = new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }

    // ---------- Eventos ----------

    private void WireEvents()
    {
        _width.TextChanged += (_, _) => { if (!_syncing) { _userSized = true; SizeEdited(fromWidth: true); } };
        _height.TextChanged += (_, _) => { if (!_syncing) { _userSized = true; SizeEdited(fromWidth: false); } };
        _autoColors.Click += (_, _) => { _mergeShades.IsEnabled = _autoColors.IsChecked == true; UpdateLabels(); QueuePreview(); };
        _mergeShades.Click += (_, _) => QueuePreview();
        _colors.ValueChanged += (_, _) => { UpdateLabels(); QueuePreview(); };
        _removeBg.Click += (_, _) => { _enclosedBg.IsEnabled = _removeBg.IsChecked == true; QueuePreview(); };
        _enclosedBg.Click += (_, _) => QueuePreview();
        _detail.ValueChanged += (_, _) => { UpdateLabels(); QueuePreview(); };
        _fillMode.SelectionChanged += (_, _) => QueuePreview();
        _showPreview.Click += (_, _) => ShowView();
        _showOriginal.Click += (_, _) => ShowView();
        _riskAck.Click += (_, _) => UpdateOkEnabled();
        _bgOnly.Click += (_, _) => SetBackgroundOnly(_bgOnly.IsChecked == true);
        _ok.Click += (_, _) => StartDigitizing();
        _cancel.Click += (_, _) => { if (_busy) _runCts?.Cancel(); else { DialogResult = false; } };
        UpdateLabels();
    }

    private void UpdateLabels()
    {
        _colors.IsEnabled = true;
        _colorsLabel.Text = _autoColors.IsChecked == true ? $"{(int)_colors.Value} no máx." : $"{(int)_colors.Value} exatas";
    }

    /// <summary>Liga/desliga o modo em que a imagem só vira fundo de referência, sem gerar bordado.</summary>
    private void SetBackgroundOnly(bool on)
    {
        _backgroundOnly = on;
        foreach (var c in _digitizeOnly) c.IsEnabled = !on;
        _mergeShades.IsEnabled = !on && _autoColors.IsChecked == true;
        _showPreview.IsEnabled = !on;
        _riskBox.Visibility = on || _riskBox.Child == null ? Visibility.Collapsed : Visibility.Visible;
        _ok.Content = on ? "Inserir como fundo" : "Digitalizar";

        // No modo "só fundo" a largura vale para a imagem inteira (com a margem); senão, só para o desenho.
        _aspect = on ? _fullAspect : _contentAspect;
        if (_userSized) SizeEdited(fromWidth: true); else FitToHoop(maxWidth: 100);
        if (on) { _showOriginal.IsChecked = true; ShowView(); }
        else { _showPreview.IsChecked = true; ShowView(); QueuePreview(); }
        UpdateSizeNote();
        UpdateSummary();
        UpdateOkEnabled();
    }

    private void UpdateSummary()
    {
        if (!_backgroundOnly) return;
        _summary.Text = $"A imagem inteira ({WidthMm:0.#} × {WidthMm * _fullAspect:0.#} mm) vai para trás do desenho, sem gerar nenhum ponto. " +
                        "Use as ferramentas de desenho por cima; ela fica salva no projeto e pode ser ocultada ou removida em Arquivo › Imagem de fundo.";
    }

    private void QueuePreview()
    {
        if (_backgroundOnly) { UpdateSizeNote(); UpdateSummary(); return; } // nada a recalcular

        _debounce.Stop();
        _debounce.Start();
    }

    // ---------- Zoom da prévia ----------

    private void Preview_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        ZoomBy(Math.Pow(1.2, e.Delta / 120.0), e.GetPosition(_previewBorder));
        e.Handled = true;
    }

    private void Preview_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ResetZoom(); return; }
        if (_zoom.ScaleX <= 1.0001) return; // sem zoom não há o que mover
        _dragStart = e.GetPosition(_previewBorder);
        _previewBorder.CaptureMouse();
        _previewBorder.Cursor = Cursors.SizeAll;
    }

    private void Preview_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start) return;
        var p = e.GetPosition(_previewBorder);
        _pan.X += p.X - start.X;
        _pan.Y += p.Y - start.Y;
        _dragStart = p;
        ClampPan();
    }

    /// <summary>Aumenta ou diminui o zoom mantendo fixo o ponto sob o cursor (ou o centro, se <paramref name="around"/> for nulo).</summary>
    private void ZoomBy(double factor, Point? around)
    {
        double s = _zoom.ScaleX, ns = Math.Clamp(s * factor, 1, MaxZoom);
        if (Math.Abs(ns - s) < 1e-9) return;
        double w = _previewBorder.ActualWidth, h = _previewBorder.ActualHeight;
        var m = around ?? new Point(w / 2, h / 2);
        double k = ns / s;
        // Ponto fixo: m = c + (q − c)·s + pan  →  novo pan = (m − c) − ((m − c) − pan)·k, com c = centro da área.
        _pan.X = (m.X - w / 2) - ((m.X - w / 2) - _pan.X) * k;
        _pan.Y = (m.Y - h / 2) - ((m.Y - h / 2) - _pan.Y) * k;
        _zoom.ScaleX = _zoom.ScaleY = ns;
        ClampPan();
        _zoomLabel.Text = ns <= 1.0001 ? "" : $"{ns * 100:0}%";
    }

    private void ResetZoom()
    {
        _zoom.ScaleX = _zoom.ScaleY = 1;
        _pan.X = _pan.Y = 0;
        _zoomLabel.Text = "";
    }

    /// <summary>Não deixa a imagem sair toda da área: no máximo metade do que sobra do zoom pode passar da borda.</summary>
    private void ClampPan()
    {
        double s = _zoom.ScaleX;
        double maxX = Math.Max(0, (_previewBorder.ActualWidth * s - _previewBorder.ActualWidth) / 2);
        double maxY = Math.Max(0, (_previewBorder.ActualHeight * s - _previewBorder.ActualHeight) / 2);
        _pan.X = Math.Clamp(_pan.X, -maxX, maxX);
        _pan.Y = Math.Clamp(_pan.Y, -maxY, maxY);
    }

    private void ShowView() => _image.Source = _showOriginal.IsChecked == true || _previewBitmap == null ? _original : _previewBitmap;

    // ---------- Tamanho ----------

    private static bool TryParse(string s, out double v) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out v) ||
        double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private void SizeEdited(bool fromWidth)
    {
        if (!TryParse((fromWidth ? _width : _height).Text, out var v) || v <= 0) return;
        _syncing = true;
        if (fromWidth) _height.Text = Format(v * _aspect); else _width.Text = Format(v / _aspect);
        _syncing = false;
        UpdateSizeNote();
        QueuePreview(); // o tamanho muda o que "detalhe pequeno" significa
    }

    private static string Format(double v) => v.ToString("0.#", CultureInfo.CurrentCulture);

    private double WidthMm => TryParse(_width.Text, out var v) ? v : 0;

    private void FitToHoop(double maxWidth)
    {
        double w = Math.Min(maxWidth, Math.Min(_hoop.WidthMm * 0.96, _hoop.HeightMm * 0.96 / _aspect));
        _syncing = true;
        _width.Text = Format(Math.Floor(w * 2) / 2);
        _height.Text = Format(Math.Floor(w * 2) / 2 * _aspect);
        _syncing = false;
        UpdateSizeNote();
    }

    private void UpdateSizeNote()
    {
        double w = WidthMm, h = w * _aspect;
        if (w <= 0) { _sizeNote.Text = "Informe uma largura."; return; }
        bool fits = w <= _hoop.WidthMm && h <= _hoop.HeightMm;
        _sizeNote.Foreground = fits ? Brushes.DimGray : Brushes.Firebrick;
        _sizeNote.Text = fits
            ? $"Bastidor atual: {_hoop.WidthMm:0} × {_hoop.HeightMm:0} mm. " +
              (_backgroundOnly ? "A largura vale para a imagem inteira." : "A largura vale para o desenho, sem a margem da imagem.")
            : $"Maior que o bastidor atual ({_hoop.WidthMm:0} × {_hoop.HeightMm:0} mm). Reduza o tamanho ou troque o bastidor depois.";
        if (w < 20) _sizeNote.Text += " Abaixo de 20 mm os detalhes finos se perdem.";
    }

    // ---------- Prévia ----------

    private QuantizeOptions CurrentQuantize() => new()
    {
        MaxColors = (int)_colors.Value,
        Auto = _autoColors.IsChecked == true,
        RemoveBackground = _removeBg.IsChecked == true,
        RemoveEnclosedBackground = _enclosedBg.IsChecked == true,
    };

    private DigitizeOptions CurrentOptions()
    {
        var o = new DigitizeOptions
        {
            WidthMm = WidthMm,
            MinDetailMm = _detail.Value,
            FillMode = (FillMode)((ComboBoxItem)_fillMode.SelectedItem).Tag,
            MergeShades = _mergeShades.IsChecked == true,
        };
        var q = CurrentQuantize();
        o.Quantize.MaxColors = q.MaxColors;
        o.Quantize.Auto = q.Auto;
        o.Quantize.RemoveBackground = q.RemoveBackground;
        o.Quantize.RemoveEnclosedBackground = q.RemoveEnclosedBackground;
        return o;
    }

    private sealed record PreviewData(BitmapSource Bitmap, List<(ThreadColor Thread, double AreaMm2)> Colors, double Aspect, double TotalAreaMm2, int Stitches);

    private void RefreshPreview()
    {
        _previewCts?.Cancel();
        var cts = _previewCts = new CancellationTokenSource();
        int version = ++_previewVersion;
        var options = CurrentOptions();
        options.WidthMm = Math.Max(1, WidthMm);
        var source = _loaded.Image;
        _summary.Text = "Calculando a prévia...";

        Task.Run(() => BuildPreview(source, options, cts.Token), cts.Token).ContinueWith(t =>
        {
            if (version != _previewVersion || t.IsCanceled) return;
            if (t.IsFaulted)
            {
                _summary.Text = "Não foi possível calcular a prévia: " + t.Exception?.GetBaseException().Message;
                return;
            }
            ApplyPreview(t.Result);
        }, cts.Token, TaskContinuationOptions.NotOnCanceled, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static PreviewData BuildPreview(RgbaImage source, DigitizeOptions options, CancellationToken ct)
    {
        // Mesmo caminho da digitalização final (recorte, tamanho, cores e limpeza): a prévia mostra o que será costurado.
        var prep = AutoDigitizer.Prepare(source, options, null, ct);
        int w = prep.Width, h = prep.Height;
        double ppm = prep.PixelsPerMm;

        var bgra = new byte[w * h * 4];
        var count = new int[prep.Threads.Count];
        for (int i = 0; i < prep.Labels.Length; i++)
        {
            int l = prep.Labels[i];
            if (l < 0) continue;
            count[l]++;
            var t = prep.Threads[l];
            bgra[i * 4] = t.B; bgra[i * 4 + 1] = t.G; bgra[i * 4 + 2] = t.R; bgra[i * 4 + 3] = 255;
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();

        var colors = Enumerable.Range(0, prep.Threads.Count).Where(i => count[i] > 0)
            .Select(i => (prep.Threads[i], count[i] / (ppm * ppm))).OrderByDescending(c => c.Item2).ToList();
        var stitches = AutoDigitizer.EstimateStitches(prep, options, ct);
        return new PreviewData(bmp, colors, prep.HeightMm / prep.WidthMm, colors.Sum(c => c.Item2), stitches);
    }

    private void ApplyPreview(PreviewData p)
    {
        _previewBitmap = p.Bitmap;
        _previewReady = true;
        _estimatedStitches = p.Stitches;
        _contentAspect = p.Aspect;
        if (_backgroundOnly) { UpdateOkEnabled(); return; } // o usuário mudou de ideia durante o cálculo: ignora a prévia

        if (Math.Abs(p.Aspect - _aspect) > 0.001)
        {
            // O recorte mudou (a margem de fundo saiu, ou "remover o fundo" foi ligado/desligado): a altura acompanha.
            _aspect = p.Aspect;
            if (_userSized) SizeEdited(fromWidth: true);
            else FitToHoop(maxWidth: 100);
        }
        ShowView();
        UpdateSizeNote();

        _swatches.Children.Clear();
        var wrap = new WrapPanel();
        foreach (var (thread, area) in p.Colors)
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 4) };
            chip.Children.Add(new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(8), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(thread.R, thread.G, thread.B)), Margin = new Thickness(0, 0, 4, 0),
            });
            chip.Children.Add(new TextBlock { Text = $"{thread.Name} · {area:0} mm²", FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
            wrap.Children.Add(chip);
        }
        _swatches.Children.Add(wrap);

        _summary.Text = p.Colors.Count == 0
            ? "Nenhuma cor encontrada: o fundo ocupa a imagem toda. Desmarque \"Remover o fundo\" ou use outra imagem."
            : $"{p.Colors.Count} cor(es) · {p.TotalAreaMm2:0} mm² de tecido bordado · aproximadamente {_estimatedStitches:N0} pontos.";
        if (_estimatedStitches > HeavyStitches)
            _summary.Text += " Isto é MUITO pesado para uma máquina doméstica.";
        UpdateOkEnabled();
    }

    // ---------- Aviso de risco ----------

    private void ShowRiskBoxIfNeeded()
    {
        var notes = new List<string>();
        if (_analysis.Kind == ImageKind.Photo)
        {
            notes.Add("Esta imagem parece uma FOTOGRAFIA (muita textura e variação de cor).");
            notes.Add("• Fotos viram manchas de poucas cores: os detalhes e as sombras se perdem.");
            notes.Add("• O resultado pode ter centenas de objetos e centenas de milhares de pontos. O projeto fica lento para editar e o arquivo fica pesado.");
            notes.Add("• Máquinas domésticas têm limite de memória e de pontos por desenho: ela pode recusar o arquivo ou travar.");
            notes.Add("• O bordado levaria horas e gastaria muita linha, com risco de franzir o tecido.");
            notes.Add("Recomendado: use um logotipo ou desenho de cores chapadas. Se mesmo assim quiser tentar, reduza para 3 a 5 cores.");
        }
        else if (_analysis.NeedsConfirmation)
        {
            notes.Add("Esta imagem tem degradês fortes (muitas cores diferentes).");
            notes.Add("• Cada degradê vira faixas de cores, e o resultado costuma ficar diferente do original.");
            notes.Add("• Muitas cores significam muitas trocas de linha e um projeto mais pesado.");
            notes.Add("Dica: reduza o número de cores para 4 a 6.");
        }
        if (_analysis.Megapixels > 16)
            notes.Add($"A imagem é muito grande ({_analysis.Megapixels:0} megapixels). Ela será reduzida; isso é normal e não afeta o bordado.");

        if (notes.Count == 0) return;

        var stack = new StackPanel();
        bool warn = _analysis.NeedsConfirmation;
        for (int i = 0; i < notes.Count; i++)
            stack.Children.Add(new TextBlock
            {
                Text = notes[i], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, i == 0 ? 0 : 3, 0, 0),
                FontWeight = i == 0 && warn ? FontWeights.Bold : FontWeights.Normal, FontSize = 12,
            });
        if (warn) stack.Children.Add(_riskAck);
        _riskBox.Child = stack;
        _riskBox.Visibility = Visibility.Visible;
        if (!warn) { _riskBox.Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF4, 0xFB)); _riskBox.BorderBrush = Brushes.LightSteelBlue; }
    }

    private void UpdateOkEnabled()
    {
        bool needsAck = _analysis.NeedsConfirmation && _riskAck.IsChecked != true && !_backgroundOnly;
        _ok.IsEnabled = !_busy && !needsAck && WidthMm >= 5 && (_previewReady || _backgroundOnly);
        _ok.ToolTip = needsAck ? "Marque a confirmação de risco para continuar." : null;
    }

    // ---------- Digitalização completa ----------

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _progressPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _cancel.Content = busy ? "Parar" : "Cancelar";
        foreach (UIElement child in _options.Children)
            if (!ReferenceEquals(child, _progressPanel)) child.IsEnabled = !busy;
        UpdateOkEnabled();
    }

    private async void StartDigitizing()
    {
        if (_backgroundOnly)
        {
            // Sem digitalizar: a imagem inteira, em PNG de boa resolução, para desenhar por cima.
            double w = WidthMm;
            if (w < 5 || w > 600) { MessageBox.Show(this, "A largura deve ficar entre 5 e 600 mm.", "Fundo", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            Outcome = new DigitizeOutcome(null, w, w * _fullAspect, 0, ImageLoader.EncodePng(_loaded.Image, 1600), _fullAspect, BackgroundOnly: true);
            DialogResult = true;
            return;
        }

        var options = CurrentOptions();
        if (options.WidthMm < 5 || options.WidthMm > 400)
        {
            MessageBox.Show(this, "A largura deve ficar entre 5 e 400 mm.", "Digitalizar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _previewCts?.Cancel();
        var cts = _runCts = new CancellationTokenSource();
        SetBusy(true);
        _progress.Value = 0;
        _progressText.Text = "Começando...";

        var source = _loaded.Image;
        var progress = new Progress<DigitizeProgress>(p => { _progress.Value = p.Fraction * 0.9; _progressText.Text = p.Stage + "..."; });
        try
        {
            var (result, stitches, croppedForReference) = await Task.Run(() =>
            {
                var r = AutoDigitizer.Run(source, options, progress, cts.Token);
                ((IProgress<DigitizeProgress>)progress).Report(new DigitizeProgress("Gerando os pontos", 0.95));
                var design = new EmbDesign();
                design.Objects.AddRange(r.Objects);
                int count = r.Objects.Count == 0 ? 0 : design.BuildPattern("digitalizado").StitchCount;
                var cropped = options.CropToContent ? AutoDigitizer.CropToContent(source, options.Quantize, cts.Token) : source;
                return (r, count, cropped);
            }, cts.Token);
            _progress.Value = 1;

            if (result.Objects.Count == 0)
            {
                MessageBox.Show(this, "Nenhum objeto foi gerado.\n\nTente reduzir \"Ignorar\" (detalhe mínimo), aumentar o tamanho ou desmarcar \"Remover o fundo\".",
                    "Digitalizar", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!ConfirmHeavy(result, stitches)) return;

            var png = _keepReference.IsChecked == true ? ImageLoader.EncodePng(croppedForReference) : null;
            Outcome = new DigitizeOutcome(result, result.WidthMm, result.HeightMm, stitches, png, croppedForReference.Height / (double)croppedForReference.Width);
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            _progressText.Text = "Cancelado.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível digitalizar a imagem:\n" + ex.Message, "Digitalizar", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (DialogResult != true) SetBusy(false);
        }
    }

    /// <summary>Depois de gerar: se o resultado ficou pesado, mostra os números e pede confirmação dos riscos.</summary>
    private bool ConfirmHeavy(DigitizeResult result, int stitches)
    {
        bool heavy = result.Objects.Count > HeavyObjects || stitches > HeavyStitches;
        if (!heavy) return true;

        var text =
            $"O resultado ficou PESADO:\n\n  • {result.Objects.Count:N0} objetos\n  • {stitches:N0} pontos\n  • {result.Layers.Count} cores\n\n" +
            "Riscos ao colocar isto no projeto:\n" +
            "  • o editor fica lento (cada alteração recalcula todos os pontos);\n" +
            "  • o arquivo para a máquina fica grande, e máquinas domésticas podem recusar ou travar;\n" +
            "  • o bordado leva muitas horas e gasta muita linha.\n\n" +
            "Dica: reduza o número de cores, aumente \"Ignorar\" (detalhe mínimo) ou diminua o tamanho.\n\n" +
            "Colocar no projeto mesmo assim?";
        return MessageBox.Show(this, text, "Resultado pesado — confirmar riscos", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    // ---------- Captura para teste visual ----------

    /// <summary>Mostra a janela, espera a prévia e salva uma imagem dela (uso interno, nos testes visuais).</summary>
    internal static void CaptureTo(Window owner, LoadedImage image, string path, bool backgroundOnly = false)
    {
        var dialog = new DigitizeDialog(owner, image, Hoops.NQ1400E[1]);
        dialog.Show();
        if (backgroundOnly) { dialog._bgOnly.IsChecked = true; dialog.SetBackgroundOnly(true); }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var element = (FrameworkElement)dialog.Content;
            element.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(element);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = System.IO.File.Create(path)) enc.Save(fs);
            dialog.Close();
            owner.Close();
        };
        timer.Start();
    }
}
