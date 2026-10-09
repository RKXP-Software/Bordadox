using System.Windows.Input;
using Bordadox.App.Editor;
using Bordadox.Core.Design;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;

namespace Bordadox.App.Tests;

/// <summary>Aplique e efeitos de preenchimento pelo editor (cliques em mm, sem abrir janela).</summary>
public class AppliqueEditorTests
{
    private const double Px = 0.1;

    private static EditorController NewEditor()
    {
        var e = new EditorController();
        e.LoadDesign(new EmbDesign(), null);
        return e;
    }

    private static void Click(EditorController e, double x, double y)
    {
        e.MouseDown(new Vec2(x, y), MouseButton.Left, 1, ModifierKeys.None, Px);
        e.MouseUp(new Vec2(x, y), ModifierKeys.None);
    }

    private static T Draw<T>(EditorController e, EditorTool tool, double size = 24) where T : EmbObject
    {
        e.Tool = tool;
        Click(e, 0, 0);
        Click(e, size, 0);
        Click(e, size, size);
        Click(e, 0, size);
        e.KeyDown(Key.Enter);
        return (T)e.Design.Objects[^1];
    }

    [Fact]
    public void AppliqueTool_DrawsAnApplique_WithThreeThreadEntriesAndTwoStops()
    {
        var e = NewEditor();
        e.CurrentColor = BrotherPalette.Colors[5];
        var a = Draw<AppliqueObject>(e, EditorTool.Applique);

        Assert.Equal(BrotherPalette.Colors[5], a.Color);
        Assert.Equal(4, a.Outline.Count);
        Assert.Same(a, Assert.Single(e.Selection));
        Assert.Equal(3, e.Pattern.Threads.Count);
        Assert.Equal(2, e.Pattern.Stitches.Count(s => s.Command == StitchCommand.ColorChange));
        Assert.True(e.StitchCountOf(a) > 400);
    }

    [Fact]
    public void ConvertingAFill_KeepsOutlineHolesColorNameAndOrder_AndCanBeUndone()
    {
        var e = NewEditor();
        var first = Draw<FillObject>(e, EditorTool.Fill, 10);
        var fill = Draw<FillObject>(e, EditorTool.Fill, 30);
        fill.Color = BrotherPalette.Colors[13];
        fill.Name = "Escudo";
        e.Tool = EditorTool.Hole;
        Click(e, 8, 8); Click(e, 20, 8); Click(e, 20, 20); Click(e, 8, 20);
        e.KeyDown(Key.Enter);
        Assert.Single(fill.Holes);

        e.Tool = EditorTool.Select;
        e.Select(new[] { fill });
        Assert.Equal(1, e.ConvertShapes(toApplique: true));

        Assert.Equal(2, e.Design.Objects.Count);
        Assert.Same(first, e.Design.Objects[0]);
        var a = Assert.IsType<AppliqueObject>(e.Design.Objects[1]);
        Assert.Equal("Escudo", a.Name);
        Assert.Equal(BrotherPalette.Colors[13], a.Color);
        Assert.Equal(4, a.Outline.Count);
        Assert.Single(a.Holes);
        Assert.Same(a, Assert.Single(e.Selection));

        e.Undo();
        Assert.IsType<FillObject>(e.Design.Objects[1]);

        // Nada selecionado do tipo certo: nada é convertido.
        e.Select(new[] { e.Design.Objects[1] });
        Assert.Equal(0, e.ConvertShapes(toApplique: false));
    }

    [Fact]
    public void ConvertingBack_GivesAPlainFill()
    {
        var e = NewEditor();
        var a = Draw<AppliqueObject>(e, EditorTool.Applique);
        e.Select(new[] { a });
        Assert.Equal(1, e.ConvertShapes(toApplique: false));
        Assert.IsType<FillObject>(e.Design.Objects[0]);
        Assert.Single(e.Pattern.Threads);
    }

