using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Bordadox.App.Controls;
using Bordadox.App.Digitize;
using Bordadox.App.Editor;
using Bordadox.Core.Design;
using Bordadox.Core.Formats;
using Bordadox.Core.Machine;
using Bordadox.Core.Stitches;
using Microsoft.Win32;

namespace Bordadox.App;

public partial class MainWindow : Window
{
    private const string ClipboardFormat = "Bordadox.Objetos";

    private readonly EditorController _editor = new();
    private readonly DispatcherTimer _simTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private bool _syncing;

    public MainWindow()
    {
        InitializeComponent();

        HoopCombo.ItemsSource = Hoops.NQ1400E;
        Palette.ItemsSource = BrotherPalette.Colors.Skip(1).Select(c => new PaletteItem(c)).ToList();

        _simTimer.Tick += SimTimer_Tick;
        Canvas.CursorMoved += (_, p) =>
            StatusCursor.Text = p is { } v ? $"X: {v.X,7:0.0} mm   Y: {v.Y,7:0.0} mm" : "";
        Canvas.ZoomChanged += (_, _) => StatusZoom.Text = $"Zoom: {Canvas.ZoomPercent:0}%";
        Canvas.Overlay = _editor.DrawOverlay;
        Canvas.WorldMouseDown += (p, b, clicks, mods, px) => _editor.MouseDown(p, b, clicks, mods, px);
        Canvas.WorldMouseMove += (p, left, mods) => _editor.MouseMove(p, left, mods);
        Canvas.WorldMouseUp += p => _editor.MouseUp(p, Keyboard.Modifiers);

        _editor.DesignChanged += OnDesignChanged;
        _editor.SelectionChanged += OnSelectionChanged;
        _editor.OverlayChanged += () => Canvas.InvalidateVisual();
        _editor.ToolChanged += OnToolChanged;
        _editor.Message += msg => ShowMessage(msg);
        // O diálogo abre depois do clique terminar, para não competir com a captura do mouse.
        _editor.TextRequested += p => Dispatcher.BeginInvoke(() =>
        {
            var spec = TextDialog.Ask(this, "Novo texto", TextSpec.FromPreferences());
            if (spec == null) return;
            _editor.AddText(p, spec);
            _editor.Tool = EditorTool.Select; // permite ajustar e mover o texto logo em seguida
        });
        _editor.EditTextRequested += t => Dispatcher.BeginInvoke(() =>
        {
            var spec = TextDialog.Ask(this, "Editar texto", TextSpec.From(t));
            if (spec != null) _editor.ApplyText(t, spec);
        });

        Properties.BeforeChange += _editor.Checkpoint;
        Properties.AfterChange += () => { _editor.MarkDirty(); _editor.Rebuild(); RefreshObjectList(); };
        ImageProperties.AfterChange += () => { _editor.MarkDirty(); UpdateBackground(); };

        _editor.LoadDesign(new EmbDesign(), null);
        OnToolChanged();
        UpdateCurrentColor();
    }

    // ---------- Inicialização ----------

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToList();

        // Régua: preferência lembrada (ou --regua, para os testes visuais).
        ShowRulerMenu.IsChecked = Canvas.ShowRuler = UserPrefs.Current.ShowRuler || args.Contains("--regua");

