using System.Windows.Input;
using Bordadox.App.Editor;
using Bordadox.App.Text;
using Bordadox.Core.Design;
using Bordadox.Core.Geometry;
using Bordadox.Core.Text;

namespace Bordadox.App.Tests;

/// <summary>Usa fontes reais do Windows (Arial e Segoe UI vêm com o sistema).</summary>
public class TextAppTests
{
    private static readonly WpfGlyphProvider Provider = new();

    public TextAppTests() => GlyphService.Provider = Provider;

    [Fact]
    public void RealFont_CapHeightAndGlyphShape()
    {
        double cap = Provider.CapHeight("Arial", false, false);
        Assert.InRange(cap, 0.65, 0.75);

        var i = Provider.GetGlyph("Arial", false, false, 'I')!;
        var pts = i.Contours.SelectMany(c => c).ToList();
        Assert.InRange(i.Advance, 0.1, 0.6);
        Assert.True(pts.Min(p => p.Y) < 0, "letras ficam acima da linha de base (Y negativo)");
        Assert.InRange(pts.Min(p => p.Y), -cap - 0.02, -cap + 0.02);
        Assert.InRange(pts.Max(p => p.Y), -0.02, 0.02);
    }

    [Fact]
    public void RealFont_LetterOHasHole_SpaceIsEmpty_MissingIsNull()
    {
        Assert.Equal(2, Provider.GetGlyph("Arial", false, false, 'O')!.Contours.Count);
        var space = Provider.GetGlyph("Arial", false, false, ' ')!;
        Assert.Empty(space.Contours);
        Assert.True(space.Advance > 0.1);
        Assert.Null(Provider.GetGlyph("Arial", false, false, 0x10FFFF));
    }

    [Fact]
    public void RealFont_BoldIsWiderThanRegular()
    {
        double Width(bool bold) => Provider.GetGlyph("Arial", bold, false, 'M')!.Contours.SelectMany(c => c).Max(p => p.X)
                                 - Provider.GetGlyph("Arial", bold, false, 'M')!.Contours.SelectMany(c => c).Min(p => p.X);
        Assert.True(Width(true) > Width(false));
    }

    [Fact]
    public void UnknownFont_FallsBackInsteadOfFailing()
    {
        Assert.NotNull(Provider.GetGlyph("Fonte Que Nao Existe 123", false, false, 'A'));
    }

    [Fact]
    public void TextObject_WithRealFont_MakesSensibleStitches()
    {
        var t = new TextObject { Text = "Bordadox", FontFamily = "Arial", HeightMm = 10, Bold = true };
        var b = t.Bounds();
        Assert.InRange(b.Width, 30, 70);
        Assert.InRange(b.Height, 9.5, 14.5); // maiúscula 10 mm; 'd' passa um pouco da altura
        var design = new EmbDesign();
        design.Objects.Add(t);
        var p = design.BuildPattern();
        Assert.InRange(p.StitchCount, 500, 6000);
        Assert.All(p.Stitches.Where(s => s.Command == Core.Stitches.StitchCommand.Stitch),
            s => Assert.True(Math.Abs(s.X / 10.0) < 100 && Math.Abs(s.Y / 10.0) < 40));
    }

    // ---------- Editor ----------

    private static EditorController NewEditor()
    {
        var e = new EditorController();
        e.LoadDesign(new EmbDesign(), null);
        return e;
    }

    [Fact]
    public void TextTool_ClickRequestsText_AddTextCreatesObject_UndoRemoves()
    {
        var e = NewEditor();
        Vec2? asked = null;
        e.TextRequested += p => asked = p;
        e.Tool = EditorTool.Text;
        e.MouseDown(new Vec2(12, 7), MouseButton.Left, 1, ModifierKeys.None, 0.1);
        e.MouseUp(new Vec2(12, 7), ModifierKeys.None);
        Assert.Equal(new Vec2(12, 7), asked);
        Assert.Empty(e.Design.Objects); // só cria depois que a janela devolve o texto

        e.AddText(asked!.Value, "Oi");
        var t = Assert.IsType<TextObject>(Assert.Single(e.Design.Objects));
        Assert.Equal(new Vec2(12, 7), t.Origin);
        Assert.Same(t, Assert.Single(e.Selection));
        Assert.True(e.Pattern.StitchCount > 100);

        e.Undo();
        Assert.Empty(e.Design.Objects);
    }

    [Fact]
    public void BlankText_IsIgnored()
    {
        var e = NewEditor();
        e.AddText(Vec2.Zero, "   ");
        Assert.Empty(e.Design.Objects);
    }

    [Fact]
    public void DoubleClickOnText_RequestsEditing_AndSetTextIsUndoable()
    {
        var e = NewEditor();
        e.AddText(new Vec2(0, 0), "Ola");
        var t = (TextObject)e.Design.Objects[0];
        e.Tool = EditorTool.Select;
        e.Select(Array.Empty<EmbObject>());

        TextObject? edit = null;
        e.EditTextRequested += o => edit = o;
        var inside = t.Bounds().Center;
        e.MouseDown(inside, MouseButton.Left, 2, ModifierKeys.None, 0.1);
        e.MouseUp(inside, ModifierKeys.None);
        Assert.Same(t, edit);

        e.SetText(t, "Tchau");
        Assert.Equal("Tchau", t.Text);
        e.Undo();
        Assert.Equal("Ola", ((TextObject)e.Design.Objects[0]).Text);
    }

    [Fact]
    public void RotateAndMirrorSelection_UpdateTextObject()
    {
        var e = NewEditor();
        e.AddText(new Vec2(0, 0), "AB");
        var t = (TextObject)e.Design.Objects[0];
        double w = t.Bounds().Width;

        e.Rotate(90);
        Assert.Equal(90, t.RotationDeg, 3);
        Assert.Equal(w, t.Bounds().Height, 2);

        e.Scale(2);
        Assert.Equal(16, t.HeightMm, 3);
    }
}
