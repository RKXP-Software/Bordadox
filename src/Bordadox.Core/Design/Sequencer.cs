using System.Text.Json;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;

namespace Bordadox.Core.Design;

/// <summary>
/// Guarda os pontos gerados por objeto para não regenerar o que não mudou.
/// A chave é o JSON do objeto mais o ponto de entrada (que afeta a ordem do preenchimento).
/// </summary>
public sealed class StitchCache
{
    private readonly Dictionary<Guid, (string Key, List<List<Vec2>> Runs)> _items = new();

    public List<List<Vec2>> Get(EmbObject obj, Vec2? from)
    {
        var key = JsonSerializer.Serialize(obj, DesignJson.Options) +
                  (from is { } f ? $"|{f.X:0.0},{f.Y:0.0}" : "|-");
        if (_items.TryGetValue(obj.Id, out var hit) && hit.Key == key) return hit.Runs;
        var runs = obj.GenerateRuns(from);
        _items[obj.Id] = (key, runs);
        return runs;
    }

    /// <summary>Último resultado gerado para o objeto (sem regenerar), para desenho e cliques.</summary>
    public List<List<Vec2>>? Peek(Guid id) => _items.TryGetValue(id, out var hit) ? hit.Runs : null;

    public void Retain(IEnumerable<Guid> ids)
    {
        var keep = ids.ToHashSet();
        foreach (var id in _items.Keys.Where(k => !keep.Contains(k)).ToList()) _items.Remove(id);
    }
}

/// <summary>Monta a sequência de máquina: liga os objetos com saltos/cortes, troca cores e aplica arremates.</summary>
public static class Sequencer
{
    private const double TieLength = 0.5;

    public static StitchPattern Build(EmbDesign design, string name, StitchCache? cache = null)
    {
        var s = design.Settings;
        var pattern = new StitchPattern { Name = name };
        ThreadColor? color = null;
        Vec2? pos = null;      // posição atual da agulha (mm)
        Vec2? prevPos = null;  // posição anterior, para direção do arremate
        bool needTieIn = true;

        void Add(Vec2 p, StitchCommand cmd) =>
            pattern.Add((int)Math.Round(p.X * 10), (int)Math.Round(p.Y * 10), cmd);

        void Stitch(Vec2 p)
        {
            if (pos is { } cur && Vec2.Distance(cur, p) < s.MinStitchMm) return;
            Add(p, StitchCommand.Stitch);
            prevPos = pos;
            pos = p;
        }

        void TieOff()
        {
            if (!s.TieStitches || pos is not { } e || prevPos is not { } q) return;
            var u = (q - e).Normalized() * TieLength;
            if (u == Vec2.Zero) return;
            for (int i = 0; i < 2; i++) { Add(e + u, StitchCommand.Stitch); Add(e, StitchCommand.Stitch); }
        }

        void TieIn(Vec2 start, Vec2 next)
        {
            if (!s.TieStitches) return;
            var u = (next - start).Normalized() * TieLength;
            if (u == Vec2.Zero) u = new Vec2(TieLength, 0);
            for (int i = 0; i < 2; i++) { Add(start + u, StitchCommand.Stitch); Add(start, StitchCommand.Stitch); }
        }

        void TrimAndJump(Vec2 to, bool trim)
        {
            if (trim && pos != null)
            {
                TieOff();
                Add(pos.Value, StitchCommand.Trim);
                needTieIn = true;
            }
            Add(to, StitchCommand.Jump);
            pos = to;
            prevPos = null;
        }

        foreach (var obj in design.Objects)
        {
            var runs = (cache?.Get(obj, pos) ?? obj.GenerateRuns(pos)).Where(r => r.Count >= 2).ToList();
            if (runs.Count == 0) continue;

            if (color != null && obj.Color != color)
            {
                TieOff();
                if (pos != null) Add(pos.Value, StitchCommand.Trim);
                Add(pos ?? Vec2.Zero, StitchCommand.ColorChange);
                needTieIn = true;
                prevPos = null;
                pattern.Threads.Add(obj.Color);
                // Depois da troca de cor a máquina já cortou; o próximo deslocamento é um salto simples.
                TrimAndJump(runs[0][0], trim: false);
            }
            else if (color == null)
            {
                pattern.Threads.Add(obj.Color);
            }
            color = obj.Color;

            foreach (var run in runs)
            {
                var start = run[0];
                if (pos is not { } cur) TrimAndJump(start, trim: false);
                else
                {
                    double d = Vec2.Distance(cur, start);
                    if (d > s.TrimAtMm) TrimAndJump(start, trim: true);
                    else if (d > 1.0 && !needTieIn) TrimAndJump(start, trim: false); // salto curto, sem corte
                    else if (d >= s.MinStitchMm) Stitch(start);
                }

                if (needTieIn)
                {
                    if (pos is not { } p0 || Vec2.Distance(p0, start) > 0.01) Add(start, StitchCommand.Jump);
                    pos = start;
                    TieIn(start, run.Skip(1).FirstOrDefault(p => Vec2.Distance(p, start) > 0.1, start));
                    needTieIn = false;
                }
                for (int i = 1; i < run.Count; i++) Stitch(run[i]);
            }
        }

        if (pos != null)
        {
            TieOff();
            Add(pos.Value, StitchCommand.Trim);
            Add(pos.Value, StitchCommand.End);
        }
        else pattern.Add(0, 0, StitchCommand.End);
        cache?.Retain(design.Objects.Select(o => o.Id));
        return pattern;
    }
}
