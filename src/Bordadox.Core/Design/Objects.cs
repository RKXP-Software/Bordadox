using System.ComponentModel;
using System.Text.Json.Serialization;
using Bordadox.Core.Generators;
using Bordadox.Core.Geometry;

namespace Bordadox.Core.Design;

/// <summary>Ponto corrido ao longo de um caminho (aberto ou fechado).</summary>
public sealed class RunObject : EmbObject
{
    public List<PathNode> Nodes { get; set; } = new();

    [Category("Ponto corrido"), DisplayName("Comprimento do ponto (mm)"), Range(1, 7, 0.1)]
    public double StitchLength { get; set; } = 2.5;

    [Category("Ponto corrido"), DisplayName("Ponto triplo (reforçado)")]
    public bool Triple { get; set; }

    [Category("Ponto corrido"), DisplayName("Caminho fechado")]
    public bool Closed { get; set; }

    public override string TypeName => "Ponto corrido";
    public override bool ClosedPaths => Closed;
    public override IReadOnlyList<IReadOnlyList<PathNode>> EditablePaths => new[] { Nodes };

    public override void SetNode(int path, int index, PathNode node) => SetIn(Nodes, index, node);
    public override void InsertNode(int path, int index, PathNode node) => Nodes.Insert(Math.Clamp(index, 0, Nodes.Count), node);
    public override bool RemoveNode(int path, int index)
    {
        if (Nodes.Count <= 2 || index < 0 || index >= Nodes.Count) return false;
        Nodes.RemoveAt(index);
        return true;
    }

    public override List<List<Vec2>> GenerateRuns(Vec2? from)
    {
        if (Nodes.Count < 2) return new();
        var pts = RunGenerator.Run(Nodes, Closed, StitchLength);
        if (Triple) pts = RunGenerator.Triple(pts);
        return new() { pts };
    }

    public override List<List<Vec2>> Outlines() => new() { PathUtil.Flatten(Nodes, Closed) };

    public override void Transform(Func<Vec2, Vec2> f) => Nodes = PathUtil.Transformed(Nodes, f);
}

/// <summary>
/// Coluna satin entre dois trilhos. Os nós alternam entre o lado A e o lado B
/// (A0, B0, A1, B1, ...), como a ferramenta "Column A" de programas de digitalização.
/// </summary>
public sealed class SatinObject : EmbObject
{
    public List<PathNode> Nodes { get; set; } = new();

    [Category("Satin"), DisplayName("Densidade (mm entre pontos)"), Range(0.2, 2, 0.05)]
    public double Spacing { get; set; } = 0.4;

    [Category("Satin"), DisplayName("Compensação de puxada (mm)"), Range(0, 1, 0.05)]
    public double PullCompensation { get; set; } = 0.15;

    [Category("Satin"), DisplayName("Dividir pontos longos")]
    public bool AutoSplit { get; set; } = true;

    [Category("Satin"), DisplayName("Ponto máximo (mm)"), Range(3, 12, 0.5)]
    public double MaxStitch { get; set; } = 7;

    [Category("Reforço por baixo"), DisplayName("Corrido central")]
    public bool CenterUnderlay { get; set; } = true;

    [Category("Reforço por baixo"), DisplayName("Corrido nas bordas")]
    public bool EdgeUnderlay { get; set; }

    [Category("Reforço por baixo"), DisplayName("Zigue-zague")]
    public bool ZigzagUnderlay { get; set; }

    [Category("Reforço por baixo"), DisplayName("Espaçamento do zigue-zague (mm)"), Range(1, 5, 0.1)]
    public double ZigzagSpacing { get; set; } = 2;

    public override string TypeName => "Coluna satin";

    [JsonIgnore, Browsable(false)]
    public List<PathNode> RailA => Nodes.Where((_, i) => i % 2 == 0).Take(Nodes.Count / 2).ToList();

    [JsonIgnore, Browsable(false)]
    public List<PathNode> RailB => Nodes.Where((_, i) => i % 2 == 1).ToList();

    public override IReadOnlyList<IReadOnlyList<PathNode>> EditablePaths => new[] { Nodes };

    public override void SetNode(int path, int index, PathNode node) => SetIn(Nodes, index, node);

    public override void InsertNode(int path, int index, PathNode node)
    {
        // Mantém a alternância A/B: insere um par (o nó e uma cópia do lado oposto).
        int pair = Math.Clamp(index / 2, 0, Nodes.Count / 2);
        var other = pair < Nodes.Count / 2 ? Nodes[pair * 2 + 1] : Nodes[^1];
        Nodes.Insert(pair * 2, other with { P = other.P });
        Nodes.Insert(pair * 2, node);
    }

