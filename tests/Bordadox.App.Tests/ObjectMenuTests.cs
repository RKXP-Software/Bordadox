using Bordadox.App.Editor;
using Bordadox.Core.Design;
using Bordadox.Core.Geometry;

namespace Bordadox.App.Tests;

/// <summary>Itens do menu Objeto e as setas do teclado que ainda não tinham teste direto.</summary>
public class ObjectMenuTests
{
    private static EditorController NewEditor()
    {
        var e = new EditorController();
        e.LoadDesign(new EmbDesign(), null);
        return e;
    }

    private static FillObject AddSquare(EditorController e, double x, double y, double size)
    {
        var f = new FillObject
        {
            Outline = new List<PathNode>
            {
                new(new Vec2(x, y), false),
                new(new Vec2(x + size, y), false),
                new(new Vec2(x + size, y + size), false),
                new(new Vec2(x, y + size), false),
            },
        };
        e.AddObjects(new EmbObject[] { f });
        return f;
    }

    [Fact]
    public void Redimensionar_MantemOCentro()
    {
        var e = NewEditor();
        var sq = AddSquare(e, 0, 0, 10);
        e.Select(new EmbObject[] { sq });

        e.Scale(2);

        Assert.Equal(20, sq.Bounds().Width, 6);
        Assert.Equal(5, sq.Bounds().Center.X, 6);
        Assert.Equal(5, sq.Bounds().Center.Y, 6);
    }

    [Fact]
    public void Redimensionar_PodeSerDesfeito()
    {
        var e = NewEditor();
        var sq = AddSquare(e, 0, 0, 10);
        e.Select(new EmbObject[] { sq });
        e.Scale(3);

        e.Undo();

        Assert.Equal(10, e.Design.Objects[0].Bounds().Width, 6);
    }

    [Fact]
    public void CentralizarNoBastidor_LevaASelecaoParaAOrigem()
    {
        var e = NewEditor();
        var sq = AddSquare(e, 30, 40, 10);
        e.Select(new EmbObject[] { sq });

        e.CenterInHoop();

        Assert.Equal(0, sq.Bounds().Center.X, 6);
        Assert.Equal(0, sq.Bounds().Center.Y, 6);
    }

    [Fact]
    public void CentralizarNoBastidor_PodeSerDesfeito()
    {
        var e = NewEditor();
        var sq = AddSquare(e, 30, 40, 10);
        e.Select(new EmbObject[] { sq });
        e.CenterInHoop();

        e.Undo();

        Assert.Equal(35, e.Design.Objects[0].Bounds().Center.X, 6);
        Assert.Equal(45, e.Design.Objects[0].Bounds().Center.Y, 6);
    }

    [Fact]
    public void Setas_MoveTudoDaSelecao()
    {
        var e = NewEditor();
        var sq = AddSquare(e, 0, 0, 10);
        e.Select(new EmbObject[] { sq });

        e.Nudge(0.5, -5);

        Assert.Equal(0.5, sq.Bounds().MinX, 6);
        Assert.Equal(-5, sq.Bounds().MinY, 6);
    }

    [Fact]
    public void Setas_SemSelecao_NaoFazNada()
    {
        var e = NewEditor();
        var sq = AddSquare(e, 0, 0, 10);
        e.Select(Array.Empty<EmbObject>());  // AddObjects deixa o objeto novo selecionado

        e.Nudge(5, 5);

        Assert.Equal(0, sq.Bounds().MinX, 6);
        Assert.Equal(0, sq.Bounds().MinY, 6);
    }
}
