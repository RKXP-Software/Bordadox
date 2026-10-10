using Bordadox.App.Editor;
using Bordadox.Core.Design;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;

namespace Bordadox.App.Tests;

/// <summary>Conversão de ponto corrido com "Caminho fechado" em aplique ou preenchimento.</summary>
public class ConvertRunTests
{
    private static EditorController NewEditor()
    {
        var e = new EditorController();
        e.LoadDesign(new EmbDesign(), null);
        return e;
    }

    private static RunObject ClosedSquareRun(bool closed = true) => new()
    {
        Closed = closed,
        Name = "Contorno",
        Color = BrotherPalette.Colors[2],
        Nodes = new List<PathNode>
        {
            new(new Vec2(0, 0), false),
            new(new Vec2(20, 0), false),
            new(new Vec2(20, 20), false),
            new(new Vec2(0, 20), false),
        },
    };

    [Fact]
    public void PontoCorridoFechado_ViraAplique_ComOCaminhoComoContorno()
    {
        var e = NewEditor();
        var run = ClosedSquareRun();
        e.AddObjects(new EmbObject[] { run });

        e.Select(new EmbObject[] { run });
        int n = e.ConvertShapes(toApplique: true);

        Assert.Equal(1, n);
        var aplique = Assert.IsType<AppliqueObject>(e.Design.Objects[0]);
        Assert.Equal(4, aplique.Outline.Count);
        Assert.Equal("Contorno", aplique.Name);
        Assert.Equal(run.Color, aplique.Color);
        Assert.True(e.Pattern.StitchCount > 0, "o aplique convertido deveria gerar pontos");
    }

    [Fact]
    public void PontoCorridoFechado_ViraPreenchimento()
    {
        var e = NewEditor();
        var run = ClosedSquareRun();
        e.AddObjects(new EmbObject[] { run });
        e.Select(new EmbObject[] { run });

        Assert.Equal(1, e.ConvertShapes(toApplique: false));
        Assert.IsType<FillObject>(e.Design.Objects[0]);
    }

    [Fact]
    public void PontoCorridoAberto_NaoConverte()
    {
        var e = NewEditor();
        var run = ClosedSquareRun(closed: false);
        e.AddObjects(new EmbObject[] { run });
        e.Select(new EmbObject[] { run });

        Assert.Equal(0, e.ConvertShapes(toApplique: true));
        Assert.Equal(0, e.ConvertShapes(toApplique: false));
        Assert.Same(run, e.Design.Objects[0]);
    }

    [Fact]
    public void PontoCorridoFechado_ConvertidoEDesfeitoVoltaAoOriginal()
    {
        var e = NewEditor();
        var run = ClosedSquareRun();
        e.AddObjects(new EmbObject[] { run });
        e.Select(new EmbObject[] { run });
        e.ConvertShapes(toApplique: true);

        e.Undo();

        // O desfazer restaura o projeto a partir de um snapshot, então o objeto volta como cópia: confere o tipo e o nome.
        var volta = Assert.IsType<RunObject>(e.Design.Objects[0]);
        Assert.True(volta.Closed);
        Assert.Equal("Contorno", volta.Name);
    }
}
