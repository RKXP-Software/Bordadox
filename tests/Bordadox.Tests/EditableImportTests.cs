using System.ComponentModel;
using System.Reflection;
using Bordadox.Core.Design;
using Bordadox.Core.Formats;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;

namespace Bordadox.Tests;

/// <summary>Pontos importados (DST, PES...) e qualquer outro objeto têm tamanho, posição e opções editáveis no painel de propriedades.</summary>
public class EditableImportTests
{
    private static StitchBlockObject Block() => new()
    {
        Runs = new()
        {
            new() { new(0, 0), new(10, 0), new(10, 20), new(0, 20), new(0, 0) },
            new() { new(30, 5), new(40, 5) },
        },
        Color = BrotherPalette.Colors[5],
    };

    private static List<string> PanelProperties(EmbObject o) =>
        o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.CanRead && p.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false)
            .Where(p => p.PropertyType == typeof(double) || p.PropertyType == typeof(bool) || p.PropertyType == typeof(string))
            .Select(p => p.Name).ToList();

    [Fact]
    public void ImportedStitches_ShowSizePositionAndStitchOptionsInThePanel()
    {
        var names = PanelProperties(Block());
        Assert.Contains("BoxWidthMm", names);
        Assert.Contains("BoxHeightMm", names);
        Assert.Contains("BoxCenterX", names);
        Assert.Contains("BoxCenterY", names);
        Assert.Contains("MaxStitchMm", names);
        Assert.Contains("MinStitchMm", names);
        Assert.Contains("Name", names);
    }

    [Fact]
    public void ADstOpenedFromAFile_ComesAsEditableObjects()
    {
        var p = new StitchPattern();
        p.Threads.Add(BrotherPalette.Colors[5]);
        p.Add(0, 0, StitchCommand.Jump);
        for (int i = 1; i <= 50; i++) p.Add(i * 20, (i % 2) * 100, StitchCommand.Stitch);
        p.Add(1000, 100, StitchCommand.End);
        using var ms = new MemoryStream();
        new DstFormat().Write(p, ms);
        ms.Position = 0;
        var design = EmbDesign.FromPattern(new DstFormat().Read(ms).Centered());
        var obj = Assert.IsType<StitchBlockObject>(Assert.Single(design.Objects));
        Assert.InRange(obj.BoxWidthMm, 90, 110);
        obj.BoxWidthMm = 50;
        Assert.Equal(50, obj.BoxWidthMm, 3);
        Assert.Equal(obj.BoxHeightMm, design.Bounds().Height, 6);
    }

    [Fact]
    public void Width_ScalesAroundTheCenter_AndKeepsTheProportion()
    {
        var o = Block();
        var before = o.Bounds();
        double ratio = before.Width / before.Height;
        o.BoxWidthMm = 80;
        var after = o.Bounds();
        Assert.Equal(80, after.Width, 6);
        Assert.Equal(80 / ratio, after.Height, 6);
        Assert.Equal(before.Center.X, after.Center.X, 6);
        Assert.Equal(before.Center.Y, after.Center.Y, 6);
    }

    [Fact]
    public void WithoutKeepAspect_WidthAndHeightAreIndependent()
    {
        var o = Block();
        o.KeepAspect = false;
        double h = o.BoxHeightMm;
        o.BoxWidthMm = 100;
        Assert.Equal(100, o.BoxWidthMm, 6);
        Assert.Equal(h, o.BoxHeightMm, 6);
        o.BoxHeightMm = 5;
        Assert.Equal(5, o.BoxHeightMm, 6);
        Assert.Equal(100, o.BoxWidthMm, 6);
    }

    [Fact]
    public void Height_AndCenter_Work_AndZeroDoesNotCollapseTheObject()
    {
        var o = Block();
        o.BoxHeightMm = 40;
        Assert.Equal(40, o.BoxHeightMm, 6);
        o.BoxCenterX = 100; o.BoxCenterY = -50;
        Assert.Equal(100, o.BoxCenterX, 6);
        Assert.Equal(-50, o.BoxCenterY, 6);
        o.BoxWidthMm = 0;                      // o usuário pode digitar 0: vira o menor tamanho, não um ponto só
        Assert.True(o.BoxWidthMm >= 0.09 && o.Bounds().Width > 0);
    }

    [Fact]
    public void SizeAndPositionApplyToEveryKindOfObject()
    {
        EmbObject[] all =
        {
            new RunObject { Nodes = new() { new(new Vec2(0, 0)), new(new Vec2(20, 10)) } },
            new FillObject { Outline = new() { new(new Vec2(0, 0)), new(new Vec2(20, 0)), new(new Vec2(20, 10)), new(new Vec2(0, 10)) } },
            new AppliqueObject { Outline = new() { new(new Vec2(0, 0)), new(new Vec2(20, 0)), new(new Vec2(20, 10)), new(new Vec2(0, 10)) } },
            Block(),
        };
        foreach (var o in all)
        {
            o.BoxWidthMm = 60;
            Assert.Equal(60, o.BoxWidthMm, 4);
            o.BoxCenterX = 7;
            Assert.Equal(7, o.BoxCenterX, 4);
        }
    }

    [Fact]
    public void SizeAndPositionAreNotSavedInTheProject_SoLoadingNeverMovesAnything()
    {
        var d = new EmbDesign();
        d.Objects.Add(Block());
        var json = d.ToJson();
        Assert.DoesNotContain("BoxWidthMm", json);
        Assert.DoesNotContain("BoxCenterX", json);
        Assert.DoesNotContain("KeepAspect", json);
        var back = EmbDesign.FromJson(json);
        Assert.Equal(d.Objects[0].Bounds(), back.Objects[0].Bounds());
    }

    [Fact]
    public void MaxStitch_SplitsLongStitches_WithoutChangingTheOriginalPoints()
    {
        var o = new StitchBlockObject { Runs = new() { new() { new(0, 0), new(30, 0), new(30, 1) } } };
        Assert.Equal(3, o.GenerateRuns(null)[0].Count);
        o.MaxStitchMm = 5;
        var run = o.GenerateRuns(null)[0];
        Assert.Equal(8, run.Count);                                   // 30 mm em 6 partes de 5 mm, mais o ponto curto final
        Assert.All(run.Zip(run.Skip(1)), s => Assert.True(Vec2.Distance(s.First, s.Second) <= 5.001));
        Assert.Equal(3, o.Runs[0].Count);                             // o original continua igual
    }

    [Fact]
    public void MinStitch_DropsVeryShortStitches_ButKeepsTheEnds()
    {
        var o = new StitchBlockObject { Runs = new() { new() { new(0, 0), new(0.1, 0), new(0.2, 0), new(5, 0), new(5.05, 0) } } };
        o.MinStitchMm = 0.5;
        var run = o.GenerateRuns(null)[0];
        Assert.Equal(new Vec2(0, 0), run[0]);
        Assert.Equal(new Vec2(5.05, 0), run[^1]);
        Assert.Equal(3, run.Count);
    }
}