        // Arquivo a abrir = argumento que não é opção "--x" nem valor de uma opção.
        var optionValues = new Dictionary<string, int>
        {
            ["--captura"] = 1, ["--selecionar"] = 1, ["--zoom"] = 1, ["--captura-dialogo"] = 1, ["--captura-digitalizar"] = 2,
        };
        var positional = new List<string>();
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i].StartsWith("--")) { i += optionValues.GetValueOrDefault(args[i]); continue; }
            positional.Add(args[i]);
        }
        var file = positional.FirstOrDefault(File.Exists);
        if (file != null)
        {
            // Imagem ("Abrir com" o Bordadox): vai para a digitalização. Bordado ou projeto: abre.
            if (ImageLoader.IsImageFile(file)) Dispatcher.BeginInvoke(() => DigitizeImage(file));
            else OpenFile(file);
        }
        Canvas.Focus();

        // --captura <arquivo.png>: salva a janela como imagem e fecha (para testes automáticos).
        // --selecionar N: seleciona o objeto N antes da captura.
        int sel = args.IndexOf("--selecionar");
        if (sel >= 0 && sel + 1 < args.Count && int.TryParse(args[sel + 1], out var idx) && idx < _editor.Design.Objects.Count)
            _editor.Select(new[] { _editor.Design.Objects[idx] });
        int cap = args.IndexOf("--captura");
        if (cap >= 0 && cap + 1 < args.Count)
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => SaveScreenshot(args[cap + 1]));

        // --captura-digitalizar <imagem> <arquivo.png>: salva a janela "Digitalizar imagem" como imagem.
        int dig = args.IndexOf("--captura-digitalizar");
        if (dig >= 0 && dig + 2 < args.Count)
            DigitizeDialog.CaptureTo(this, ImageLoader.Load(args[dig + 1]), args[dig + 2], args.Contains("--fundo"));

        // --captura-dialogo <arquivo.png> [--com-lista]: salva a janela de texto (e a lista de fontes) como imagem.
        int dlg = args.IndexOf("--captura-dialogo");
        if (dlg >= 0 && dlg + 1 < args.Count)
        {
            TextDialog.CaptureTo(this, args[dlg + 1], args.Contains("--com-lista"));
        }
    }

    private void SaveScreenshot(string path)
    {
        Canvas.ZoomToFit();
        // --zoom F: amplia F vezes em torno do centro da tela (para inspecionar os pontos).
        var args = Environment.GetCommandLineArgs().ToList();
        int z = args.IndexOf("--zoom");
        if (z >= 0 && z + 1 < args.Count && double.TryParse(args[z + 1], System.Globalization.CultureInfo.InvariantCulture, out var f))
            Canvas.ZoomBy(f);
        UpdateLayout();
        var content = (FrameworkElement)Content;
        var bmp = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(content);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(path)) enc.Save(fs);
        _editor.LoadDesign(new EmbDesign(), null); // evita a pergunta de salvar
        Close();
    }

    // ---------- Reações ao editor ----------

    private void OnDesignChanged()
    {
        StopSimulation();
        var data = _editor.RenderData;
        Canvas.Data = data;

        var name = _editor.FilePath != null ? Path.GetFileName(_editor.FilePath) : "Sem título";
        Title = $"{name}{(_editor.IsDirty ? " *" : "")} — Bordadox";

        var p = _editor.Pattern;
        int colors = data?.Blocks.Count ?? 0;
        StatusStitches.Text = $"Pontos: {data?.StitchCount ?? 0:N0}";
        StatusColors.Text = $"Cores: {colors}";
        StatusTrims.Text = $"Cortes: {data?.TrimCount ?? 0}";
        StatusSize.Text = data != null ? $"Tamanho: {data.WidthMm:0.0} × {data.HeightMm:0.0} mm" : "Tamanho: —";

        StatusWarning.Text = "";
        if (data != null)
        {
            var hoop = _editor.Design.Hoop;
            var b = p.Bounds();
            bool outside = b.MinX / 10.0 < -hoop.WidthMm / 2 || b.MaxX / 10.0 > hoop.WidthMm / 2 ||
                           b.MinY / 10.0 < -hoop.HeightMm / 2 || b.MaxY / 10.0 > hoop.HeightMm / 2;
            if (Hoops.SmallestFittingAnyOrientation(data.WidthMm, data.HeightMm) == null)
                StatusWarning.Text = "Desenho maior que o maior bastidor (160 × 260 mm)";
            else if (outside)
                StatusWarning.Text = "Parte do desenho está fora do bastidor";
        }

        _syncing = true;
        HoopCombo.SelectedItem = _editor.Design.BaseHoop;
        RotateHoopButton.IsChecked = RotateHoopMenu.IsChecked = _editor.Design.HoopRotated;
        if (Canvas.Hoop != _editor.Design.Hoop)
        {
            Canvas.Hoop = _editor.Design.Hoop;
            Canvas.ZoomToFit();
        }
        SimSlider.Maximum = data?.Segments.Count ?? 0;
        SimSlider.Value = SimSlider.Maximum;
        _syncing = false;

        RefreshObjectList();
        UpdateBackground();
    }

    private void RefreshObjectList()
    {
        _syncing = true;
        var objs = _editor.Design.Objects;
        var items = objs.Select((o, i) => new ObjectItem(o, i + 1, _editor.StitchCountOf(o))).ToList();
        ObjectList.ItemsSource = items;
        ObjectList.SelectedItems.Clear();
        foreach (var item in items.Where(i => _editor.Selection.Contains(i.Object))) ObjectList.SelectedItems.Add(item);
        if (ObjectList.SelectedItems.Count > 0) ObjectList.ScrollIntoView(ObjectList.SelectedItems[^1]);
        _syncing = false;
    }

    private void OnSelectionChanged()
    {
        RefreshObjectList();
        var sel = _editor.Selection;
        if (sel.Count == 0)
        {
            PropertiesHeader.Text = "Configurações do desenho";
            Properties.SetTargets(new object[] { _editor.Design.Settings });
            ImageProperties.SetTargets(_editor.Design.Background is { } bg ? new object[] { bg } : Array.Empty<object>());
        }
        else
        {
            PropertiesHeader.Text = sel.Count == 1 ? $"Propriedades — {sel[0].TypeName}" : $"Propriedades — {sel.Count} objetos";
            Properties.SetTargets(sel);
            ImageProperties.SetTargets(Array.Empty<object>());
            if (sel.Select(o => o.Color).Distinct().Count() == 1) _editor.CurrentColor = sel[0].Color;
        }
        UpdateCurrentColor();
        Canvas.InvalidateVisual();
    }

    private void OnToolChanged()
    {
        var tool = _editor.Tool;
        foreach (var rb in new[] { ToolSelect, ToolRun, ToolSatin, ToolFill, ToolApplique, ToolHole, ToolText })
            rb.IsChecked = (string)rb.Tag == tool.ToString();
        Canvas.ToolCursor = tool == EditorTool.Select ? null : Cursors.Cross;
        HintText.Text = _editor.ToolHint;
    }

    private void UpdateCurrentColor()
    {
        CurrentColorSwatch.Background = new SolidColorBrush(Color.FromRgb(_editor.CurrentColor.R, _editor.CurrentColor.G, _editor.CurrentColor.B));
        CurrentColorName.Text = _editor.CurrentColor.Name;
    }

    private void ShowMessage(string msg)
    {
        HintText.Text = "⚠ " + msg;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) => { timer.Stop(); HintText.Text = _editor.ToolHint; };
        timer.Start();
    }

    // ---------- Arquivo ----------

    private bool ConfirmDiscard()
    {
        if (!_editor.IsDirty) return true;
        var r = MessageBox.Show(this, "O desenho tem alterações não salvas. Deseja salvar antes?",
            "Bordadox", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return false;
        if (r == MessageBoxResult.Yes) return SaveProject(_editor.FilePath);
        return true;
    }

    private void New_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var design = new EmbDesign { HoopName = _editor.Design.HoopName, HoopRotated = _editor.Design.HoopRotated };
        _editor.LoadDesign(design, null);
    }

    private static string OpenFilter()
    {
        var readable = EmbroideryFormats.All.Where(f => f.CanRead).ToList();
        var all = string.Join(";", new[] { "*." + EmbDesign.Extension }.Concat(readable.Select(f => "*." + f.Extension)));
        const string images = "*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.svg";
        return $"Todos os suportados|{all};{images}|Projeto Bordadox (*.bdx)|*.bdx|" +
               string.Join("|", readable.Select(f => $"{f.Description} (*.{f.Extension})|*.{f.Extension}")) +
               $"|Imagens para digitalizar|{images}";
    }

    private void Open_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Abrir", Filter = OpenFilter() };
        if (dlg.ShowDialog(this) != true) return;
        // Uma imagem é digitalizada e acrescentada ao desenho atual: não substitui nada, então não pergunta sobre salvar.
        if (ImageLoader.IsImageFile(dlg.FileName)) { DigitizeImage(dlg.FileName); return; }
        if (ConfirmDiscard()) OpenFile(dlg.FileName);
    }

    private void OpenFile(string path)
    {
        try
        {
            if (Path.GetExtension(path).Equals("." + EmbDesign.Extension, StringComparison.OrdinalIgnoreCase))
            {
                _editor.LoadDesign(EmbDesign.Load(path), path);
            }
            else
            {
                var pattern = EmbroideryFormats.ReadFile(path).Centered();
                var design = EmbDesign.FromPattern(pattern);
                _editor.LoadDesign(design, null);
                Title = $"{Path.GetFileName(path)} (importado) — Bordadox";
            }
            Canvas.ZoomToFit();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível abrir o arquivo:\n{path}\n\n{ex.Message}",
                "Bordadox", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) => SaveProject(_editor.FilePath);
    private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e) => SaveProject(null);

    private bool SaveProject(string? path)
    {
        if (path == null)
        {
            var dlg = new SaveFileDialog { Title = "Salvar projeto", Filter = "Projeto Bordadox (*.bdx)|*.bdx", DefaultExt = ".bdx" };
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }
        try
        {
            _editor.Design.Save(path);
            _editor.MarkSaved(path);
            ShowMessage($"Projeto salvo: {path}");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível salvar:\n{ex.Message}", "Bordadox", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void Export_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_editor.Design.Objects.Count == 0)
        {
            ShowMessage("O desenho está vazio.");
            return;
        }
        if (StatusWarning.Text.Length > 0 &&
            MessageBox.Show(this, StatusWarning.Text + ".\nExportar mesmo assim?", "Bordadox",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var writable = EmbroideryFormats.All.Where(f => f.CanWrite).ToList();
        var dlg = new SaveFileDialog
        {
            Title = "Exportar para a máquina",
            Filter = string.Join("|", writable.Select(f => $"{f.Description} (*.{f.Extension})|*.{f.Extension}")),
            FileName = _editor.FilePath != null ? Path.GetFileNameWithoutExtension(_editor.FilePath) : "bordado",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var design = _editor.Design;
            var pattern = design.BuildMachinePattern(Path.GetFileNameWithoutExtension(dlg.FileName));
            EmbroideryFormats.WriteFile(pattern, dlg.FileName);
            var rotated = design.HoopRotated && design.Settings.RotateOnExport
                ? " — girado 90° (horário): prenda o tecido com o bastidor de lado"
                : "";
            ShowMessage($"Exportado: {dlg.FileName} ({pattern.StitchCount:N0} pontos){rotated}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível exportar:\n{ex.Message}", "Bordadox", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void InsertEmbroidery_Click(object sender, RoutedEventArgs e)
    {
        var readable = EmbroideryFormats.All.Where(f => f.CanRead).ToList();
        var all = string.Join(";", readable.Select(f => "*." + f.Extension));
        var dlg = new OpenFileDialog { Title = "Inserir bordado", Filter = $"Bordados ({all})|{all}" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var imported = EmbDesign.FromPattern(EmbroideryFormats.ReadFile(dlg.FileName).Centered());
            _editor.AddObjects(imported.Objects);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível abrir:\n{ex.Message}", "Bordadox", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmDiscard()) e.Cancel = true;
        else _simTimer.Stop();
    }

    // ---------- Imagem de fundo ----------

    private void InsertImage_Click(object sender, RoutedEventArgs e)
    {
        // Aceita também SVG e imagens grandes (reduzidas a 1600 px, o bastante para desenhar por cima).
        var dlg = new OpenFileDialog { Title = "Imagem de fundo", Filter = ImageLoader.Filter };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var loaded = ImageLoader.Load(dlg.FileName);
            double aspect = loaded.Image.Height / (double)loaded.Image.Width;
            var hoop = _editor.Design.Hoop;
            double width = Math.Min(hoop.WidthMm * 0.9, hoop.HeightMm * 0.9 / aspect);
            _editor.Design.Background = new BackgroundImage
            {
                Data = ImageLoader.EncodePng(loaded.Image, 1600),
                FileName = loaded.Name + ".png",
                Aspect = aspect,
                WidthMm = Math.Round(width),
            };
            ShowImageMenu.IsChecked = true;
            _editor.Select(Array.Empty<EmbObject>());
            _editor.MarkDirty();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível abrir a imagem:\n{ex.Message}", "Bordadox", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowImage_Click(object sender, RoutedEventArgs e)
    {
        if (_editor.Design.Background is { } bg)
        {
            bg.Visible = ShowImageMenu.IsChecked;
            _editor.MarkDirty();
        }
    }

    private void ImageProperties_Click(object sender, RoutedEventArgs e)
    {
        if (_editor.Design.Background == null) { ShowMessage("Nenhuma imagem de fundo."); return; }
        _editor.Select(Array.Empty<EmbObject>());
    }

    private void RemoveImage_Click(object sender, RoutedEventArgs e)
    {
        _editor.Design.Background = null;
        _editor.MarkDirty();
        OnSelectionChanged();
    }

    private BitmapSource? _bgImage;
    private byte[]? _bgBytes;

    private void UpdateBackground()
    {
        var bg = _editor.Design.Background;
        if (bg == null || bg.Data.Length == 0)
        {
            Canvas.BackgroundSource = null;
            _bgImage = null;
            _bgBytes = null;
        }
        else
        {
            if (!ReferenceEquals(_bgBytes, bg.Data))
            {
                _bgImage = LoadImage(bg.Data);
                _bgBytes = bg.Data;
            }
            Canvas.BackgroundSource = bg.Visible ? _bgImage : null;
            Canvas.BackgroundRect = new Rect(bg.X - bg.WidthMm / 2, bg.Y - bg.HeightMm / 2, bg.WidthMm, bg.HeightMm);
            Canvas.BackgroundOpacity = bg.Opacity;
            ShowImageMenu.IsChecked = bg.Visible;
        }
        Canvas.InvalidateVisual();
    }

    private static BitmapSource LoadImage(byte[] bytes)
    {
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = new MemoryStream(bytes);
        img.EndInit();
        img.Freeze();
        return img;
    }

    // ---------- Edição ----------

    private void Undo_Executed(object sender, ExecutedRoutedEventArgs e) => _editor.Undo();
    private void Redo_Executed(object sender, ExecutedRoutedEventArgs e) => _editor.Redo();
    private void Duplicate_Executed(object sender, ExecutedRoutedEventArgs e) => _editor.DuplicateSelection();
    private void Delete_Executed(object sender, ExecutedRoutedEventArgs e) => _editor.DeleteSelection();
    private void SelectAll_Executed(object sender, ExecutedRoutedEventArgs e) => _editor.SelectAll();

    private void Copy_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_editor.CopySelection() is { } json) Clipboard.SetData(ClipboardFormat, json);
    }

    private void Paste_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (Clipboard.GetData(ClipboardFormat) is string json) _editor.Paste(json);
    }

    private void RotateCw_Click(object sender, RoutedEventArgs e) { _editor.Rotate(90); OnSelectionChanged(); }
    private void RotateCcw_Click(object sender, RoutedEventArgs e) { _editor.Rotate(-90); OnSelectionChanged(); }
    private void MirrorH_Click(object sender, RoutedEventArgs e) { _editor.Mirror(true); OnSelectionChanged(); }
    private void MirrorV_Click(object sender, RoutedEventArgs e) { _editor.Mirror(false); OnSelectionChanged(); }
    private void Center_Click(object sender, RoutedEventArgs e) => _editor.CenterInHoop();
    private void ToApplique_Click(object sender, RoutedEventArgs e) => ConvertShapes(true);
    private void ToFill_Click(object sender, RoutedEventArgs e) => ConvertShapes(false);

    private void ConvertShapes(bool toApplique)
    {
        int n = _editor.ConvertShapes(toApplique);
        if (n == 0) ShowMessage(toApplique ? "Selecione um preenchimento para converter em aplique." : "Selecione um aplique para converter em preenchimento.");
        else OnSelectionChanged();
    }

    private void SeqUp_Click(object sender, RoutedEventArgs e) => _editor.MoveInSequence(-1);
    private void SeqDown_Click(object sender, RoutedEventArgs e) => _editor.MoveInSequence(1);

    private void RotateBy_Click(object sender, RoutedEventArgs e)
    {
        if (_editor.Selection.Count == 0) { ShowMessage("Selecione um objeto."); return; }
        if (InputDialog.AskNumber(this, "Girar", "Ângulo em graus (positivo = horário):", 15) is { } deg)
        {
            _editor.Rotate(deg);
            OnSelectionChanged();
        }
    }

    private void Scale_Click(object sender, RoutedEventArgs e)
    {
        if (_editor.Selection.Count == 0) { ShowMessage("Selecione um objeto."); return; }
        if (InputDialog.AskNumber(this, "Redimensionar", "Novo tamanho em % do atual:", 100) is { } pct && pct > 0)
            _editor.Scale(pct / 100);
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse<EditorTool>(tag, out var tool)) _editor.Tool = tool;
        Canvas.Focus();
    }

    private void PaletteColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ThreadColor color })
        {
            _editor.SetColor(color);
            UpdateCurrentColor();
        }
    }

    private void ObjectList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        _editor.Select(ObjectList.SelectedItems.Cast<ObjectItem>().Select(i => i.Object));
    }

    // ---------- Exibição ----------

    private void HoopCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || HoopCombo.SelectedItem is not Hoop hoop) return;
        _editor.SetHoop(hoop.Name, _editor.Design.HoopRotated);
    }

    private void RotateHoop_Click(object sender, RoutedEventArgs e) =>
        _editor.SetHoop(_editor.Design.HoopName, !_editor.Design.HoopRotated);

    private void ViewOptions_Click(object sender, RoutedEventArgs e)
    {
        Canvas.ShowGrid = ShowGridMenu.IsChecked;
        Canvas.ShowJumps = ShowJumpsMenu.IsChecked;
        Canvas.ShowRuler = ShowRulerMenu.IsChecked;
        if (UserPrefs.Current.ShowRuler != ShowRulerMenu.IsChecked)
        {
            UserPrefs.Current.ShowRuler = ShowRulerMenu.IsChecked; // lembra a escolha para a próxima vez
            UserPrefs.Current.Save();
        }
    }

    private void ZoomFit_Executed(object sender, ExecutedRoutedEventArgs e) => Canvas.ZoomToFit();
    private void ZoomIn_Executed(object sender, ExecutedRoutedEventArgs e) => Canvas.ZoomBy(1.25);
    private void ZoomOut_Executed(object sender, ExecutedRoutedEventArgs e) => Canvas.ZoomBy(1 / 1.25);
    private void ZoomActual_Executed(object sender, ExecutedRoutedEventArgs e) => Canvas.ZoomActualSize();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (Keyboard.FocusedElement is TextBox) return;

        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None && _editor.KeyDown(e.Key)) { e.Handled = true; return; }

        // Ctrl+setas: ordem de costura.
        if (mods == ModifierKeys.Control && e.Key is Key.Up or Key.Down)
        {
            _editor.MoveInSequence(e.Key == Key.Up ? -1 : 1);
            e.Handled = true;
            return;
        }
        if (mods is not (ModifierKeys.None or ModifierKeys.Shift)) return;

        // Setas movem a seleção (Shift = 5 mm), exceto quando a lista de objetos tem o foco.
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down && !ObjectList.IsKeyboardFocusWithin && _editor.Selection.Count > 0)
        {
            double step = mods == ModifierKeys.Shift ? 5 : 0.5;
            var (dx, dy) = e.Key switch { Key.Left => (-step, 0.0), Key.Right => (step, 0.0), Key.Up => (0.0, -step), _ => (0.0, step) };
            _editor.Nudge(dx, dy);
            e.Handled = true;
            return;
        }
        if (mods != ModifierKeys.None || Keyboard.FocusedElement is ComboBox or ComboBoxItem) return;

        e.Handled = true;
        switch (e.Key)
        {
            case Key.V: _editor.Tool = EditorTool.Select; break;
            case Key.R: _editor.Tool = EditorTool.Run; break;
            case Key.C: _editor.Tool = EditorTool.Satin; break;
            case Key.F: _editor.Tool = EditorTool.Fill; break;
            case Key.A: _editor.Tool = EditorTool.Applique; break;
            case Key.H: _editor.Tool = EditorTool.Hole; break;
            case Key.T: _editor.Tool = EditorTool.Text; break;
            case Key.B: RotateHoop_Click(this, e); break;
            case Key.D0 or Key.NumPad0: Canvas.ZoomToFit(); break;
            case Key.D1 or Key.NumPad1: Canvas.ZoomActualSize(); break;
            case Key.OemPlus or Key.Add: Canvas.ZoomBy(1.25); break;
            case Key.OemMinus or Key.Subtract: Canvas.ZoomBy(1 / 1.25); break;
            case Key.P: AppCommands.PlayPause.Execute(null, this); break;
            case Key.Delete: _editor.DeleteSelection(); break;
            case Key.Home: SimStart_Click(this, e); break;
            case Key.End: SimEnd_Click(this, e); break;
            case Key.Space: break; // segurar Espaço + arrastar move a vista
            default: e.Handled = false; break;
        }
    }

    // ---------- Simulação ----------

    private void PlayPause_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var data = _editor.RenderData;
        if (data == null || data.Segments.Count == 0) return;
        if (_simTimer.IsEnabled) { StopSimulation(); return; }
        if (SimSlider.Value >= SimSlider.Maximum) SimSlider.Value = 0;
        _simTimer.Start();
        PlayButton.Content = "⏸";
    }

    private void StopSimulation()
    {
        _simTimer.Stop();
        PlayButton.Content = "▶";
    }

    private void SimTimer_Tick(object? sender, EventArgs e)
    {
        int speed = SpeedCombo.SelectedItem is ComboBoxItem { Tag: string tag } ? int.Parse(tag) : 6;
        var next = Math.Min(SimSlider.Value + speed, SimSlider.Maximum);
        SimSlider.Value = next;
        if (next >= SimSlider.Maximum) StopSimulation();
    }

    private void SimSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing) return;
        Canvas.VisibleSegments = (int)SimSlider.Value;
    }

    private void SimStart_Click(object sender, RoutedEventArgs e)
    {
        StopSimulation();
        SimSlider.Value = 0;
    }

    private void SimEnd_Click(object sender, RoutedEventArgs e)
    {
        StopSimulation();
        SimSlider.Value = SimSlider.Maximum;
    }

    // ---------- Arrastar e soltar ----------

    private static string? DroppedFile(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files) return null;
        var f = files[0];
        bool ok = Path.GetExtension(f).Equals("." + EmbDesign.Extension, StringComparison.OrdinalIgnoreCase) ||
                  EmbroideryFormats.ForFile(f) is { CanRead: true } ||
                  ImageLoader.IsImageFile(f);
        return ok ? f : null;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedFile(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (DroppedFile(e) is not { } file) return;
        // Imagem (logo, SVG): digitaliza e acrescenta ao desenho atual, sem fechar nada.
        if (ImageLoader.IsImageFile(file)) { Dispatcher.BeginInvoke(() => DigitizeImage(file)); return; }
        if (ConfirmDiscard()) OpenFile(file);
    }

    // ---------- Digitalizar imagem ----------

    private void Digitize_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Digitalizar imagem (logo ou desenho)", Filter = ImageLoader.Filter };
        if (dlg.ShowDialog(this) == true) DigitizeImage(dlg.FileName);
    }

    private void DigitizeImage(string path)
    {
        LoadedImage loaded;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            loaded = ImageLoader.Load(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Não foi possível abrir a imagem:\n{path}\n\n{ex.Message}", "Bordadox", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        var outcome = DigitizeDialog.Ask(this, loaded, _editor.Design.Hoop);
        if (outcome == null) return;

        // "Só usar como fundo": a imagem inteira vai para trás do desenho, sem gerar bordado.
        if (outcome.BackgroundOnly)
        {
            _editor.Design.Background = new BackgroundImage
            {
                Data = outcome.ReferencePng!,
                FileName = loaded.Name + ".png",
                Aspect = outcome.ReferenceAspect,
                WidthMm = outcome.WidthMm,
                X = 0,
                Y = 0,
                Opacity = 0.6,
                Visible = true,
            };
            ShowImageMenu.IsChecked = true;
            _editor.Select(Array.Empty<EmbObject>()); // mostra as propriedades da imagem (posição, largura, opacidade)
            _editor.MarkDirty();
            Canvas.ZoomToFit();
            ShowMessage($"Imagem inserida como fundo ({outcome.WidthMm:0.#} × {outcome.HeightMm:0.#} mm). Desenhe por cima; ajuste posição, tamanho e opacidade nas Propriedades.");
            return;
        }

        // Imagem de referência (opcional): fica atrás, no mesmo tamanho e posição do desenho gerado.
        if (outcome.ReferencePng is { } png)
        {
            _editor.Design.Background = new BackgroundImage
            {
                Data = png,
                FileName = loaded.Name + ".png",
                Aspect = outcome.ReferenceAspect,
                WidthMm = outcome.WidthMm,
                X = 0,
                Y = 0,
                Opacity = 0.35,
            };
        }
        var generated = outcome.Result!;
        _editor.AddObjects(generated.Objects);
        Canvas.ZoomToFit();
        ShowMessage($"Digitalizado: {generated.Objects.Count} objetos, {generated.Layers.Count} cores, " +
                    $"{outcome.StitchCount:N0} pontos, {outcome.WidthMm:0.#} × {outcome.HeightMm:0.#} mm. Todos os objetos ficaram selecionados: arraste para mover o desenho todo.");
    }
}
