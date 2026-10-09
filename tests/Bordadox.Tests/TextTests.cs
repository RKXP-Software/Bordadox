using Bordadox.Core.Design;
using Bordadox.Core.Formats;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;
using Bordadox.Core.Text;

namespace Bordadox.Tests;

/// <summary>Fonte falsa e previsível: cada letra é um retângulo; "O" tem um furo; espaço não desenha nada.</summary>
public sealed class FakeGlyphProvider : IGlyphProvider
{
    public const double Cap = 0.7, Width = 0.5, Advance = 0.6;

    public double CapHeight(string family, bool bold, bool italic) => Cap;

    public Glyph? GetGlyph(string family, bool bold, bool italic, int codePoint)
    {
        if (codePoint == '?') return null; // caractere inexistente na fonte
        if (codePoint == ' ') return new Glyph(0.3, Array.Empty<IReadOnlyList<Vec2>>());
        var outer = Rect(0, -Cap, Width, 0);
        var contours = new List<IReadOnlyList<Vec2>> { outer };
        if (codePoint == 'O') contours.Add(Rect(0.15, -Cap + 0.15, Width - 0.15, -0.15));
        return new Glyph(Advance, contours);
    }

    private static List<Vec2> Rect(double x0, double y0, double x1, double y1) =>
        new() { new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1) };
}

public class TextTests
{
    public TextTests() => GlyphService.Provider = new FakeGlyphProvider();

    private static TextObject Make(string text, double height = 10) =>
        new() { Text = text, HeightMm = height, Underlay = false, PullCompensation = 0, FillType = FillType.Tatami, Angle = 0 };

    private static Rect2 B(TextObject t) => t.Bounds();

    [Fact]
    public void Height_IsCapHeightInMm()
    {
        var b = B(Make("H", 12));
        Assert.Equal(12, b.Height, 3);
        Assert.Equal(0, b.MaxY, 3); // linha de base na origem
    }

    [Fact]
    public void Width_FollowsAdvanceAndLetterSpacing()
    {
        double em = 10 / FakeGlyphProvider.Cap;
        var t = Make("AB");
        // largura = avanço da 1ª letra + largura da 2ª
        Assert.Equal(FakeGlyphProvider.Advance * em + FakeGlyphProvider.Width * em, B(t).Width, 3);
        t.LetterSpacing = 2;
        Assert.Equal(FakeGlyphProvider.Advance * em + FakeGlyphProvider.Width * em + 2, B(t).Width, 3);
    }

    [Fact]
    public void Alignment_CenterAndRight()
    {
        var t = Make("AB");
        double w = B(t).Width;
        Assert.Equal(0, B(t).MinX, 3); // esquerda: a tinta começa na origem
        t.Alignment = TextAlign.Center;
        Assert.Equal(0, B(t).Center.X, 3);
        t.Alignment = TextAlign.Right;
        Assert.Equal(0, B(t).MaxX, 3);
        Assert.Equal(w, B(t).Width, 3);

        // Várias linhas: cada linha alinha por si.
        var two = Make("A\nABAB");
        two.Alignment = TextAlign.Right;
        Assert.All(two.Outlines().GroupBy(c => Math.Round(c.Max(p => p.Y))), g =>
            Assert.Equal(0, g.SelectMany(c => c).Max(p => p.X), 3));
    }

    [Fact]
    public void MultiLine_StacksLinesByLineSpacing()
    {
        var t = Make("A\nA");
        t.LineSpacing = 2;
        Assert.Equal(10 + 20, B(t).Height, 3);
        Assert.Equal(20, B(t).MaxY, 3); // 2ª linha: linha de base 20 mm abaixo
    }

    [Fact]
    public void MissingGlyphs_AndSpaces_AreSkipped()
    {
        Assert.Equal(B(Make("A")).Width, B(Make("?A")).Width, 3);
        double em = 10 / FakeGlyphProvider.Cap;
        // "A A": avanço de A (0,6) + avanço do espaço (0,3) + largura do 2º A (0,5)
        Assert.Equal((0.6 + 0.3 + 0.5) * em, B(Make("A A")).Width, 3);
        Assert.Empty(Make("   ").Outlines());
        Assert.Empty(Make("").GenerateRuns(null));
    }

    [Fact]
    public void Rotation_TurnsTextAroundOrigin()
    {
        var t = Make("AB");
        t.Origin = new Vec2(5, 5);
        t.RotationDeg = 90;
        var b = B(t);
        Assert.True(b.Height > b.Width * 1.5);
        Assert.Equal(90, t.RotationDeg, 6);
        // 90° horário: o texto passa a correr para baixo e o topo das letras aponta para a direita.
        Assert.Equal(5, b.MinX, 2);
        Assert.Equal(15, b.MaxX, 2);
        Assert.Equal(5, b.MinY, 2);
    }

