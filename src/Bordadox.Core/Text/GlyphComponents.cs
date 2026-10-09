using Bordadox.Core.Geometry;

namespace Bordadox.Core.Text;

/// <summary>Separa os contornos de uma letra em partes conexas: cada contorno externo com os furos dentro dele (o ponto do "i" é uma parte, o corpo é outra).</summary>
public static class GlyphComponents
{
    public static List<List<List<Vec2>>> Split(IReadOnlyList<IReadOnlyList<Vec2>> contours)
    {
        var list = contours.Where(c => c.Count >= 3).ToList();
        int n = list.Count;
        if (n <= 1) return n == 0 ? new() : new() { new() { list[0].ToList() } };

        var area = list.Select(c => Math.Abs(GeometryUtil.SignedArea(c))).ToArray();
        bool Contains(int outer, int inner) =>
            area[outer] > area[inner] && GeometryUtil.PointInContours(list[inner][0], new[] { list[outer] });

        var depth = new int[n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                if (i != j && Contains(i, j)) depth[j]++;

        var components = new List<List<List<Vec2>>>();
        var indexOf = new Dictionary<int, int>();
        for (int j = 0; j < n; j++)
            if (depth[j] % 2 == 0)
            {
                indexOf[j] = components.Count;
                components.Add(new() { list[j].ToList() });
            }
        for (int j = 0; j < n; j++)
        {
            if (depth[j] % 2 == 0) continue;
            int parent = -1;
            for (int i = 0; i < n; i++)
                if (depth[i] == depth[j] - 1 && Contains(i, j) && (parent < 0 || area[i] < area[parent])) parent = i;
            if (parent >= 0 && indexOf.TryGetValue(parent, out int comp)) components[comp].Add(list[j].ToList());
        }
        return components;
    }
}
