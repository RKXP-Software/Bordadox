using System.Windows.Input;
using Bordadox.App.Editor;
using Bordadox.Core.Design;
using Bordadox.Core.Geometry;

namespace Bordadox.App.Tests;

/// <summary>Arraste com o mouse: o recálculo é adiado durante o movimento, mas o resultado final e o desfazer precisam estar certos.</summary>
public class DragTests
{
    private const double Px = 0.1;

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
        e.Select(Array.Empty<EmbObject>());
        return f;
    }

    [Fact]
    public void ArrastarMuitasVezes_TerminaComOPontoCertoAoSoltar()
    {
        var e = NewEditor();
        var sq = AddSquare(e, 0, 0, 10);
        var start = new Vec2(5, 5);

        e.MouseDown(start, MouseButton.Left, 1, ModifierKeys.None, Px);
        for (int i = 1; i <= 60; i++)
            e.MouseMove(start + new Vec2(i * 0.5, 0), true, ModifierKeys.None);
        e.MouseUp(start + new Vec2(30, 0), ModifierKeys.None);

        Assert.Equal(30, sq.Bounds().MinX, 6);
        // O desenho exibido precisa refletir a posição final, não um passo intermediário.
        // O padrão guarda décimos de milímetro; os pontos de arremate podem passar um pouco da borda.
        var atual = e.Pattern.Bounds();
        Assert.InRange(atual.MinX / 10.0, 29.0, 31.0);
    }

    [Fact]
    public void ArrastarEDesfazer_VoltaAPosicaoOriginal()
    {
        var e = NewEditor();
        var sq = AddSquare(e, 0, 0, 10);
        var start = new Vec2(5, 5);

        e.MouseDown(start, MouseButton.Left, 1, ModifierKeys.None, Px);
        for (int i = 1; i <= 20; i++)
            e.MouseMove(start + new Vec2(i * 0.5, 0), true, ModifierKeys.None);
        e.MouseUp(start + new Vec2(10, 0), ModifierKeys.None);
        Assert.Equal(10, sq.Bounds().MinX, 6);

        e.Undo();

        var volta = Assert.Single(e.Design.Objects);
        Assert.Equal(0, volta.Bounds().MinX, 6);
    }

    [Fact]
    public void Clicar_SemMover_NaoCriaPassoDeDesfazer()
    {
        var e = NewEditor();
        AddSquare(e, 0, 0, 10);
        bool antes = e.CanUndo;
        var start = new Vec2(5, 5);

        e.MouseDown(start, MouseButton.Left, 1, ModifierKeys.None, Px);
        e.MouseUp(start, ModifierKeys.None);

        Assert.Equal(antes, e.CanUndo);
    }
}
