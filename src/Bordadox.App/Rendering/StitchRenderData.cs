using System.Windows.Media;
using Bordadox.Core.Stitches;

namespace Bordadox.App.Rendering;

/// <summary>Segmento desenhável em milímetros. Um por ponto ou salto do arquivo, na ordem de costura.</summary>
public readonly record struct StitchSegment(float X1, float Y1, float X2, float Y2, int ColorIndex, bool IsJump);

/// <summary>Resumo de um bloco de cor para a lista lateral.</summary>
public sealed record ColorBlockInfo(int Number, ThreadColor Thread, int StitchCount)
{
    public SolidColorBrush Brush { get; } = MakeBrush(Thread);
    public string Label => $"{Number}. {Thread.Name} ({Thread.Code})";
    public string Detail => $"{StitchCount:N0} pontos";

    private static SolidColorBrush MakeBrush(ThreadColor t)
    {
        var b = new SolidColorBrush(Color.FromRgb(t.R, t.G, t.B));
        b.Freeze();
        return b;
    }
}

/// <summary>Converte um <see cref="StitchPattern"/> em segmentos prontos para desenhar e simular.</summary>
public sealed class StitchRenderData
{
    public StitchPattern Pattern { get; }
    public IReadOnlyList<StitchSegment> Segments { get; }
    public IReadOnlyList<ColorBlockInfo> Blocks { get; }
    public IReadOnlyList<Color> Colors { get; }

    public int StitchCount { get; }
    public int JumpCount { get; }
    public int TrimCount { get; }
    public double WidthMm { get; }
    public double HeightMm { get; }

    public StitchRenderData(StitchPattern pattern)
    {
        Pattern = pattern;
        pattern.FixThreadCount();
        Colors = pattern.Threads.Select(t => Color.FromRgb(t.R, t.G, t.B)).ToList();

        var segs = new List<StitchSegment>(pattern.Stitches.Count);
        var perColor = new int[pattern.Threads.Count];
        int color = 0, x = 0, y = 0;
        foreach (var s in pattern.Stitches)
        {
            switch (s.Command)
            {
                case StitchCommand.Stitch:
                case StitchCommand.Jump:
                    bool jump = s.Command == StitchCommand.Jump;
                    if (s.X != x || s.Y != y || !jump)
                        segs.Add(new StitchSegment(x / 10f, y / 10f, s.X / 10f, s.Y / 10f, color, jump));
                    if (jump) JumpCount++;
                    else { StitchCount++; perColor[color]++; }
                    x = s.X; y = s.Y;
                    break;
                case StitchCommand.Trim:
                    TrimCount++;
                    break;
                case StitchCommand.ColorChange:
                    color = Math.Min(color + 1, pattern.Threads.Count - 1);
                    break;
            }
        }
        Segments = segs;
        Blocks = pattern.Threads.Select((t, i) => new ColorBlockInfo(i + 1, t, perColor[i])).ToList();

        var b = pattern.Bounds();
        WidthMm = (b.MaxX - b.MinX) / 10.0;
        HeightMm = (b.MaxY - b.MinY) / 10.0;
    }
}