public class EmbFormatTests
{
    [Fact]
    public void ANonCompoundFile_IsRejectedWithAClearMessage()
    {
        var ex = Assert.Throws<InvalidDataException>(() => new EmbFormat().Read(new MemoryStream(new byte[2000])));
        Assert.Contains("Embird", ex.Message);
    }

    [Fact]
    public void CompoundFileReader_RejectsGarbage()
    {
        Assert.False(CompoundFile.IsCompoundFile(new byte[600]));
        Assert.Throws<InvalidDataException>(() => new CompoundFile(new byte[600]));
    }

    [Fact]
    public void AWilcomEmb_IsRecognized_WithItsPreview_AndAHelpfulMessage()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "amostras"))) dir = dir.Parent;
        var file = dir == null ? null : Directory.EnumerateFiles(Path.Combine(dir.FullName, "amostras"), "*.emb").FirstOrDefault();
        if (file == null) return;     // sem .emb de exemplo nesta máquina (a pasta é só local)

        using var fs = File.OpenRead(file);
        var ex = Assert.Throws<WilcomEmbException>(() => new EmbFormat().Read(fs));
        Assert.Contains("DST", ex.Message);
        Assert.Contains("DesignDocument", ex.Streams);
        Assert.True(ex.DesignDataBytes > 1000);
        Assert.NotNull(ex.PreviewBmp);
        Assert.Equal((byte)'B', ex.PreviewBmp![0]);
        Assert.Equal(100, BitConverter.ToInt32(ex.PreviewBmp, 18));     // a miniatura tem 100 x 100
    }
}
