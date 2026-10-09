using System.Diagnostics;
using System.Reflection;
using Bordadox.Core.Design;
using Bordadox.Core.Generators;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;
using Bordadox.Core.Text;

namespace Bordadox.Tests;

/// <summary>Os campos em milímetros aceitam de 0 a 100: nenhum valor nesse intervalo pode travar nem quebrar a geração de pontos.</summary>
public class ExtremeValuesTests
{
    public ExtremeValuesTests() => GlyphService.Provider = new FakeGlyphProvider();

    private static List<PathNode> Square(double s) =>
        new() { new(new Vec2(0, 0)), new(new Vec2(s, 0)), new(new Vec2(s, s)), new(new Vec2(0, s)) };

    private static IEnumerable<EmbObject> Objects()
    {
        yield return new RunObject { Nodes = new() { new(new Vec2(0, 0)), new(new Vec2(40, 10)), new(new Vec2(60, 0)) } };
        yield return new SatinObject { Nodes = new() { new(new Vec2(0, 0)), new(new Vec2(0, 6)), new(new Vec2(20, 0)), new(new Vec2(20, 6)), new(new Vec2(40, 2)), new(new Vec2(40, 8)) } };
        yield return new FillObject { Outline = Square(30), Holes = { Square(30).Select(n => new PathNode(new Vec2(n.P.X * 0.3 + 10, n.P.Y * 0.3 + 10))).ToList() } };
        yield return new FillObject { Outline = Square(30), FillType = FillType.Satin };
        yield return new FillObject { Outline = Square(30), Effect = FillEffect.Gradient };
        yield return new FillObject { Outline = Square(30), Effect = FillEffect.Contour };
        yield return new FillObject { Outline = Square(30), Effect = FillEffect.Pattern, Pattern = FillPattern.Diamond };
        yield return new AppliqueObject { Outline = Square(30) };
        yield return new TextObject { Text = "HOLA O", HeightMm = 10, FillType = FillType.Satin };
        yield return new TextObject { Text = "HOLA O", HeightMm = 10, FillType = FillType.Tatami };
    }

    private static IEnumerable<PropertyInfo> MmProperties(EmbObject o) =>
        o.GetType().GetProperties().Where(p => p.PropertyType == typeof(double) && p.CanWrite && p.GetCustomAttribute<RangeAttribute>() != null
                                               && (p.GetCustomAttribute<System.ComponentModel.DisplayNameAttribute>()?.DisplayName.Contains("mm") ?? false));

    public static IEnumerable<object[]> Cases()
    {
        int i = 0;
        foreach (var o in Objects())
        {
            foreach (var p in MmProperties(o)) yield return new object[] { i, p.Name };
            yield return new object[] { i, "*" };
            i++;
        }
    }

    private static void Check(EmbObject o, string what)
    {
        var sw = Stopwatch.StartNew();
        var runs = o.GenerateRuns(null);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(25), $"{what}: demorou {sw.Elapsed.TotalSeconds:0.0}s");
        Assert.All(runs.SelectMany(r => r), p => Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y), $"{what}: ponto inválido"));
        Assert.True(runs.Sum(r => r.Count) < 3_000_000, $"{what}: pontos demais");

        // E o desenho todo continua montando uma sequência de máquina válida.
        var d = new EmbDesign();
        d.Objects.Add(o);
        var pattern = d.BuildPattern("t");
        Assert.Equal(StitchCommand.End, pattern.Stitches[^1].Command);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ZeroAndHundredMillimetres_NeverHangOrBreak(int index, string property)
    {
        foreach (var value in new[] { 0.0, 100.0 })
        {
            var o = Objects().ElementAt(index);
            var props = property == "*" ? MmProperties(o).ToList() : new List<PropertyInfo> { o.GetType().GetProperty(property)! };
            foreach (var p in props)
            {
                double v = p.Name == "LetterSpacing" && value == 0 ? 0 : value;
                p.SetValue(o, v);
            }
            Check(o, $"{o.TypeName} {property}={value}");
        }
    }

    [Fact]
    public void TextWithZeroHeight_StillShowsTheLettersAtTheSmallestSize()
    {
        var t = new TextObject { Text = "HO", HeightMm = 0, FillType = FillType.Satin };
        Assert.NotEmpty(t.GenerateRuns(null));
        Assert.True(t.Bounds().Height >= 0.4);
    }

    [Fact]
    public void ZeroDensityOrZeroStitchLength_FallBackToTheSmallestUsefulValue()
    {
        var f = new FillObject { Outline = Square(20), Spacing = 0, StitchLength = 0, Underlay = false };
        var zero = f.GenerateRuns(null).Sum(r => r.Count);
        var min = new FillObject { Outline = Square(20), Spacing = FillGenerator.MinSpacing, StitchLength = FillGenerator.MinStitchLength, Underlay = false }
            .GenerateRuns(null).Sum(r => r.Count);
        Assert.Equal(min, zero);
    }
}
