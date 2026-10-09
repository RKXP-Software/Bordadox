using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Bordadox.App.Rendering;
using Bordadox.Core.Design;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;

namespace Bordadox.App.Editor;

public enum EditorTool { Select, Run, Satin, Fill, Hole, Text, Applique }

/// <summary>
/// Estado e regras do editor: desenho atual, seleção, ferramentas, arrastar, edição de nós e desfazer.
/// A janela só repassa eventos de mouse/teclado (em mm) e reage aos eventos deste controlador.
/// </summary>
public sealed class EditorController
{
    private const int MaxUndo = 200;
    private const double HandlePx = 7;
    private const double HitPx = 6;

    private readonly StitchCache _cache = new();
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private EditorTool _tool = EditorTool.Select;

    // Arrastar
    private enum DragKind { None, Move, Node, Rubber }
    private DragKind _drag;
    private Vec2 _dragStart, _dragLast;
    private (int Path, int Index) _dragNode;
    private string? _pendingSnapshot;

    public EmbDesign Design { get; private set; } = new();
    public StitchPattern Pattern { get; private set; } = new();
    public StitchRenderData? RenderData { get; private set; }
    public List<EmbObject> Selection { get; } = new();
    public List<PathNode> Drawing { get; } = new();
    public Vec2? Cursor { get; private set; }
    public ThreadColor CurrentColor { get; set; } = BrotherPalette.Colors[5];
    public string? FilePath { get; set; }
    public bool IsDirty { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Desenho mudou (pontos recalculados).</summary>
    public event Action? DesignChanged;
    public event Action? SelectionChanged;
    public event Action? ToolChanged;
    /// <summary>Só a sobreposição (seleção, prévia) precisa ser redesenhada.</summary>
    public event Action? OverlayChanged;
    public event Action<string>? Message;
    /// <summary>A ferramenta Texto foi clicada: a janela pede o conteúdo e chama <see cref="AddText"/>.</summary>
    public event Action<Vec2>? TextRequested;
    /// <summary>Duplo-clique num texto: a janela pede o novo conteúdo e chama <see cref="SetText"/>.</summary>
    public event Action<TextObject>? EditTextRequested;

    public EditorTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            _tool = value;
            Drawing.Clear();
            if (value == EditorTool.Hole && !(Selection.Count == 1 && Selection[0] is ShapeObject))
                Message?.Invoke("Selecione um preenchimento ou aplique antes de desenhar o furo.");
            ToolChanged?.Invoke();
            OverlayChanged?.Invoke();
        }
    }

    public string ToolHint => _tool switch
    {
        EditorTool.Select => "Clique para selecionar · arraste para mover · Ctrl+clique no contorno insere nó · duplo-clique no nó remove · clique direito no nó alterna canto/curva",
        EditorTool.Text => "Clique onde o texto deve começar (linha de base) · depois ajuste fonte, altura e espaçamento em Propriedades · duplo-clique num texto (ferramenta Selecionar) edita o conteúdo",
        EditorTool.Applique => "Clique os cantos do contorno do tecido do aplique · esquerdo = canto · direito = curva · Enter ou duplo-clique conclui · depois ajuste a largura do acabamento e as cores em Propriedades",
        EditorTool.Satin => "Clique alternando os dois lados da coluna (A, B, A, B...) · esquerdo = canto · direito = curva · Enter conclui · Esc cancela · Backspace desfaz ponto",
        _ => "Clique esquerdo = canto · clique direito = curva · Shift = ângulo de 15° · Enter ou duplo-clique conclui · Esc cancela · Backspace desfaz ponto",
    };

    // ---------- Documento ----------

    public void LoadDesign(EmbDesign design, string? path)
    {
        Design = design;
        FilePath = path;
        Selection.Clear();
        Drawing.Clear();
        _undo.Clear();
        _redo.Clear();
        IsDirty = false;
        Rebuild();
        SelectionChanged?.Invoke();
    }

    public void MarkSaved(string path)
    {
        FilePath = path;
        IsDirty = false;
        DesignChanged?.Invoke();
    }

    public void Rebuild()
    {
        var name = FilePath != null ? System.IO.Path.GetFileNameWithoutExtension(FilePath) : "Bordadox";
        Pattern = Design.BuildPattern(name, _cache);
        RenderData = Design.Objects.Count > 0 ? new StitchRenderData(Pattern) : null;
        DesignChanged?.Invoke();
    }

    /// <summary>Quantidade de pontos gerados pelo objeto na última montagem.</summary>
    public int StitchCountOf(EmbObject o) => _cache.Peek(o.Id)?.Sum(r => r.Count) ?? 0;

    /// <summary>Marca como modificado sem registrar desfazer (ex.: imagem de fundo, bastidor).</summary>
    public void MarkDirty()
    {
        IsDirty = true;
        DesignChanged?.Invoke();
    }

    /// <summary>Troca o bastidor e/ou a orientação (com desfazer).</summary>
    public void SetHoop(string hoopName, bool rotated)
    {
        if (Design.HoopName == hoopName && Design.HoopRotated == rotated) return;
        Change(() =>
        {
            Design.HoopName = hoopName;
            Design.HoopRotated = rotated;
        });
    }

    /// <summary>Executa uma alteração com desfazer, recálculo e marca de modificado.</summary>
    public void Change(Action action)
    {
        Checkpoint();
        action();
        IsDirty = true;
        Rebuild();
    }

    // ---------- Desfazer / refazer ----------

    private sealed class Snapshot
    {
        public string HoopName { get; set; } = "";
        public bool HoopRotated { get; set; }
        public DesignSettings Settings { get; set; } = new();
        public List<EmbObject> Objects { get; set; } = new();
        public List<Guid> Selected { get; set; } = new();
    }

    private string TakeSnapshot() => JsonSerializer.Serialize(new Snapshot
    {
        HoopName = Design.HoopName,
        HoopRotated = Design.HoopRotated,
        Settings = Design.Settings,
        Objects = Design.Objects,
        Selected = Selection.Select(o => o.Id).ToList(),
    }, DesignJson.Options);

    private void RestoreSnapshot(string json)
    {
        var s = JsonSerializer.Deserialize<Snapshot>(json, DesignJson.Options)!;
        Design.HoopName = s.HoopName;
        Design.HoopRotated = s.HoopRotated;
        Design.Settings = s.Settings;
        Design.Objects = s.Objects;
        Selection.Clear();
        Selection.AddRange(Design.Objects.Where(o => s.Selected.Contains(o.Id)));
        IsDirty = true;
        Rebuild();
        SelectionChanged?.Invoke();
    }

    public void Checkpoint()
    {
        _undo.Add(TakeSnapshot());
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
    }

    public void Undo()
    {
        if (Drawing.Count > 0) { Drawing.Clear(); OverlayChanged?.Invoke(); return; }
        if (_undo.Count == 0) return;
        _redo.Add(TakeSnapshot());
        var s = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        RestoreSnapshot(s);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Add(TakeSnapshot());
        var s = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        RestoreSnapshot(s);
    }

    // ---------- Seleção e operações ----------

    public void Select(IEnumerable<EmbObject> objs)
    {
        Selection.Clear();
        Selection.AddRange(objs.Distinct());
        SelectionChanged?.Invoke();
        OverlayChanged?.Invoke();
    }

    public void SelectAll() => Select(Design.Objects);

    public void DeleteSelection()
    {
        if (Selection.Count == 0) return;
        Change(() => Design.Objects.RemoveAll(o => Selection.Contains(o)));
        Select(Array.Empty<EmbObject>());
    }

    public void DuplicateSelection()
    {
        if (Selection.Count == 0) return;
        var copies = Selection.Select(o => o.Clone()).ToList();
        Change(() =>
        {
            foreach (var c in copies) c.Transform(p => p + new Vec2(5, 5));
            Design.Objects.AddRange(copies);
        });
        Select(copies);
    }

    public string? CopySelection() =>
        Selection.Count == 0 ? null : JsonSerializer.Serialize(Selection, DesignJson.Options);

    public void Paste(string json)
    {
        List<EmbObject>? objs;
        try { objs = JsonSerializer.Deserialize<List<EmbObject>>(json, DesignJson.Options); }
        catch (JsonException) { return; }
        if (objs == null || objs.Count == 0) return;
        var copies = objs.Select(o => o.Clone()).ToList();
        Change(() =>
        {
            foreach (var c in copies) c.Transform(p => p + new Vec2(5, 5));
            Design.Objects.AddRange(copies);
        });
        Select(copies);
    }

    public void AddObjects(IEnumerable<EmbObject> objs)
    {
        var list = objs.ToList();
        if (list.Count == 0) return;
        Change(() => Design.Objects.AddRange(list));
        Select(list);
    }

    private Vec2 SelectionCenter() =>
        Selection.Aggregate(Rect2.Empty, (r, o) => r.Union(o.Bounds())).Center;

    public void TransformSelection(Func<Vec2, Vec2, Vec2> f)
    {
        if (Selection.Count == 0) return;
        var c = SelectionCenter();
        Change(() => { foreach (var o in Selection) o.Transform(p => f(p, c)); });
        OverlayChanged?.Invoke();
    }

    public void Rotate(double degrees)
    {
        double rad = degrees * Math.PI / 180;
        TransformSelection((p, c) => p.RotateAround(c, rad));
        // Preenchimentos: gira também a direção das linhas.
        foreach (var f in Selection.OfType<FillObject>()) f.Angle = NormalizeAngle(f.Angle + degrees);
        Rebuild();
    }

    private static double NormalizeAngle(double a)
    {
        a %= 360;
        if (a > 180) a -= 360;
        if (a <= -180) a += 360;
        return a;
    }

    public void Mirror(bool horizontal)
    {
        TransformSelection((p, c) => horizontal ? new Vec2(2 * c.X - p.X, p.Y) : new Vec2(p.X, 2 * c.Y - p.Y));
        foreach (var f in Selection.OfType<FillObject>()) f.Angle = NormalizeAngle(-f.Angle);
        Rebuild();
    }

    public void Scale(double factor) => TransformSelection((p, c) => c + (p - c) * factor);

    public void CenterInHoop()
    {
        if (Selection.Count == 0) return;
        var c = SelectionCenter();
        Change(() => { foreach (var o in Selection) o.Transform(p => p - c); });
        OverlayChanged?.Invoke();
    }

    public void Nudge(double dx, double dy)
    {
        if (Selection.Count == 0) return;
        Change(() => { foreach (var o in Selection) o.Transform(p => p + new Vec2(dx, dy)); });
        OverlayChanged?.Invoke();
    }

    public void SetColor(ThreadColor color)
    {
        CurrentColor = color;
        if (Selection.Count == 0) return;
        Change(() => { foreach (var o in Selection) o.Color = color; });
        SelectionChanged?.Invoke();
    }

    /// <summary>
    /// Converte os preenchimentos selecionados em aplique (ou os apliques em preenchimento), mantendo o contorno, os furos,
    /// o nome, a cor e a posição na ordem de costura. Devolve quantos objetos foram convertidos.
    /// </summary>
    public int ConvertShapes(bool toApplique)
    {
        var targets = Selection.Where(o => toApplique ? o is FillObject : o is AppliqueObject).Cast<ShapeObject>().ToList();
        if (targets.Count == 0) return 0;
        var created = new List<EmbObject>();
        Change(() =>
        {
            foreach (var old in targets)
            {
                ShapeObject neu = toApplique ? new AppliqueObject() : new FillObject();
                neu.Outline = old.Outline.ToList();
                neu.Holes = old.Holes.Select(h => h.ToList()).ToList();
                neu.Color = old.Color;
                neu.Name = old.Name;
                int i = Design.Objects.IndexOf(old);
                Design.Objects[i] = neu;
                created.Add(neu);
            }
        });
        Select(created);
        return created.Count;
    }

    /// <summary>
    /// Explode os objetos selecionados em partes independentes (ver <see cref="Exploder"/>), no mesmo lugar da ordem de costura.
    /// Devolve quantas partes foram criadas (0 se nada podia ser explodido). Um só Ctrl+Z desfaz tudo.
    /// </summary>
    public int Explode()
    {
        var targets = Selection.Where(Exploder.CanExplode).ToList();
        if (targets.Count == 0) return 0;
        var created = new List<EmbObject>();
        Change(() =>
        {
            foreach (var old in targets)
            {
                var parts = Exploder.Explode(old);
                int i = Design.Objects.IndexOf(old);
                Design.Objects.RemoveAt(i);
                Design.Objects.InsertRange(i, parts);
                created.AddRange(parts);
            }
        });
        Select(created);
        return created.Count;
    }

    /// <summary>Move a seleção na ordem de costura (-1 = antes, +1 = depois).</summary>
    public void MoveInSequence(int dir)
    {
        if (Selection.Count == 0) return;
        Change(() =>
        {
            var list = Design.Objects;
            var idx = Selection.Select(o => list.IndexOf(o)).Where(i => i >= 0).OrderBy(i => i).ToList();
            if (dir > 0) idx.Reverse();
            foreach (var i in idx)
            {
                int j = i + dir;
                if (j < 0 || j >= list.Count || Selection.Contains(list[j])) continue;
                (list[i], list[j]) = (list[j], list[i]);
            }
        });
        SelectionChanged?.Invoke();
    }

    // ---------- Mouse ----------

    public void MouseDown(Vec2 p, MouseButton button, int clicks, ModifierKeys mods, double px)
    {
        if (_tool == EditorTool.Select) SelectDown(p, button, clicks, mods, px);
        else if (_tool == EditorTool.Text)
        {
            if (button == MouseButton.Left && clicks == 1) TextRequested?.Invoke(p);
        }
        else DrawDown(p, button, clicks, mods);
    }

    /// <summary>Atalho com valores fixos (Arial, 8 mm); a janela de texto é que usa as preferências lembradas.</summary>
    public void AddText(Vec2 at, string text) => AddText(at, new TextSpec(text, "Arial", 8, false, false));

    public void AddText(Vec2 at, TextSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Text)) return;
        var obj = new TextObject
        {
            Name = $"Texto {Design.Objects.Count + 1}",
            Origin = at,
            Color = CurrentColor,
        };
        spec.Apply(obj);
        AddObjects(new[] { obj });
    }

    /// <summary>Aplica conteúdo, fonte, altura e estilo de uma vez (um único desfazer).</summary>
    public void ApplyText(TextObject obj, TextSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Text) || spec.Matches(obj)) return;
        Change(() => spec.Apply(obj));
        SelectionChanged?.Invoke();
    }

    public void SetText(TextObject obj, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || obj.Text == text) return;
        Change(() => obj.Text = text);
        SelectionChanged?.Invoke();
    }

    public void MouseMove(Vec2 p, bool leftDown, ModifierKeys mods)
    {
        Cursor = p;
        if (_tool != EditorTool.Select)
        {
            if (Drawing.Count > 0) OverlayChanged?.Invoke();
            return;
        }
        if (!leftDown || _drag == DragKind.None) return;

        if (_pendingSnapshot != null && Vec2.Distance(p, _dragStart) > 0.05 && _drag != DragKind.Rubber)
        {
            _undo.Add(_pendingSnapshot);
            _redo.Clear();
            _pendingSnapshot = null;
            IsDirty = true;
        }

        switch (_drag)
        {
            case DragKind.Move:
                var d = p - _dragLast;
                foreach (var o in Selection) o.Transform(q => q + d);
                Rebuild();
                break;
            case DragKind.Node:
                var obj = Selection[0];
                var old = obj.EditablePaths[_dragNode.Path][_dragNode.Index];
                obj.SetNode(_dragNode.Path, _dragNode.Index, old with { P = p });
                Rebuild();
                break;
        }
        _dragLast = p;
        OverlayChanged?.Invoke();
    }

    public void MouseUp(Vec2 p, ModifierKeys mods)
    {
        if (_drag == DragKind.Rubber)
        {
            var r = Rect2.Empty.Include(_dragStart).Include(p);
            if (r.Width > 0.5 || r.Height > 0.5)
            {
                var hits = Design.Objects.Where(o => Intersects(r, o.Bounds()));
                Select(mods.HasFlag(ModifierKeys.Control) ? Selection.Concat(hits).ToList() : hits.ToList());
            }
        }
        if (_drag is DragKind.Move or DragKind.Node) SelectionChanged?.Invoke();
        _drag = DragKind.None;
        _pendingSnapshot = null;
        OverlayChanged?.Invoke();
    }

    private static bool Intersects(Rect2 a, Rect2 b) =>
        !b.IsEmpty && a.MinX <= b.MaxX && a.MaxX >= b.MinX && a.MinY <= b.MaxY && a.MaxY >= b.MinY;

    private void SelectDown(Vec2 p, MouseButton button, int clicks, ModifierKeys mods, double px)
    {
        double tol = HitPx * px;
        bool ctrl = mods.HasFlag(ModifierKeys.Control) || mods.HasFlag(ModifierKeys.Shift);

        // Alças de nó (só com um objeto selecionado).
        if (Selection.Count == 1 && FindHandle(Selection[0], p, HandlePx * px) is { } h)
        {
            var obj = Selection[0];
            if (button == MouseButton.Right)
            {
                var n = obj.EditablePaths[h.Path][h.Index];
                Change(() => obj.SetNode(h.Path, h.Index, n with { Curve = !n.Curve }));
                return;
            }
            if (clicks == 2)
            {
                Checkpoint();
                if (obj.RemoveNode(h.Path, h.Index)) { IsDirty = true; Rebuild(); }
                else { _undo.RemoveAt(_undo.Count - 1); Message?.Invoke("Este contorno precisa de mais nós."); }
                OverlayChanged?.Invoke();
                return;
            }
            StartDrag(DragKind.Node, p);
            _dragNode = h;
            return;
        }

        if (button != MouseButton.Left) return;

        // Ctrl+clique no contorno do objeto selecionado: insere nó.
        if (Selection.Count == 1 && mods.HasFlag(ModifierKeys.Control) && TryInsertNode(Selection[0], p, tol)) return;

        var hit = HitTest(p, tol);
        if (hit != null)
        {
            if (clicks == 2 && hit is TextObject text && !ctrl)
            {
                Select(new[] { hit });
                EditTextRequested?.Invoke(text);
                return;
            }
            if (ctrl)
            {
                if (Selection.Contains(hit)) Selection.Remove(hit); else Selection.Add(hit);
                SelectionChanged?.Invoke();
                OverlayChanged?.Invoke();
                return;
            }
            if (!Selection.Contains(hit)) Select(new[] { hit });
            StartDrag(DragKind.Move, p);
            return;
        }

        if (!ctrl && Selection.Count > 0) Select(Array.Empty<EmbObject>());
        StartDrag(DragKind.Rubber, p);
    }

    private void StartDrag(DragKind kind, Vec2 p)
    {
        _drag = kind;
        _dragStart = _dragLast = p;
        _pendingSnapshot = kind == DragKind.Rubber ? null : TakeSnapshot();
    }

    public EmbObject? HitTest(Vec2 p, double tol)
    {
        // De cima para baixo: o último costurado fica por cima.
        for (int i = Design.Objects.Count - 1; i >= 0; i--)
        {
            var o = Design.Objects[i];
            if (o is StitchBlockObject block)
            {
                if (block.Runs.Any(r => GeometryUtil.DistanceToPolyline(p, r, false) <= tol)) return o;
            }
            else if (o.HitTest(p, tol)) return o;
        }
        return null;
    }

    private static (int Path, int Index)? FindHandle(EmbObject obj, Vec2 p, double tol)
    {
        (int, int)? best = null;
        double bestD = tol;
        var paths = obj.EditablePaths;
        for (int pi = 0; pi < paths.Count; pi++)
            for (int i = 0; i < paths[pi].Count; i++)
            {
                double d = Vec2.Distance(paths[pi][i].P, p);
                if (d <= bestD) { bestD = d; best = (pi, i); }
            }
        return best;
    }

    private bool TryInsertNode(EmbObject obj, Vec2 p, double tol)
    {
        var paths = obj.EditablePaths;
        for (int pi = 0; pi < paths.Count; pi++)
        {
            var nodes = paths[pi];
            if (obj is SatinObject satin)
            {
                // Satin: procura o trecho entre pares consecutivos em qualquer trilho.
                var a = satin.RailA; var b = satin.RailB;
                for (int k = 0; k + 1 < a.Count; k++)
                {
                    bool onA = GeometryUtil.DistanceToSegment(p, a[k].P, a[k + 1].P) <= tol;
                    bool onB = GeometryUtil.DistanceToSegment(p, b[k].P, b[k + 1].P) <= tol;
                    if (!onA && !onB) continue;
                    Change(() =>
                    {
                        var pa = onA ? p : Vec2.Lerp(a[k].P, a[k + 1].P, 0.5);
                        var pb = onB ? p : Vec2.Lerp(b[k].P, b[k + 1].P, 0.5);
                        satin.Nodes.Insert((k + 1) * 2, new PathNode(pb, b[k].Curve));
                        satin.Nodes.Insert((k + 1) * 2, new PathNode(pa, a[k].Curve));
                    });
                    return true;
                }
                return false;
            }

            int count = obj.ClosedPaths ? nodes.Count : nodes.Count - 1;
            for (int i = 0; i < count; i++)
            {
                var a = nodes[i].P;
                var b = nodes[(i + 1) % nodes.Count].P;
                if (GeometryUtil.DistanceToSegment(p, a, b) > tol) continue;
                int path = pi, at = i + 1;
                Change(() => obj.InsertNode(path, at, new PathNode(p)));
                return true;
            }
        }
        return false;
    }

    // ---------- Desenho de objetos ----------

    private Vec2 Constrain(Vec2 p, ModifierKeys mods)
    {
        if (!mods.HasFlag(ModifierKeys.Shift) || Drawing.Count == 0) return p;
        var last = Drawing[^1].P;
        var d = p - last;
        double step = Math.PI / 12;
        double ang = Math.Round(Math.Atan2(d.Y, d.X) / step) * step;
        return last + new Vec2(Math.Cos(ang), Math.Sin(ang)) * d.Length;
    }

    private void DrawDown(Vec2 p, MouseButton button, int clicks, ModifierKeys mods)
    {
        if (button == MouseButton.Left && clicks == 2) { FinishDrawing(); return; }
        if (button is not (MouseButton.Left or MouseButton.Right)) return;
        p = Constrain(p, mods);
        if (Drawing.Count > 0 && Vec2.Distance(Drawing[^1].P, p) < 0.05) return;
        Drawing.Add(new PathNode(p, button == MouseButton.Right));
        OverlayChanged?.Invoke();
    }

    public bool KeyDown(Key key)
    {
        switch (key)
        {
            case Key.Enter when _tool != EditorTool.Select:
                FinishDrawing();
                return true;
            case Key.Escape:
                if (Drawing.Count > 0) Drawing.Clear();
                else if (_tool != EditorTool.Select) Tool = EditorTool.Select;
                else if (Selection.Count > 0) Select(Array.Empty<EmbObject>());
                OverlayChanged?.Invoke();
                return true;
            case Key.Back when Drawing.Count > 0:
                Drawing.RemoveAt(Drawing.Count - 1);
                OverlayChanged?.Invoke();
                return true;
        }
        return false;
    }

    public void FinishDrawing()
    {
        var nodes = Drawing.ToList();
        Drawing.Clear();
        OverlayChanged?.Invoke();

        int n = Design.Objects.Count + 1;
        EmbObject? obj = null;
        switch (_tool)
        {
            case EditorTool.Run when nodes.Count >= 2:
                obj = new RunObject { Nodes = nodes, Name = $"Corrido {n}" };
                break;
            case EditorTool.Satin when nodes.Count >= 4:
                obj = new SatinObject { Nodes = nodes.Take(nodes.Count / 2 * 2).ToList(), Name = $"Satin {n}" };
                break;
            case EditorTool.Fill when nodes.Count >= 3:
                obj = new FillObject { Outline = nodes, Name = $"Preenchimento {n}" };
                break;
            case EditorTool.Applique when nodes.Count >= 3:
                obj = new AppliqueObject { Outline = nodes, Name = $"Aplique {n}" };
                break;
            case EditorTool.Hole when nodes.Count >= 3:
                if (Selection.Count == 1 && Selection[0] is ShapeObject fill)
                {
                    Change(() => fill.Holes.Add(nodes));
                    OverlayChanged?.Invoke();
                }
                else Message?.Invoke("Selecione um preenchimento ou aplique antes de desenhar o furo.");
                return;
            default:
                if (nodes.Count > 0)
                    Message?.Invoke(_tool == EditorTool.Satin
                        ? "A coluna satin precisa de pelo menos 2 pares de pontos (4 cliques)."
                        : "Pontos insuficientes para criar o objeto.");
                return;
        }

        obj.Color = CurrentColor;
        AddObjects(new[] { obj });
    }

    // ---------- Sobreposição (seleção e prévia) ----------

    private static readonly Color Accent = Color.FromRgb(0x1E, 0x88, 0xE5);
    private static readonly Color DrawColor = Color.FromRgb(0xE6, 0x51, 0x00);

    public void DrawOverlay(DrawingContext dc, double px)
    {
        var accent = new SolidColorBrush(Accent);
        var dashed = new Pen(accent, 1.5 * px) { DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0) };

        foreach (var o in Selection)
        {
            if (o is StitchBlockObject)
            {
                var b = o.Bounds();
                dc.DrawRectangle(null, dashed, new Rect(b.MinX, b.MinY, b.Width, b.Height));
                continue;
            }
            foreach (var outline in o.Outlines()) DrawPolyline(dc, dashed, outline, o.ClosedPaths);
        }

        if (Selection.Count == 1) DrawHandles(dc, Selection[0], px, accent);

        if (_drag == DragKind.Rubber && Cursor is { } cur)
        {
            var r = Rect2.Empty.Include(_dragStart).Include(cur);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x20, Accent.R, Accent.G, Accent.B)),
                new Pen(accent, px), new Rect(r.MinX, r.MinY, r.Width, r.Height));
        }

        if (_tool != EditorTool.Select && Drawing.Count > 0) DrawPreview(dc, px);
    }

    private void DrawHandles(DrawingContext dc, EmbObject obj, double px, Brush accent)
    {
        var pen = new Pen(accent, 1.2 * px);
        if (obj is SatinObject satin)
        {
            // Linhas de direção entre os pares A/B.
            var rung = new Pen(new SolidColorBrush(Color.FromArgb(0xA0, Accent.R, Accent.G, Accent.B)), px);
            for (int i = 0; i + 1 < satin.Nodes.Count; i += 2)
                dc.DrawLine(rung, ToPoint(satin.Nodes[i].P), ToPoint(satin.Nodes[i + 1].P));
        }
        foreach (var path in obj.EditablePaths)
            for (int i = 0; i < path.Count; i++)
                DrawNode(dc, path[i], px, i == 0 ? accent : Brushes.White, pen);
    }

    private static void DrawNode(DrawingContext dc, PathNode n, double px, Brush fill, Pen pen)
    {
        double r = HandlePx / 2 * px;
        if (n.Curve) dc.DrawEllipse(fill, pen, ToPoint(n.P), r, r);
        else dc.DrawRectangle(fill, pen, new Rect(n.P.X - r, n.P.Y - r, 2 * r, 2 * r));
    }

    private void DrawPreview(DrawingContext dc, double px)
    {
        var brush = new SolidColorBrush(DrawColor);
        var pen = new Pen(brush, 1.5 * px);
        var nodes = Drawing.ToList();
        if (Cursor is { } c) nodes.Add(new PathNode(Constrain(c, Keyboard.Modifiers)));

        if (_tool == EditorTool.Satin)
        {
            var a = nodes.Where((_, i) => i % 2 == 0).ToList();
            var b = nodes.Where((_, i) => i % 2 == 1).ToList();
            DrawPolyline(dc, pen, PathUtil.Flatten(a, false), false);
            DrawPolyline(dc, pen, PathUtil.Flatten(b, false), false);
            var thin = new Pen(brush, px) { DashStyle = DashStyles.Dot };
            for (int i = 0; i + 1 < nodes.Count; i += 2) dc.DrawLine(thin, ToPoint(nodes[i].P), ToPoint(nodes[i + 1].P));
            if (nodes.Count % 2 == 0 && nodes.Count >= 2)
                dc.DrawLine(thin, ToPoint(nodes[^2].P), ToPoint(nodes[^1].P));
        }
        else
        {
            bool closed = _tool is EditorTool.Fill or EditorTool.Hole or EditorTool.Applique;
            DrawPolyline(dc, pen, PathUtil.Flatten(nodes, closed && nodes.Count >= 3), closed && nodes.Count >= 3);
        }
        foreach (var n in Drawing) DrawNode(dc, n, px, Brushes.White, pen);
    }

    private static void DrawPolyline(DrawingContext dc, Pen pen, IReadOnlyList<Vec2> pts, bool closed)
    {
        if (pts.Count < 2) return;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(ToPoint(pts[0]), false, closed);
            for (int i = 1; i < pts.Count; i++) ctx.LineTo(ToPoint(pts[i]), true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }

    private static Point ToPoint(Vec2 v) => new(v.X, v.Y);
}
