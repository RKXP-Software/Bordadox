using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Bordadox.App.Rendering;
using Bordadox.Core.Geometry;
using Bordadox.Core.Machine;

namespace Bordadox.App.Controls;

/// <summary>
/// Área de trabalho: desenha bastidor, grade e pontos. Coordenadas do mundo em milímetros,
/// com a origem no centro do bastidor. Roda do mouse = zoom, arrastar = mover a vista.
/// </summary>
public sealed class EmbroideryCanvas : FrameworkElement
{
    private static readonly Brush WorkspaceBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3D, 0x42)));
    private static readonly Brush FabricBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xF7, 0xF4, 0xEC)));
    private static readonly Color HoopColor = Color.FromRgb(0x8A, 0x6A, 0x45);
    private static readonly Color GridMinorColor = Color.FromArgb(0x30, 0x80, 0x80, 0x80);
    private static readonly Color GridMajorColor = Color.FromArgb(0x60, 0x80, 0x80, 0x80);
    private static readonly Color JumpColor = Color.FromArgb(0xA0, 0x90, 0x90, 0x90);

    private const double ThreadWidthMm = 0.4;

    private StitchRenderData? _data;
    private Hoop _hoop = Hoops.Default;
    private int _visibleSegments;
    private bool _showGrid = true;
    private bool _showJumps;

    private double _scale = 3;           // pixels por mm
    private Point _origin;               // posição na tela da origem do mundo
    private bool _viewInitialized;
    private Point? _panStart;

    // Geometrias por cor, reaproveitadas enquanto o limite visível não muda.
    private List<(int Color, StreamGeometry Geometry)>? _chunks;
    private StreamGeometry? _jumpGeometry;
    private int _cachedLimit = -1;

    public event EventHandler<Vec2?>? CursorMoved;
    public event EventHandler? ZoomChanged;

    public EmbroideryCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        SizeChanged += (_, _) =>
        {
            if (!_viewInitialized && ActualWidth > 0) { _viewInitialized = true; ZoomToFit(); }
        };
    }

    public StitchRenderData? Data
    {
        get => _data;
        set
        {
            _data = value;
            _visibleSegments = value?.Segments.Count ?? 0;
            InvalidateCache();
        }
    }

    public Hoop Hoop
    {
        get => _hoop;
        set { _hoop = value; InvalidateVisual(); }
    }

    /// <summary>Quantos segmentos (em ordem de costura) são desenhados — usado pelo simulador.</summary>
    public int VisibleSegments
    {
        get => _visibleSegments;
        set
        {
            var v = Math.Clamp(value, 0, _data?.Segments.Count ?? 0);
            if (v == _visibleSegments) return;
            _visibleSegments = v;
            InvalidateVisual();
        }
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set { _showGrid = value; InvalidateVisual(); }
    }

    public bool ShowJumps
    {
        get => _showJumps;
        set { _showJumps = value; InvalidateVisual(); }
    }

    /// <summary>Zoom em porcentagem, considerando 96 DPI (100% = tamanho real na tela).</summary>
    public double ZoomPercent => _scale / (96 / 25.4) * 100;

    public void ZoomToFit()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var area = new Rect2(-_hoop.WidthMm / 2, -_hoop.HeightMm / 2, _hoop.WidthMm / 2, _hoop.HeightMm / 2);
        if (_data != null)
        {
            var b = _data.Pattern.Bounds();
            area = area.Union(new Rect2(b.MinX / 10.0, b.MinY / 10.0, b.MaxX / 10.0, b.MaxY / 10.0));
        }
        area = area.Inflate(8);
        _scale = Math.Min(ActualWidth / area.Width, ActualHeight / area.Height);
        _origin = new Point(ActualWidth / 2 - area.Center.X * _scale, ActualHeight / 2 - area.Center.Y * _scale);
        InvalidateVisual();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomBy(double factor) => ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), factor);

    public void ZoomActualSize() => ZoomBy(96 / 25.4 / _scale);

    private void ZoomAt(Point screen, double factor)
    {
        var newScale = Math.Clamp(_scale * factor, 0.2, 400);
        factor = newScale / _scale;
        _origin = new Point(screen.X - (screen.X - _origin.X) * factor, screen.Y - (screen.Y - _origin.Y) * factor);
        _scale = newScale;
        InvalidateVisual();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public Vec2 ScreenToWorld(Point p) => new((p.X - _origin.X) / _scale, (p.Y - _origin.Y) / _scale);

    private void InvalidateCache()
    {
        _chunks = null;
        _jumpGeometry = null;
        _cachedLimit = -1;
        InvalidateVisual();
    }

    // ---------- Desenho ----------

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(WorkspaceBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.PushTransform(new MatrixTransform(_scale, 0, 0, _scale, _origin.X, _origin.Y));

        double px = 1 / _scale; // 1 pixel em mm
        DrawHoop(dc, px);
        if (BackgroundSource != null && BackgroundOpacity > 0)
        {
            dc.PushOpacity(BackgroundOpacity);
            dc.DrawImage(BackgroundSource, BackgroundRect);
            dc.Pop();
        }
        if (_data != null) DrawStitches(dc, px);
        Overlay?.Invoke(dc, px);

        dc.Pop();
        if (_showRuler) DrawRulers(dc);
    }

    // ---------- Régua (mm) ----------

    private const double RulerThickness = 22;
    private static readonly Brush RulerBackground = Frozen(new SolidColorBrush(Color.FromRgb(0xF1, 0xF1, 0xF1)));
    private static readonly Pen RulerTickPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)), 1));
    private static readonly Pen RulerEdgePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)), 1));
    private static readonly Pen RulerCursorPen = Frozen(new Pen(Brushes.Red, 1));
    private static readonly Typeface RulerFont = new("Segoe UI");
    // (intervalo principal em mm, divisões entre dois principais): o primeiro com folga de pixels para o número.
    private static readonly (double Major, int Divisions)[] RulerSteps =
        { (0.5, 5), (1, 10), (2, 4), (5, 5), (10, 10), (20, 4), (50, 5), (100, 10), (200, 4) };

    private bool _showRuler;
    private Vec2? _cursorWorld;

    /// <summary>Régua em milímetros nas bordas da área de trabalho; zero no centro do bastidor.</summary>
    public bool ShowRuler
    {
        get => _showRuler;
        set { _showRuler = value; InvalidateVisual(); }
    }

    private void DrawRulers(DrawingContext dc)
    {
        double t = RulerThickness, w = ActualWidth, h = ActualHeight;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        dc.DrawRectangle(RulerBackground, null, new Rect(0, 0, w, t));
        dc.DrawRectangle(RulerBackground, null, new Rect(0, 0, t, h));
        dc.DrawLine(RulerEdgePen, new Point(t, t - 0.5), new Point(w, t - 0.5));
        dc.DrawLine(RulerEdgePen, new Point(t - 0.5, t), new Point(t - 0.5, h));

        var (major, divisions) = RulerSteps.FirstOrDefault(s => s.Major * _scale >= 56);
        if (major == 0) (major, divisions) = RulerSteps[^1];
        double minor = major / divisions;

        FormattedText Label(double mm) => new(mm.ToString("0.#", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, RulerFont, 10, Brushes.DimGray, dpi);

        // Horizontal
        double x0 = (t - _origin.X) / _scale, x1 = (w - _origin.X) / _scale;
        for (long k = (long)Math.Floor(x0 / minor); k <= (long)Math.Ceiling(x1 / minor); k++)
        {
            double mm = k * minor, sx = _origin.X + mm * _scale;
            if (sx < t || sx > w) continue;
            bool isMajor = k % divisions == 0, isHalf = divisions % 2 == 0 && k % (divisions / 2) == 0;
            double len = isMajor ? 14 : isHalf ? 9 : 5;
            dc.DrawLine(RulerTickPen, new Point(sx, t), new Point(sx, t - len));
            if (isMajor) dc.DrawText(Label(mm), new Point(sx + 2, 1));
        }

        // Vertical (números girados, lidos de baixo para cima)
        double y0 = (t - _origin.Y) / _scale, y1 = (h - _origin.Y) / _scale;
        for (long k = (long)Math.Floor(y0 / minor); k <= (long)Math.Ceiling(y1 / minor); k++)
        {
            double mm = k * minor, sy = _origin.Y + mm * _scale;
            if (sy < t || sy > h) continue;
            bool isMajor = k % divisions == 0, isHalf = divisions % 2 == 0 && k % (divisions / 2) == 0;
            double len = isMajor ? 14 : isHalf ? 9 : 5;
            dc.DrawLine(RulerTickPen, new Point(t, sy), new Point(t - len, sy));
            if (isMajor)
            {
                dc.PushTransform(new TranslateTransform(1, sy - 2));
                dc.PushTransform(new RotateTransform(-90));
                dc.DrawText(Label(mm), new Point(0, 0));
                dc.Pop();
                dc.Pop();
            }
        }

        // Posição do cursor nas duas réguas.
        if (_cursorWorld is { } c)
        {
            double cx = _origin.X + c.X * _scale, cy = _origin.Y + c.Y * _scale;
            if (cx >= t && cx <= w) dc.DrawLine(RulerCursorPen, new Point(cx, 0), new Point(cx, t));
            if (cy >= t && cy <= h) dc.DrawLine(RulerCursorPen, new Point(0, cy), new Point(t, cy));
        }

        // Canto: unidade.
        dc.DrawRectangle(RulerBackground, null, new Rect(0, 0, t, t));
        dc.DrawText(new FormattedText("mm", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, RulerFont, 9, Brushes.Gray, dpi), new Point(3, 5));
    }

    /// <summary>Imagem de referência (em mm) desenhada sob os pontos.</summary>
    public ImageSource? BackgroundSource { get; set; }
    public Rect BackgroundRect { get; set; }
    public double BackgroundOpacity { get; set; } = 0.5;

    /// <summary>Desenho extra por cima dos pontos (seleção, prévia), em coordenadas de mm.</summary>
    public Action<DrawingContext, double>? Overlay { get; set; }

    public double PixelSizeMm => 1 / _scale;

    private void DrawHoop(DrawingContext dc, double px)
    {
        double w = _hoop.WidthMm, h = _hoop.HeightMm;
        var area = new Rect(-w / 2, -h / 2, w, h);

        var frame = area;
        frame.Inflate(6, 6);
        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(HoopColor), 5), frame, 12, 12);
        dc.DrawRectangle(FabricBrush, null, area);

        if (_showGrid)
        {
            var minor = new Pen(new SolidColorBrush(GridMinorColor), px);
            var major = new Pen(new SolidColorBrush(GridMajorColor), px);
            for (double x = 0; x <= w / 2; x += 10)
                foreach (var sx in x == 0 ? new[] { 0.0 } : new[] { x, -x })
                    dc.DrawLine(x % 50 == 0 ? major : minor, new Point(sx, -h / 2), new Point(sx, h / 2));
            for (double y = 0; y <= h / 2; y += 10)
                foreach (var sy in y == 0 ? new[] { 0.0 } : new[] { y, -y })
                    dc.DrawLine(y % 50 == 0 ? major : minor, new Point(-w / 2, sy), new Point(w / 2, sy));
        }

        // Cruz do centro do bastidor.
        var center = new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xC0, 0x30, 0x30)), px);
        dc.DrawLine(center, new Point(-5, 0), new Point(5, 0));
        dc.DrawLine(center, new Point(0, -5), new Point(0, 5));
    }

    private void DrawStitches(DrawingContext dc, double px)
    {
        var data = _data!;
        if (_chunks == null || _cachedLimit != _visibleSegments) BuildGeometry(data);

        double thickness = Math.Max(ThreadWidthMm, 1.2 * px);
        // Com zoom suficiente, desenha cada ponto com borda escura e brilho no meio (aspecto de linha).
        // Os blocos seguem a ordem de costura, então o que é costurado depois cobre o que veio antes.
        bool shaded = ThreadWidthMm / px >= 3;
        var pens = new Dictionary<int, (Pen Dark, Pen Main, Pen Light)>();
        foreach (var (c, geom) in _chunks!)
        {
            if (!pens.TryGetValue(c, out var p))
            {
                var color = data.Colors[c];
                p = (ThreadPen(Shade(color, 0.62), thickness), ThreadPen(color, shaded ? thickness * 0.7 : thickness), ThreadPen(Tint(color, 0.35), thickness * 0.25));
                pens[c] = p;
            }
            if (shaded)
            {
                dc.DrawGeometry(null, p.Dark, geom);
                dc.DrawGeometry(null, p.Main, geom);
                dc.DrawGeometry(null, p.Light, geom);
            }
            else dc.DrawGeometry(null, p.Main, geom);
        }

        if (_showJumps && _jumpGeometry != null)
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(JumpColor), px) { DashStyle = DashStyles.Dash }, _jumpGeometry);

        // Agulha: marca a posição atual durante a simulação.
        if (_visibleSegments > 0 && _visibleSegments < data.Segments.Count)
        {
            var s = data.Segments[_visibleSegments - 1];
            var needle = new Pen(Brushes.Red, 2 * px);
            double r = 6 * px;
            dc.DrawEllipse(null, needle, new Point(s.X2, s.Y2), r, r);
            dc.DrawLine(needle, new Point(s.X2 - 2 * r, s.Y2), new Point(s.X2 + 2 * r, s.Y2));
            dc.DrawLine(needle, new Point(s.X2, s.Y2 - 2 * r), new Point(s.X2, s.Y2 + 2 * r));
        }
    }

    private static Pen ThreadPen(Color c, double thickness) => new(new SolidColorBrush(c), thickness)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
        LineJoin = PenLineJoin.Round,
    };

    private static Color Shade(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    private static Color Tint(Color c, double f) =>
        Color.FromRgb((byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));

    private const int ChunkSegments = 120;

    private void BuildGeometry(StitchRenderData data)
    {
        var chunks = new List<(int Color, StreamGeometry Geometry)>();
        StreamGeometry? geom = null;
        StreamGeometryContext? ctx = null;
        int chunkColor = -1, chunkCount = 0;
        Point? last = null;

        void CloseChunk()
        {
            if (ctx == null) return;
            ctx.Close();
            geom!.Freeze();
            chunks.Add((chunkColor, geom));
            ctx = null;
            last = null;
        }

        var jumps = new StreamGeometry();
        using (var jc = jumps.Open())
        {
            for (int i = 0; i < _visibleSegments; i++)
            {
                var s = data.Segments[i];
                var a = new Point(s.X1, s.Y1);
                var b = new Point(s.X2, s.Y2);
                if (s.IsJump)
                {
                    jc.BeginFigure(a, false, false);
                    jc.LineTo(b, true, false);
                    continue;
                }
                if (ctx == null || s.ColorIndex != chunkColor || chunkCount >= ChunkSegments)
                {
                    CloseChunk();
                    geom = new StreamGeometry();
                    ctx = geom.Open();
                    chunkColor = s.ColorIndex;
                    chunkCount = 0;
                }
                if (last != a) ctx!.BeginFigure(a, false, false);
                ctx!.LineTo(b, true, true);
                last = b;
                chunkCount++;
            }
        }
        CloseChunk();
        jumps.Freeze();

        _chunks = chunks;
        _jumpGeometry = jumps;
        _cachedLimit = _visibleSegments;
    }

    // ---------- Mouse ----------
    // Botão do meio (ou Espaço + arrastar) move a vista; os demais cliques vão para o editor, em mm.

    public event Action<Vec2, MouseButton, int, ModifierKeys, double>? WorldMouseDown;
    public event Action<Vec2, bool, ModifierKeys>? WorldMouseMove;
    public event Action<Vec2>? WorldMouseUp;

    /// <summary>Cursor usado fora do modo de mover a vista (muda conforme a ferramenta).</summary>
    public Cursor? ToolCursor { get; set; }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ZoomAt(e.GetPosition(this), e.Delta > 0 ? 1.2 : 1 / 1.2);
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        if (_showRuler && (p.X < RulerThickness || p.Y < RulerThickness)) return; // clique na régua não desenha
        if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space)))
        {
            _panStart = p;
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
            return;
        }
        CaptureMouse();
        WorldMouseDown?.Invoke(ScreenToWorld(p), e.ChangedButton, e.ClickCount, Keyboard.Modifiers, 1 / _scale);
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        if (_panStart != null)
        {
            _panStart = null;
            Cursor = ToolCursor;
            return;
        }
        WorldMouseUp?.Invoke(ScreenToWorld(e.GetPosition(this)));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        var world = ScreenToWorld(p);
        if (_showRuler)
        {
            _cursorWorld = world;
            InvalidateVisual(); // move o marcador vermelho nas réguas
        }
        if (_panStart is { } start)
        {
            _origin = new Point(_origin.X + p.X - start.X, _origin.Y + p.Y - start.Y);
            _panStart = p;
            InvalidateVisual();
        }
        else
        {
            Cursor = ToolCursor;
            WorldMouseMove?.Invoke(world, e.LeftButton == MouseButtonState.Pressed, Keyboard.Modifiers);
        }
        CursorMoved?.Invoke(this, world);
    }

    protected override void OnMouseLeave(MouseEventArgs e) => CursorMoved?.Invoke(this, null);

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
