using System.IO;
using System.Windows.Input;
using Bordadox.App.Editor;
using Bordadox.Core.Design;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;

namespace Bordadox.App.Tests;

/// <summary>Simula o uso do editor (cliques em mm) sem abrir janela.</summary>
public class EditorTests
{
    private const double Px = 0.1; // 1 pixel = 0,1 mm (zoom alto)

    private static EditorController NewEditor()
    {
        var e = new EditorController();
        e.LoadDesign(new EmbDesign(), null);
        return e;
    }

    private static void Click(EditorController e, double x, double y, MouseButton b = MouseButton.Left, int clicks = 1, ModifierKeys mods = ModifierKeys.None)
    {
        e.MouseDown(new Vec2(x, y), b, clicks, mods, Px);
        e.MouseUp(new Vec2(x, y), mods);
    }

    private static void Drag(EditorController e, Vec2 from, Vec2 to)
    {
        e.MouseDown(from, MouseButton.Left, 1, ModifierKeys.None, Px);
        e.MouseMove(Vec2.Lerp(from, to, 0.5), true, ModifierKeys.None);
        e.MouseMove(to, true, ModifierKeys.None);
        e.MouseUp(to, ModifierKeys.None);
    }

    private static FillObject DrawSquareFill(EditorController e, double x = 0, double y = 0, double size = 20)
    {
        e.Tool = EditorTool.Fill;
        Click(e, x, y);
        Click(e, x + size, y);
        Click(e, x + size, y + size);
        Click(e, x, y + size);
        e.KeyDown(Key.Enter);
        return (FillObject)e.Design.Objects[^1];
    }

    [Fact]
    public void DrawFill_CreatesObjectWithCurrentColorAndSelectsIt()
    {
        var e = NewEditor();
        e.CurrentColor = BrotherPalette.Colors[13];
        var fill = DrawSquareFill(e);

        Assert.Single(e.Design.Objects);
        Assert.Equal(4, fill.Outline.Count);
        Assert.Equal(BrotherPalette.Colors[13], fill.Color);
        Assert.Same(fill, Assert.Single(e.Selection));
        // 20 × 20 mm: 50 linhas de ~7 pontos + reforço por baixo.
        Assert.InRange(e.Pattern.StitchCount, 350, 700);
        Assert.True(e.IsDirty);
    }

    [Fact]
    public void RightClick_CreatesCurveNode_DoubleClickFinishes()
    {
        var e = NewEditor();
        e.Tool = EditorTool.Run;
        Click(e, 0, 0);
        Click(e, 10, 10, MouseButton.Right);
        Click(e, 20, 0);
        Click(e, 20, 0, clicks: 2);

        var run = Assert.IsType<RunObject>(Assert.Single(e.Design.Objects));
        Assert.Equal(new[] { false, true, false }, run.Nodes.Select(n => n.Curve));
    }

    [Fact]
    public void Satin_NeedsTwoPairs_AndDropsOddNode()
    {
        var e = NewEditor();
        e.Tool = EditorTool.Satin;
        Click(e, 0, 0); Click(e, 0, 4); Click(e, 20, 0);
        e.KeyDown(Key.Enter);
        Assert.Empty(e.Design.Objects); // só 3 pontos: não cria

        Click(e, 0, 0); Click(e, 0, 4); Click(e, 20, 0); Click(e, 20, 4); Click(e, 30, 0);
        e.KeyDown(Key.Enter);
        var satin = Assert.IsType<SatinObject>(Assert.Single(e.Design.Objects));
        Assert.Equal(4, satin.Nodes.Count);
    }

    [Fact]
    public void EscapeCancelsDrawing_ThenReturnsToSelect()
    {
        var e = NewEditor();
        e.Tool = EditorTool.Fill;
        Click(e, 0, 0); Click(e, 10, 0);
        e.KeyDown(Key.Escape);
        Assert.Empty(e.Drawing);
        Assert.Equal(EditorTool.Fill, e.Tool);
        e.KeyDown(Key.Escape);
        Assert.Equal(EditorTool.Select, e.Tool);
    }

    [Fact]
    public void Select_DragMovesObject_AndUndoRestores()
    {
        var e = NewEditor();
        var fill = DrawSquareFill(e);
        e.Tool = EditorTool.Select;
        e.Select(Array.Empty<EmbObject>());

        Drag(e, new Vec2(10, 10), new Vec2(15, 12)); // clica dentro do quadrado e arrasta
        Assert.Equal(new Vec2(5, 2), fill.Outline[0].P);

        e.Undo();
        var restored = (FillObject)e.Design.Objects[0];
        Assert.Equal(new Vec2(0, 0), restored.Outline[0].P);
        e.Redo();
        Assert.Equal(new Vec2(5, 2), ((FillObject)e.Design.Objects[0]).Outline[0].P);
    }

    [Fact]
    public void DragNode_ToggleCurve_InsertAndRemoveNode()
    {
        var e = NewEditor();
        var fill = DrawSquareFill(e);
        e.Tool = EditorTool.Select;

        Drag(e, new Vec2(20, 20), new Vec2(25, 25)); // arrasta o 3º nó
        Assert.Equal(new Vec2(25, 25), fill.Outline[2].P);

        Click(e, 25, 25, MouseButton.Right); // alterna para curva
        Assert.True(fill.Outline[2].Curve);

        Click(e, 10, 0, mods: ModifierKeys.Control); // insere nó na aresta de cima
        Assert.Equal(5, fill.Outline.Count);
        Assert.Equal(new Vec2(10, 0), fill.Outline[1].P);

        Click(e, 10, 0, clicks: 2); // duplo-clique remove
        Assert.Equal(4, fill.Outline.Count);
    }

