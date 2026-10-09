using Bordadox.Core.Design;
using Bordadox.Core.Geometry;
using Bordadox.Core.Machine;
using Bordadox.Core.Stitches;

namespace Bordadox.Tests;

public class HoopRotationTests
{
    // Faixa larga 200 × 20 mm: só cabe no bastidor grande deitado (260 × 160).
    private static EmbDesign WideDesign(bool rotated, bool rotateOnExport = true)
    {
        var d = new EmbDesign { HoopName = Hoops.Default.Name, HoopRotated = rotated };
        d.Settings.RotateOnExport = rotateOnExport;
        d.Objects.Add(new FillObject
        {
            Outline = { new(new Vec2(-100, -10)), new(new Vec2(100, -10)), new(new Vec2(100, 10)), new(new Vec2(-100, 10)) },
        });
        return d;
    }

    private static (double W, double H) SizeMm(StitchPattern p)
    {
        var b = p.Bounds();
        return ((b.MaxX - b.MinX) / 10.0, (b.MaxY - b.MinY) / 10.0);
    }

    [Fact]
    public void RotatedHoop_SwapsDimensions()
    {
        var d = WideDesign(rotated: true);
        Assert.Equal(260, d.Hoop.WidthMm);
        Assert.Equal(160, d.Hoop.HeightMm);
        Assert.Equal(160, d.BaseHoop.WidthMm);
    }

    [Fact]
    public void MachinePattern_IsRotatedToPortrait_WhenHoopIsRotated()
    {
        var (w, h) = SizeMm(WideDesign(rotated: true).BuildMachinePattern("t"));
        Assert.True(h > 190 && w < 30, $"esperado em pé, veio {w} × {h}");
        Assert.True(Hoops.Default.Fits(w, h));
    }

    [Fact]
    public void MachinePattern_KeepsOrientation_WhenOptionIsOff()
    {
        var (w, h) = SizeMm(WideDesign(rotated: true, rotateOnExport: false).BuildMachinePattern("t"));
        Assert.True(w > 190 && h < 30);
    }

    [Fact]
    public void Rotated90Clockwise_MapsRightToDown()
    {
        var p = new StitchPattern();
        p.Add(100, 0, StitchCommand.Stitch);
        var r = p.Rotated90Clockwise();
        Assert.Equal(new Stitch(0, 100, StitchCommand.Stitch), r.Stitches[0]);
    }

    [Fact]
    public void FromPattern_WideDesign_PicksRotatedHoop()
    {
        var wide = WideDesign(rotated: false).BuildPattern();
        var design = EmbDesign.FromPattern(wide);
        Assert.True(design.HoopRotated);
        Assert.Equal(Hoops.Default.Name, design.HoopName);
    }

    [Fact]
    public void HoopRotation_IsSavedInProject()
    {
        var back = EmbDesign.FromJson(WideDesign(rotated: true).ToJson());
        Assert.True(back.HoopRotated);
    }

    [Theory]
    [InlineData(170, 120, "Médio 130 × 180 mm", true)]
    [InlineData(120, 170, "Médio 130 × 180 mm", false)]
    [InlineData(250, 150, "Grande 160 × 260 mm", true)]
    public void SmallestFittingAnyOrientation(double w, double h, string hoop, bool rotated)
    {
        var fit = Hoops.SmallestFittingAnyOrientation(w, h);
        Assert.Equal(hoop, fit?.Hoop.Name);
        Assert.Equal(rotated, fit?.Rotated);
    }
}
