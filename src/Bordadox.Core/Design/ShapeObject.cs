using Bordadox.Core.Geometry;

namespace Bordadox.Core.Design;

/// <summary>Objeto de forma fechada (contorno externo e furos): a base do preenchimento e do aplique.</summary>
public abstract class ShapeObject : EmbObject
{
    public List<PathNode> Outline { get; set; } = new();
    public List<List<PathNode>> Holes { get; set; } = new();

    public override bool ClosedPaths => true;

    public override IReadOnlyList<IReadOnlyList<PathNode>> EditablePaths =>
        new IReadOnlyList<PathNode>[] { Outline }.Concat(Holes).ToList();

    private List<PathNode> PathAt(int path) => path == 0 ? Outline : Holes[path - 1];

    public override void SetNode(int path, int index, PathNode node) => SetIn(PathAt(path), index, node);

    public override void InsertNode(int path, int index, PathNode node)
    {
        var list = PathAt(path);
        list.Insert(Math.Clamp(index, 0, list.Count), node);
    }

    public override bool RemoveNode(int path, int index)
    {
        var list = PathAt(path);
        if (index < 0 || index >= list.Count) return false;
        if (list.Count <= 3)
        {
            if (path == 0) return false;
            Holes.RemoveAt(path - 1); // furo com menos de 3 nós deixa de existir
            return true;
        }
        list.RemoveAt(index);
        return true;
    }

    /// <summary>Contornos achatados: o externo primeiro, depois os furos.</summary>
    public List<IReadOnlyList<Vec2>> Contours() =>
        new IReadOnlyList<Vec2>[] { PathUtil.Flatten(Outline, true) }
            .Concat(Holes.Where(h => h.Count >= 3).Select(h => (IReadOnlyList<Vec2>)PathUtil.Flatten(h, true)))
            .ToList();

    public override List<List<Vec2>> Outlines() => Contours().Select(c => c.ToList()).ToList();

    public override bool HitTest(Vec2 p, double tolerance) =>
        base.HitTest(p, tolerance) || GeometryUtil.PointInContours(p, Contours());

    public override void Transform(Func<Vec2, Vec2> f)
    {
        Outline = PathUtil.Transformed(Outline, f);
        Holes = Holes.Select(h => PathUtil.Transformed(h, f)).ToList();
    }
}