    public override bool RemoveNode(int path, int index)
    {
        // Remove o par inteiro (A e B) para manter a coluna consistente.
        if (Nodes.Count <= 4 || index < 0 || index >= Nodes.Count) return false;
        int pair = index / 2;
        Nodes.RemoveRange(pair * 2, Math.Min(2, Nodes.Count - pair * 2));
        return true;
    }

    public List<(Vec2 A, Vec2 B)> Pairs() => SatinGenerator.SamplePairs(RailA, RailB, Spacing / 2);

    public override List<List<Vec2>> GenerateRuns(Vec2? from)
    {
        var pairs = Pairs();
        if (pairs.Count < 2) return new();

        // Cada passada inverte o sentido; o satin (por cima) é sempre a última.
        var passes = new List<List<Vec2>>();
        if (CenterUnderlay) passes.Add(SatinGenerator.CenterRun(pairs, 2.5));
        if (EdgeUnderlay) passes.Add(SatinGenerator.EdgeRun(pairs, 0.4, 2.5));
        if (ZigzagUnderlay) passes.Add(SatinGenerator.ZigzagUnderlay(pairs, ZigzagSpacing, 0.4, Spacing / 2));
        var top = SatinGenerator.Satin(pairs, new SatinSettings
        {
            Spacing = Spacing,
            PullCompensation = PullCompensation,
            AutoSplit = AutoSplit,
            MaxStitch = MaxStitch,
        });
        passes.Add(top);

        // Alterna o sentido para que cada passada comece onde a anterior terminou.
        var result = new List<Vec2>();
        bool forward = true;
        foreach (var pass in passes)
        {
            bool isLoop = pass.Count > 2 && Vec2.Distance(pass[0], pass[^1]) < 0.01;
            var p = forward ? pass : Reverse(pass, pass == top, pairs.Count);
            result.AddRange(p);
            if (!isLoop) forward = !forward;
        }
        return new() { result };
    }

    // Inverter o satin precisa recriar o zigue-zague a partir do fim para manter a alternância A/B.
    private List<Vec2> Reverse(List<Vec2> pass, bool isTop, int pairCount)
    {
        if (!isTop) return Enumerable.Reverse(pass).ToList();
        var reversedPairs = Pairs();
        reversedPairs.Reverse();
        return SatinGenerator.Satin(reversedPairs, new SatinSettings
        {
            Spacing = Spacing,
            PullCompensation = PullCompensation,
            AutoSplit = AutoSplit,
            MaxStitch = MaxStitch,
        });
    }

    public override List<List<Vec2>> Outlines()
    {
        var a = RailA;
        var b = RailB;
        if (a.Count < 2) return new() { Nodes.Select(n => n.P).ToList() };
        var outline = PathUtil.Flatten(a, false);
        var rb = PathUtil.Flatten(b, false);
        rb.Reverse();
        outline.AddRange(rb);
        return new() { outline };
    }

    public override bool ClosedPaths => true;

    public override bool HitTest(Vec2 p, double tolerance) =>
        base.HitTest(p, tolerance) || GeometryUtil.PointInContours(p, Outlines());

    public override void Transform(Func<Vec2, Vec2> f) => Nodes = PathUtil.Transformed(Nodes, f);
}

public enum FillType
{
    [Description("Tatami")] Tatami,
    [Description("Satin")] Satin,
}

/// <summary>Pontos prontos (ex.: importados de PES/DST). Pode ser movido, girado e recolorido, mas não redigitalizado.</summary>
public sealed class StitchBlockObject : EmbObject
{
    /// <summary>Sequências contínuas de pontos (mm).</summary>
    public List<List<Vec2>> Runs { get; set; } = new();

    public override string TypeName => "Pontos importados";
    public override IReadOnlyList<IReadOnlyList<PathNode>> EditablePaths => Array.Empty<IReadOnlyList<PathNode>>();
    public override void SetNode(int path, int index, PathNode node) { }
    public override void InsertNode(int path, int index, PathNode node) { }
    public override bool RemoveNode(int path, int index) => false;

    public override List<List<Vec2>> GenerateRuns(Vec2? from) => Runs.Select(r => r.ToList()).ToList();
    public override List<List<Vec2>> Outlines() => Runs;

    public override void Transform(Func<Vec2, Vec2> f) =>
        Runs = Runs.Select(r => r.Select(f).ToList()).ToList();
}