    [Fact]
    public void HoleTool_WorksOnAnApplique()
    {
        var e = NewEditor();
        var a = Draw<AppliqueObject>(e, EditorTool.Applique, 30);
        e.Tool = EditorTool.Hole;
        Click(e, 10, 10); Click(e, 20, 10); Click(e, 20, 20); Click(e, 10, 20);
        e.KeyDown(Key.Enter);
        Assert.Single(a.Holes);
    }

    [Fact]
    public void GradientFill_ShowsAsTwoThreadsInThePattern()
    {
        var e = NewEditor();
        var fill = Draw<FillObject>(e, EditorTool.Fill);
        e.Change(() => { fill.Effect = FillEffect.Gradient; fill.Color2 = BrotherPalette.Colors[2]; });
        Assert.Equal(2, e.Pattern.Threads.Count);
        e.Undo();
        Assert.Single(e.Pattern.Threads);
    }

    [Fact]
    public void MovingAnApplique_MovesItsStitches()
    {
        var e = NewEditor();
        var a = Draw<AppliqueObject>(e, EditorTool.Applique);
        var before = a.Bounds();
        e.Nudge(10, 5);
        var after = a.Bounds();
        Assert.Equal(before.MinX + 10, after.MinX, 6);
        Assert.Equal(before.MinY + 5, after.MinY, 6);
    }
}

public class ExplodeEditorTests
{
    private const double Px = 0.1;

    [Fact]
    public void ExplodeSelection_SplitsTheApplique_KeepsOrder_AndOneUndoRestoresIt()
    {
        var e = new EditorController();
        e.LoadDesign(new EmbDesign(), null);
        e.Tool = EditorTool.Fill;
        foreach (var (x, y) in new[] { (0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (0.0, 10.0) })
        {
            e.MouseDown(new Vec2(x, y), MouseButton.Left, 1, ModifierKeys.None, Px);
            e.MouseUp(new Vec2(x, y), ModifierKeys.None);
        }
        e.KeyDown(Key.Enter);
        var fill = e.Design.Objects[0];
        e.Select(new[] { fill });
        Assert.Equal(1, e.ConvertShapes(toApplique: true));
        var applique = e.Design.Objects[0];

        e.Select(new[] { applique });
        Assert.Equal(3, e.Explode());
        Assert.Equal(3, e.Design.Objects.Count);
        Assert.All(e.Design.Objects, o => Assert.IsType<StitchBlockObject>(o));
        Assert.Equal(3, e.Selection.Count);
        Assert.Equal(2, e.Pattern.Stitches.Count(s => s.Command == StitchCommand.ColorChange));

        e.Undo();
        Assert.IsType<AppliqueObject>(Assert.Single(e.Design.Objects));
    }

    [Fact]
    public void ExplodeSelection_OnASimpleObject_DoesNothing()
    {
        var e = new EditorController();
        e.LoadDesign(new EmbDesign(), null);
        e.AddObjects(new EmbObject[] { new RunObject { Nodes = new() { new(new Vec2(0, 0)), new(new Vec2(10, 0)) } } });
        Assert.Equal(0, e.Explode());
        Assert.Single(e.Design.Objects);
    }
}

public class MenuTests
{
    private static string Xaml()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "src", "Bordadox.App", "MainWindow.xaml"))) dir = dir.Parent;
        return dir == null ? "" : System.IO.File.ReadAllText(System.IO.Path.Combine(dir.FullName, "src", "Bordadox.App", "MainWindow.xaml"));
    }

    [Theory]
    [InlineData("Explode_Click")]
    [InlineData("ToApplique_Click")]
    [InlineData("ToFill_Click")]
    [InlineData("ProductionSheet_Click")]
    public void EveryCommandHandler_IsReachableFromTheMenu(string handler)
    {
        var xaml = Xaml();
        if (xaml.Length == 0) return;      // fora do repositório (sem as fontes)
        Assert.Contains($"Click=\"{handler}\"", xaml);
    }
}
