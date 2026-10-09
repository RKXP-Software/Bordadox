namespace Bordadox.Core.Digitize;

public sealed class ComponentInfo
{
    public int Label { get; init; }
    public int Area { get; set; }
}

/// <summary>Regiões conexas (vizinhança de 4) de um mapa de rótulos.</summary>
public sealed class ComponentMap
{
    public int Width { get; init; }
    public int Height { get; init; }
    /// <summary>Número da região de cada pixel.</summary>
    public int[] Id { get; init; } = Array.Empty<int>();
    public List<ComponentInfo> Items { get; init; } = new();

    public static ComponentMap Build(int[] labels, int w, int h, CancellationToken ct = default)
    {
        var id = new int[w * h];
        Array.Fill(id, -1);
        var items = new List<ComponentInfo>();
        var stack = new Stack<int>();

        for (int start = 0; start < labels.Length; start++)
        {
            if (id[start] >= 0) continue;
            if ((start & 0xFFFFF) == 0) ct.ThrowIfCancellationRequested();
            int label = labels[start], comp = items.Count, area = 0;
            id[start] = comp;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                area++;
                int x = i % w, y = i / w;
                if (x > 0) Push(i - 1);
                if (x < w - 1) Push(i + 1);
                if (y > 0) Push(i - w);
                if (y < h - 1) Push(i + w);
            }
            items.Add(new ComponentInfo { Label = label, Area = area });

            void Push(int j)
            {
                if (id[j] >= 0 || labels[j] != label) return;
                id[j] = comp;
                stack.Push(j);
            }
        }
        return new ComponentMap { Width = w, Height = h, Id = id, Items = items };
    }
}

public static class LabelCleaner
{
    /// <summary>
    /// Regiões menores que <paramref name="minArea"/> pixels (de qualquer cor, inclusive furos de fundo) passam a ter
    /// o rótulo mais comum entre seus vizinhos. Repete até estabilizar (no máximo 4 vezes).
    /// </summary>
    public static int RemoveSmall(int[] labels, int w, int h, int minArea, CancellationToken ct = default)
    {
        if (minArea <= 1) return 0;
        int total = 0;
        for (int pass = 0; pass < 4; pass++)
        {
            ct.ThrowIfCancellationRequested();
            var map = ComponentMap.Build(labels, w, h, ct);
            var small = new HashSet<int>();
            for (int c = 0; c < map.Items.Count; c++)
                if (map.Items[c].Area < minArea) small.Add(c);
            if (small.Count == 0) break;

            // Para cada região pequena, conta os rótulos dos vizinhos.
            var votes = new Dictionary<int, Dictionary<int, int>>();
            for (int i = 0; i < labels.Length; i++)
            {
                int c = map.Id[i];
                if (!small.Contains(c)) continue;
                int x = i % w, y = i / w;
                if (x > 0) Vote(c, i - 1);
                if (x < w - 1) Vote(c, i + 1);
                if (y > 0) Vote(c, i - w);
                if (y < h - 1) Vote(c, i + w);

                void Vote(int comp, int j)
                {
                    if (map.Id[j] == comp) return;
                    if (!votes.TryGetValue(comp, out var d)) votes[comp] = d = new Dictionary<int, int>();
                    d[labels[j]] = d.GetValueOrDefault(labels[j]) + 1;
                }
            }

            var newLabel = new Dictionary<int, int>();
            foreach (var (comp, d) in votes)
                newLabel[comp] = d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
            if (newLabel.Count == 0) break;

            for (int i = 0; i < labels.Length; i++)
                if (newLabel.TryGetValue(map.Id[i], out var nl)) labels[i] = nl;
            total += newLabel.Count;
        }
        return total;
    }

    /// <summary>Dilata a máscara em <paramref name="radius"/> pixels (cruz 3×3 repetida).</summary>
    public static bool[] Dilate(bool[] mask, int w, int h, int radius)
    {
        var cur = (bool[])mask.Clone();
        for (int r = 0; r < radius; r++)
        {
            var next = (bool[])cur.Clone();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (cur[i]) continue;
                    if ((x > 0 && cur[i - 1]) || (x < w - 1 && cur[i + 1]) || (y > 0 && cur[i - w]) || (y < h - 1 && cur[i + w]))
                        next[i] = true;
                }
            cur = next;
        }
        return cur;
    }
}
