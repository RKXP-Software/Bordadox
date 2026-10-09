using Bordadox.Core.Geometry;

namespace Bordadox.Core.Stitches;

public enum StitchCommand
{
    Stitch,
    Jump,
    Trim,
    ColorChange,
    Stop,
    End,
}

/// <summary>Um registro de máquina. Coordenadas absolutas em décimos de milímetro (unidade nativa de PES/DST).</summary>
public readonly record struct Stitch(int X, int Y, StitchCommand Command);

/// <summary>Sequência de pontos pronta para a máquina, com a lista de linhas (uma por bloco de cor).</summary>
public sealed class StitchPattern
{
    public List<Stitch> Stitches { get; } = new();
    public List<ThreadColor> Threads { get; } = new();
    public string Name { get; set; } = "Bordado";

    public void Add(int x, int y, StitchCommand cmd) => Stitches.Add(new Stitch(x, y, cmd));

    public int StitchCount => Stitches.Count(s => s.Command == StitchCommand.Stitch);
    public int TrimCount => Stitches.Count(s => s.Command == StitchCommand.Trim);
    public int ColorCount => Math.Max(Threads.Count, 1);

    /// <summary>Limites em 0,1 mm, considerando só os pontos costurados (saltos não deixam linha no tecido).</summary>
    public (int MinX, int MinY, int MaxX, int MaxY) Bounds()
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var s in Stitches)
        {
            if (s.Command != StitchCommand.Stitch) continue;
            minX = Math.Min(minX, s.X); minY = Math.Min(minY, s.Y);
            maxX = Math.Max(maxX, s.X); maxY = Math.Max(maxY, s.Y);
        }
        return minX == int.MaxValue ? (0, 0, 0, 0) : (minX, minY, maxX, maxY);
    }

    public StitchPattern Translated(int dx, int dy)
    {
        var p = CloneEmpty();
        foreach (var s in Stitches) p.Stitches.Add(s with { X = s.X + dx, Y = s.Y + dy });
        return p;
    }

    /// <summary>Cópia girada 90° no sentido horário (na tela, com Y para baixo): (x, y) → (−y, x).</summary>
    public StitchPattern Rotated90Clockwise()
    {
        var p = CloneEmpty();
        foreach (var s in Stitches) p.Stitches.Add(s with { X = -s.Y, Y = s.X });
        return p;
    }

    /// <summary>Cópia centralizada na origem (a máquina posiciona o centro do desenho no centro do bastidor).</summary>
    public StitchPattern Centered()
    {
        var b = Bounds();
        return Translated(-(b.MinX + b.MaxX) / 2, -(b.MinY + b.MaxY) / 2);
    }

    /// <summary>
    /// Divide movimentos maiores que <paramref name="max"/> unidades em partes iguais.
    /// Pontos longos viram vários pontos; saltos longos viram vários saltos.
    /// </summary>
    public StitchPattern WithMaxMove(int max)
    {
        var p = CloneEmpty();
        int x = 0, y = 0;
        foreach (var s in Stitches)
        {
            if (s.Command is StitchCommand.Stitch or StitchCommand.Jump)
            {
                int dx = s.X - x, dy = s.Y - y;
                int steps = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)) / (double)max);
                for (int i = 1; i < steps; i++)
                    p.Stitches.Add(new Stitch(x + (int)Math.Round(dx * (double)i / steps), y + (int)Math.Round(dy * (double)i / steps), s.Command));
                x = s.X; y = s.Y;
            }
            p.Stitches.Add(s);
        }
        return p;
    }

    /// <summary>Garante que existe uma linha para cada bloco de cor.</summary>
    public void FixThreadCount()
    {
        int blocks = 1 + Stitches.Count(s => s.Command == StitchCommand.ColorChange);
        var seq = BrotherPalette.ContrastSequence;
        while (Threads.Count < blocks) Threads.Add(BrotherPalette.Colors[seq[Threads.Count % seq.Count]]);
    }

    /// <summary>Blocos de cor com as polilinhas costuradas (para visualização). Saltos quebram a polilinha.</summary>
    public IEnumerable<(int ColorIndex, List<List<Vec2>> Runs)> ColorBlocks()
    {
        int color = 0;
        var runs = new List<List<Vec2>>();
        List<Vec2>? current = null;
        Vec2 last = Vec2.Zero;
        foreach (var s in Stitches)
        {
            var pt = new Vec2(s.X / 10.0, s.Y / 10.0);
            switch (s.Command)
            {
                case StitchCommand.Stitch:
                    if (current == null) { current = new List<Vec2> { last }; runs.Add(current); }
                    current.Add(pt);
                    last = pt;
                    break;
                case StitchCommand.Jump:
                    current = null;
                    last = pt;
                    break;
                case StitchCommand.Trim:
                    current = null;
                    break;
                case StitchCommand.ColorChange:
                    yield return (color, runs);
                    color++;
                    runs = new List<List<Vec2>>();
                    current = null;
                    break;
                case StitchCommand.End:
                    yield return (color, runs);
                    yield break;
            }
        }
        yield return (color, runs);
    }

    private StitchPattern CloneEmpty()
    {
        var p = new StitchPattern { Name = Name };
        p.Threads.AddRange(Threads);
        return p;
    }
}