    [Fact]
    public void Transform_ScaleMoveAndMirror()
    {
        var t = Make("AB");
        var before = B(t);

        t.Transform(p => p * 2 + new Vec2(10, 0));
        Assert.Equal(20, t.HeightMm, 6);
        Assert.Equal(before.Width * 2, B(t).Width, 3);
        Assert.Equal(10, B(t).MinX, 3);

        var t2 = Make("AB");
        t2.Transform(p => new Vec2(-p.X, p.Y)); // espelho horizontal
        var b2 = B(t2);
        Assert.Equal(-before.Width, b2.MinX, 3);
        Assert.Equal(0, b2.MaxX, 3);
        Assert.True(Vec2.Cross(t2.U, t2.V) < 0); // inverteu a orientação
    }

    [Fact]
    public void Stitches_StayInsideLettersAndRespectHoles()
    {
        var t = Make("O");
        var runs = t.GenerateRuns(null);
        Assert.NotEmpty(runs);
        double em = 10 / FakeGlyphProvider.Cap;
        var hole = new Rect2(0.15 * em, (-FakeGlyphProvider.Cap + 0.15) * em, (FakeGlyphProvider.Width - 0.15) * em, -0.15 * em);
        var outer = B(t);

        foreach (var run in runs)
            foreach (var (a, b) in run.Zip(run.Skip(1)))
            {
                var mid = Vec2.Lerp(a, b, 0.5);
                Assert.True(outer.Inflate(0.01).Contains(a) && outer.Inflate(0.01).Contains(b));
                bool inHole = mid.X > hole.MinX + 0.2 && mid.X < hole.MaxX - 0.2 && mid.Y > hole.MinY + 0.2 && mid.Y < hole.MaxY - 0.2;
                Assert.False(inHole, $"ponto atravessa o furo do O: {a} → {b}");
            }
    }

    [Fact]
    public void Stitches_FollowTextRotation()
    {
        var flat = Make("H");
        var turned = Make("H");
        turned.RotationDeg = 90;
        var bf = B(flat);
        var bt = B(turned);
        Assert.Equal(bf.Width, bt.Height, 2);
        var pts = turned.GenerateRuns(null).SelectMany(r => r).ToList();
        Assert.True(pts.All(p => bt.Inflate(0.01).Contains(p)));
    }

    [Fact]
    public void Design_BuildsPatternWithColorAndSurvivesJson()
    {
        var design = new EmbDesign();
        var text = Make("AB\nO");
        text.Color = BrotherPalette.Colors[13];
        text.Underlay = true;
        text.Origin = new Vec2(-20, -5);
        text.Alignment = TextAlign.Center;
        text.RotationDeg = 15;
        design.Objects.Add(text);

        var json = design.ToJson();
        Assert.Contains("\"tipo\": \"texto\"", json);
        var back = EmbDesign.FromJson(json);
        var t2 = Assert.IsType<TextObject>(Assert.Single(back.Objects));
        Assert.Equal("AB\nO", t2.Text);
        Assert.Equal(15, t2.RotationDeg, 6);
        Assert.Equal(design.BuildPattern().Stitches, back.BuildPattern().Stitches);
        Assert.Equal(BrotherPalette.Colors[13], back.BuildPattern().Threads.Single());
        Assert.True(design.BuildPattern().StitchCount > 300);
    }

    [Fact]
    public void TextPattern_SurvivesPesAndDstRoundTrip()
    {
        var design = new EmbDesign();
        design.Objects.Add(Make("ABO O", 12));
        var pattern = design.BuildPattern();

        foreach (IEmbroideryFormat format in new IEmbroideryFormat[] { new PesFormat(), new DstFormat() })
        {
            using var ms = new MemoryStream();
            format.Write(pattern, ms);
            ms.Position = 0;
            var back = format.Read(ms);
            Assert.Equal(pattern.StitchCount, back.StitchCount);
        }
    }

    [Fact]
    public void HitTest_InsideLetterAndOutside()
    {
        var t = Make("H");
        t.Origin = new Vec2(100, 100);
        var b = B(t);
        Assert.True(t.HitTest(b.Center, 0.5));
        Assert.False(t.HitTest(new Vec2(b.MaxX + 5, b.MaxY + 5), 0.5));
    }

    [Fact]
    public void Clone_KeepsEverythingButId()
    {
        var t = Make("AB");
        t.Origin = new Vec2(3, 4);
        t.RotationDeg = 30;
        var c = (TextObject)t.Clone();
        Assert.NotEqual(t.Id, c.Id);
        Assert.Equal(t.Origin, c.Origin);
        Assert.Equal(30, c.RotationDeg, 6);
    }
}