    [Fact]
    public void Hole_IsAddedToSelectedFill()
    {
        var e = NewEditor();
        var fill = DrawSquareFill(e, size: 30);
        e.Tool = EditorTool.Hole;
        Click(e, 10, 10); Click(e, 20, 10); Click(e, 20, 20); Click(e, 10, 20);
        e.KeyDown(Key.Enter);
        Assert.Single(fill.Holes);
        Assert.Single(e.Design.Objects);
    }

    [Fact]
    public void RubberBandSelectsObjects()
    {
        var e = NewEditor();
        DrawSquareFill(e, 0, 0, 10);
        DrawSquareFill(e, 30, 0, 10);
        DrawSquareFill(e, 0, 40, 10);
        e.Tool = EditorTool.Select;
        e.Select(Array.Empty<EmbObject>());

        Drag(e, new Vec2(-5, -5), new Vec2(45, 15)); // começa fora dos objetos
        Assert.Equal(2, e.Selection.Count);
    }

    [Fact]
    public void ColorSequenceAndTransforms()
    {
        var e = NewEditor();
        var a = DrawSquareFill(e, 0, 0, 10);
        var b = DrawSquareFill(e, 30, 0, 10);
        e.Select(new[] { b });
        e.SetColor(BrotherPalette.Colors[2]);
        Assert.Equal(2, e.Pattern.Threads.Count);

        e.MoveInSequence(-1);
        Assert.Same(b, e.Design.Objects[0]);

        a.Angle = 45;
        e.Select(new[] { a });
        e.Rotate(90);
        Assert.Equal(135, a.Angle);
        e.Mirror(true);
        Assert.Equal(-135, a.Angle);

        e.DuplicateSelection();
        Assert.Equal(3, e.Design.Objects.Count);
        e.DeleteSelection();
        Assert.Equal(2, e.Design.Objects.Count);
    }

    [Fact]
    public void TextWithFontAndHeight_ApplyIsOneUndoStep()
    {
        var e = NewEditor();
        e.AddText(new Vec2(0, 0), new TextSpec("Oi", "Times New Roman", 12, true, true));
        var t = Assert.IsType<TextObject>(Assert.Single(e.Design.Objects));
        Assert.Equal(("Oi", "Times New Roman", 12.0, true, true), (t.Text, t.FontFamily, t.HeightMm, t.Bold, t.Italic));

        e.ApplyText(t, new TextSpec("Tchau", "Arial", 6, false, false));
        Assert.Equal(("Tchau", "Arial", 6.0, false, false), (t.Text, t.FontFamily, t.HeightMm, t.Bold, t.Italic));

        e.Undo(); // um único passo desfaz conteúdo, fonte, altura e estilo
        var back = (TextObject)e.Design.Objects[0];
        Assert.Equal(("Oi", "Times New Roman", 12.0, true, true), (back.Text, back.FontFamily, back.HeightMm, back.Bold, back.Italic));

        e.ApplyText(back, TextSpec.From(back)); // sem mudança: não cria passo de desfazer
        e.Undo();
        Assert.Empty(e.Design.Objects);
    }

    [Fact]
    public void TextSpec_ParsesHeightAndChecksRange()
    {
        Assert.True(TextSpec.TryParseHeight("8", out var a) && a == 8);
        Assert.True(TextSpec.TryParseHeight("7,5", out var b) && b == 7.5);
        Assert.True(TextSpec.TryParseHeight("7.5", out var c) && c == 7.5);
        Assert.False(TextSpec.TryParseHeight("1", out _));
        Assert.False(TextSpec.TryParseHeight("101", out _));
        Assert.False(TextSpec.TryParseHeight("abc", out _));
        Assert.False(TextSpec.TryParseHeight("", out _));
    }

    [Fact]
    public void UserPrefs_RememberLastTextChoices()
    {
        var path = Path.Combine(Path.GetTempPath(), "bordadox-prefs-" + Guid.NewGuid(), "prefs.json");
        var original = UserPrefs.FilePath;
        try
        {
            UserPrefs.FilePath = path;
            UserPrefs.Reset();
            Assert.Equal("Arial", TextSpec.FromPreferences().FontFamily); // padrão sem arquivo

            var p = UserPrefs.Current;
            p.LastFont = "Segoe UI"; p.LastHeight = 12; p.LastBold = true;
            p.Save();

            UserPrefs.Reset();
            var again = TextSpec.FromPreferences();
            Assert.Equal(("Segoe UI", 12.0, true, false), (again.FontFamily, again.HeightMm, again.Bold, again.Italic));

            File.WriteAllText(path, "{ isto nao e json");
            UserPrefs.Reset();
            Assert.Equal("Arial", TextSpec.FromPreferences().FontFamily); // arquivo corrompido: volta ao padrão
        }
        finally
        {
            UserPrefs.FilePath = original;
            UserPrefs.Reset();
        }
    }

    [Fact]
    public void RotateHoop_CanBeUndone()
    {
        var e = NewEditor();
        e.SetHoop(e.Design.HoopName, true);
        Assert.True(e.Design.HoopRotated);
        Assert.Equal(260, e.Design.Hoop.WidthMm);
        e.Undo();
        Assert.False(e.Design.HoopRotated);
    }

    [Fact]
    public void CopyPaste_CreatesIndependentCopies()
    {
        var e = NewEditor();
        var fill = DrawSquareFill(e);
        var json = e.CopySelection()!;
        e.Paste(json);
        Assert.Equal(2, e.Design.Objects.Count);
        Assert.NotEqual(e.Design.Objects[0].Id, e.Design.Objects[1].Id);
        Assert.Equal(new Vec2(5, 5), ((FillObject)e.Design.Objects[1]).Outline[0].P);
    }
}
