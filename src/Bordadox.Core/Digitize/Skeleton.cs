namespace Bordadox.Core.Digitize;

/// <summary>Separa as partes finas de uma máscara e acha a linha central (esqueleto) de cada traço.</summary>
public static class Skeleton
{
    // Vizinhos em sentido horário a partir do norte: N, NE, E, SE, S, SW, W, NW.
    private static readonly int[] Dx = { 0, 1, 1, 1, 0, -1, -1, -1 };
    private static readonly int[] Dy = { -1, -1, 0, 1, 1, 1, 0, -1 };

    /// <summary>
    /// Parte da máscara mais fina que <c>2·radius</c> pixels (traços, fios). O resto (as partes grossas) mantém os cantos:
    /// a "abertura" arredonda os cantos, então a parte grossa é reconstruída dilatando de volta dentro da máscara.
    /// </summary>
    public static bool[] ThinPart(bool[] mask, int w, int h, int radius, CancellationToken ct = default)
    {
        var thin = new bool[mask.Length];
        if (!TryBounds(mask, w, h, out int x0, out int y0, out int x1, out int y1)) return thin;

        int m = radius + Math.Max(1, radius / 4) + 3;
        x0 = Math.Max(0, x0 - m); y0 = Math.Max(0, y0 - m);
        x1 = Math.Min(w - 1, x1 + m); y1 = Math.Min(h - 1, y1 + m);
        int cw = x1 - x0 + 1, ch = y1 - y0 + 1;

        var local = new bool[cw * ch];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++) local[y * cw + x] = mask[(y + y0) * w + x + x0];

        // Abertura = erosão seguida de dilatação (a erosão é a dilatação do vazio).
        var inverse = new bool[local.Length];
        for (int i = 0; i < local.Length; i++) inverse[i] = !local[i];
        bool[] Open(int r)
        {
            var eroded = LabelCleaner.Dilate(inverse, cw, ch, r);
            for (int i = 0; i < eroded.Length; i++) eroded[i] = !eroded[i];
            ct.ThrowIfCancellationRequested();
            return LabelCleaner.Dilate(eroded, cw, ch, r);
        }
        var opened = Open(radius);

        // Histerese: um trecho "grosso" só vale se alguma parte dele for CLARAMENTE grossa (raio maior). Um fio que oscila
        // em torno do limite de espessura (1,1 a 1,3 mm) não tem nenhuma parte assim e fica todo como linha, em vez de
        // alternar entre linha e remendos de preenchimento.
        var strong = Open(radius + Math.Max(1, radius / 4));
        var blobs = new int[local.Length];
        for (int i = 0; i < local.Length; i++) blobs[i] = opened[i] ? 1 : 0;
        var blobMap = ComponentMap.Build(blobs, cw, ch, ct);
        var hasStrong = new bool[blobMap.Items.Count];
        for (int i = 0; i < local.Length; i++) if (opened[i] && strong[i]) hasStrong[blobMap.Id[i]] = true;
        for (int i = 0; i < local.Length; i++) if (opened[i] && !hasStrong[blobMap.Id[i]]) opened[i] = false;

        // A abertura arredonda os cantos das partes grossas. Recupera os cantos dilatando só um pouco (metade do raio),
        // sem sair da máscara: dilatar o raio inteiro engoliria vários milímetros de qualquer fio ligado à forma.
        var thick = LabelCleaner.Dilate(opened, cw, ch, Math.Max(1, (radius + 1) / 2));
        for (int i = 0; i < thick.Length; i++) thick[i] &= local[i];

        // O que sobrou é "fino". Fragmentos pequenos e isolados (chanfro de uma quina) voltam para a parte grossa.
        var leftover = new int[local.Length];
        for (int i = 0; i < local.Length; i++) leftover[i] = local[i] && !thick[i] ? 1 : 0;
        var parts = ComponentMap.Build(leftover, cw, ch, ct);
        int minPart = 2 * radius * radius;

        // Cruzamentos e bifurcações de fios finos têm um ponto onde a forma é "mais grossa" (duas barras sobrepostas).
        // Esse remendo pequeno, ligado a 2 ou mais traços finos, é parte do traço e não um preenchimento à parte:
        // sem isto a linha se parte em quatro no cruzamento.
        var thickMap = ComponentMap.Build(thick.Select(t => t ? 1 : 0).ToArray(), cw, ch, ct);
        int comps = thickMap.Items.Count;
        var minX = new int[comps]; var maxX = new int[comps]; var minY = new int[comps]; var maxY = new int[comps];
        Array.Fill(minX, int.MaxValue); Array.Fill(minY, int.MaxValue);
        var attached = new HashSet<int>[comps];
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int i = y * cw + x;
                if (!thick[i]) continue;
                int c = thickMap.Id[i];
                if (x < minX[c]) minX[c] = x; if (x > maxX[c]) maxX[c] = x;
                if (y < minY[c]) minY[c] = y; if (y > maxY[c]) maxY[c] = y;
                void Look(int j)
                {
                    if (leftover[j] == 1 && parts.Items[parts.Id[j]].Area >= minPart) (attached[c] ??= new()).Add(parts.Id[j]);
                }
                if (x > 0) Look(i - 1);
                if (x < cw - 1) Look(i + 1);
                if (y > 0) Look(i - cw);
                if (y < ch - 1) Look(i + cw);
            }
        var junctionPatch = new bool[comps];
        for (int c = 0; c < comps; c++)
            junctionPatch[c] = thickMap.Items[c].Label == 1 && attached[c] is { Count: >= 2 }
                && Math.Max(maxX[c] - minX[c], maxY[c] - minY[c]) + 1 <= 6 * radius;
        if (junctionPatch.Any(j => j))
        {
            // Um nó de cruzamento mal passa de ~1,5x a espessura do fio; uma bolinha ou forma realmente grossa (raio
            // 2x o do limite) é um preenchimento de verdade, mesmo com fios saindo dela.
            var huge = Open(2 * radius);
            for (int i = 0; i < huge.Length; i++)
                if (huge[i] && thick[i]) junctionPatch[thickMap.Id[i]] = false;
        }

        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++)
            {
                int i = y * cw + x;
                bool part = leftover[i] == 1 && parts.Items[parts.Id[i]].Area >= minPart;
                if (part || (thick[i] && junctionPatch[thickMap.Id[i]])) thin[(y + y0) * w + x + x0] = true;
            }
        return thin;
    }

    private static bool TryBounds(bool[] mask, int w, int h, out int x0, out int y0, out int x1, out int y1)
    {
        x0 = w; y0 = h; x1 = -1; y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (mask[y * w + x])
                {
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }
        return x1 >= 0;
    }

    /// <summary>Esqueleto de espessura 1 pixel (Zhang-Suen), já sem os pixels de "quina" que criariam falsas bifurcações.</summary>
    public static bool[] Thin(bool[] mask, int w, int h, CancellationToken ct = default)
    {
        var result = new bool[mask.Length];
        if (!TryBounds(mask, w, h, out int x0, out int y0, out int x1, out int y1)) return result;

        // Cópia com 1 pixel de folga em volta, para não testar limites a cada vizinho.
        int cw = x1 - x0 + 3, ch = y1 - y0 + 3;
        var a = new bool[cw * ch];
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if (mask[y * w + x]) a[(y - y0 + 1) * cw + (x - x0 + 1)] = true;

        var offsets = new int[8];
        for (int k = 0; k < 8; k++) offsets[k] = Dy[k] * cw + Dx[k];

        var remove = new List<int>();
        bool changed;
        int guard = 0;
        do
        {
            changed = false;
            for (int step = 0; step < 2; step++)
            {
                remove.Clear();
                for (int y = 1; y < ch - 1; y++)
                    for (int x = 1; x < cw - 1; x++)
                    {
                        int i = y * cw + x;
                        if (!a[i]) continue;
                        bool n = a[i + offsets[0]], ne = a[i + offsets[1]], e = a[i + offsets[2]], se = a[i + offsets[3]];
                        bool s = a[i + offsets[4]], sw = a[i + offsets[5]], wv = a[i + offsets[6]], nw = a[i + offsets[7]];

                        int b = (n ? 1 : 0) + (ne ? 1 : 0) + (e ? 1 : 0) + (se ? 1 : 0) + (s ? 1 : 0) + (sw ? 1 : 0) + (wv ? 1 : 0) + (nw ? 1 : 0);
                        if (b < 2 || b > 6) continue;

                        int transitions = 0;
                        if (!n && ne) transitions++;
                        if (!ne && e) transitions++;
                        if (!e && se) transitions++;
                        if (!se && s) transitions++;
                        if (!s && sw) transitions++;
                        if (!sw && wv) transitions++;
                        if (!wv && nw) transitions++;
                        if (!nw && n) transitions++;
                        if (transitions != 1) continue;

                        bool ok = step == 0 ? !(n && e && s) && !(e && s && wv) : !(n && e && wv) && !(n && s && wv);
                        if (ok) remove.Add(i);
                    }
                foreach (var i in remove) a[i] = false;
                if (remove.Count > 0) changed = true;
            }
            if ((++guard & 7) == 0) ct.ThrowIfCancellationRequested();
        } while (changed && guard < 400);

        // Pixel de "quina": tem exatamente dois vizinhos e eles já se tocam (diagonal). Sem ele a linha continua ligada.
        for (int y = 1; y < ch - 1; y++)
            for (int x = 1; x < cw - 1; x++)
            {
                int i = y * cw + x;
                if (!a[i]) continue;
                int first = -1, second = -1, count = 0;
                for (int k = 0; k < 8; k++)
                    if (a[i + offsets[k]]) { count++; if (first < 0) first = k; else if (second < 0) second = k; }
                if (count == 2 && AreTouching(first, second)) a[i] = false;
            }

        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                result[y * w + x] = a[(y - y0 + 1) * cw + (x - x0 + 1)];
        return result;
    }

    private static bool AreTouching(int k1, int k2)
    {
        int dx = Math.Abs(Dx[k1] - Dx[k2]), dy = Math.Abs(Dy[k1] - Dy[k2]);
        return dx <= 1 && dy <= 1;
    }

    /// <summary>
    /// Quantos ramos saem do pixel: grupos SEPARADOS de vizinhos (0 = isolado, 1 = ponta, 2 = trecho de linha, 3 ou mais = bifurcação).
    /// Contar grupos e não vizinhos é o que evita falsas bifurcações nas "quinas" de uma linha diagonal em escada.
    /// </summary>
    public static int Branches(bool[] skeleton, int w, int h, int x, int y)
    {
        Span<bool> around = stackalloc bool[8];
        int count = 0;
        for (int k = 0; k < 8; k++)
        {
            int nx = x + Dx[k], ny = y + Dy[k];
            around[k] = nx >= 0 && ny >= 0 && nx < w && ny < h && skeleton[ny * w + nx];
            if (around[k]) count++;
        }
        if (count == 0) return 0;
        int groups = 0;
        for (int k = 0; k < 8; k++)
            if (around[k] && !around[(k + 7) % 8]) groups++;
        return groups == 0 ? 8 : groups; // todos os 8 vizinhos: bloco cheio, trata como bifurcação
    }

    /// <summary>
    /// Poda os "espinhos": pontinhas curtas (até <paramref name="maxSpurPixels"/> pixels) que saem de uma bifurcação.
    /// São restos da afinação em bordas irregulares; sem a poda, cada um cria uma falsa bifurcação e corta o traço em pedaços.
    /// Repete algumas vezes, porque tirar um espinho pode deixar outro como ponta.
    /// </summary>
    public static void Prune(bool[] skeleton, int w, int h, int maxSpurPixels, int rounds = 4)
    {
        if (maxSpurPixels < 1) return;
        int n = w * h;
        for (int round = 0; round < rounds; round++)
        {
            var type = new sbyte[n];
            for (int i = 0; i < n; i++)
                if (skeleton[i]) type[i] = (sbyte)Math.Min(8, Branches(skeleton, w, h, i % w, i / w));

            bool changed = false;
            for (int e = 0; e < n; e++)
            {
                if (!skeleton[e] || type[e] != 1) continue;
                var path = new List<int> { e };
                int prev = -1, cur = e;
                bool reachedJunction = false;
                while (path.Count <= maxSpurPixels + 1)
                {
                    int next = -1;
                    int x = cur % w, y = cur / w;
                    for (int k = 0; k < 8 && next < 0; k++)
                    {
                        int nx = x + Dx[WalkOrder[k]], ny = y + Dy[WalkOrder[k]];
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h || !skeleton[ny * w + nx]) continue;
                        int ni = ny * w + nx;
                        if (ni != prev && !path.Contains(ni)) next = ni;
                    }
                    if (next < 0) break;                     // traço isolado: não é espinho
                    if (type[next] >= 3) { reachedJunction = true; break; }
                    if (type[next] == 1) break;              // outra ponta: também é um traço isolado
                    path.Add(next);
                    prev = cur;
                    cur = next;
                }
                if (!reachedJunction) continue;
                foreach (var i in path) skeleton[i] = false;
                changed = true;
            }
            if (!changed) break;
        }
    }

    // Ordem de preferência ao caminhar: primeiro os vizinhos de lado (N, L, S, O), depois os diagonais.
    private static readonly int[] WalkOrder = { 0, 2, 4, 6, 1, 3, 5, 7 };

    /// <summary>
    /// Divide o esqueleto em caminhos (de ponta a ponta, ou até uma bifurcação). Um anel sem pontas vira um caminho
    /// fechado (o último ponto repete o primeiro). Cada caminho é a lista de pixels em ordem.
    /// </summary>
    public static List<List<PointI>> Trace(bool[] skeleton, int w, int h, CancellationToken ct = default)
    {
        int n = w * h;
        var type = new sbyte[n];
        for (int i = 0; i < n; i++)
            if (skeleton[i]) type[i] = (sbyte)Math.Min(8, Branches(skeleton, w, h, i % w, i / w));

        var visited = new bool[n];
        var paths = new List<List<PointI>>();

        PointI P(int i) => new(i % w, i / w);

        IEnumerable<int> Neighbors(int i)
        {
            int x = i % w, y = i / w;
            foreach (int k in WalkOrder)
            {
                int nx = x + Dx[k], ny = y + Dy[k];
                if (nx >= 0 && ny >= 0 && nx < w && ny < h && skeleton[ny * w + nx]) yield return ny * w + nx;
            }
        }

        bool Touching(int a, int b) => Math.Abs(a % w - b % w) <= 1 && Math.Abs(a / w - b / w) <= 1;

        // Próximo passo: um vizinho ainda não visitado, de preferência um que NÃO encosta no pixel de onde viemos
        // (esse é o que segue adiante; o que encosta é uma "quina" ao lado). Quinas que sobram viram caminhos de 1 pixel, descartados depois.
        int PickNext(int cur, int prev)
        {
            int fallback = -1;
            foreach (var nb in Neighbors(cur))
            {
                if (nb == prev || visited[nb]) continue;
                if (prev >= 0 && Touching(nb, prev)) { if (fallback < 0) fallback = nb; continue; }
                return nb;
            }
            return fallback;
        }

        List<PointI> Walk(int start, int first)
        {
            var path = new List<PointI> { P(start) };
            int prev = start, cur = first;
            while (true)
            {
                path.Add(P(cur));
                if (type[cur] != 2) break;                 // ponta ou bifurcação: fim do trecho
                visited[cur] = true;
                int next = PickNext(cur, prev);
                if (next < 0) break;
                prev = cur;
                cur = next;
            }
            return path;
        }

        // 1) Caminhos que saem de pontas e de bifurcações.
        for (int i = 0; i < n; i++)
        {
            if (!skeleton[i] || type[i] == 2 || type[i] == 0) continue;
            if ((i & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            foreach (var nb in Neighbors(i))
                if (type[nb] == 2 && !visited[nb]) paths.Add(Walk(i, nb));
        }

        // 2) O que sobrou são anéis, sem ponta nem bifurcação.
        for (int i = 0; i < n; i++)
        {
            if (!skeleton[i] || type[i] != 2 || visited[i]) continue;
            var path = new List<PointI> { P(i) };
            visited[i] = true;
            int prev = -1, cur = i;
            while (true)
            {
                int next = PickNext(cur, prev);
                if (next < 0) break;
                path.Add(P(next));
                visited[next] = true;
                prev = cur;
                cur = next;
            }
            if (path.Count > 3 && Touching(i, cur)) path.Add(P(i)); // fechou o anel
            paths.Add(path);
        }

        // 3) Pixels isolados (sem nenhum vizinho): caminhos de um ponto só.
        for (int i = 0; i < n; i++)
            if (skeleton[i] && type[i] == 0) paths.Add(new List<PointI> { P(i) });
        return paths;
    }

    /// <summary>
    /// Liga os trechos que se encontram numa bifurcação ou cruzamento em linhas contínuas: em cada encontro, os braços que
    /// seguem "em frente" um do outro (ângulo de pelo menos ~120°) viram um caminho só. Um cruzamento de duas linhas vira
    /// duas linhas inteiras, em vez de quatro pedaços; num T, a linha de cima continua e só o braço do T fica separado.
    /// Menos pedaços significa menos cortes de linha e menos pontos de arremate na máquina.
    /// </summary>
    public static List<List<PointI>> JoinAtJunctions(List<List<PointI>> paths, bool[] skeleton, int w, int h,
        int clusterRadius = 3, int lookAhead = 8, double maxCos = -0.5)
    {
        // Pontas de caminhos (token = 2·caminho + 0 para o início, +1 para o fim) que terminam numa bifurcação.
        var ends = new List<(int Token, PointI P)>();
        for (int p = 0; p < paths.Count; p++)
        {
            var path = paths[p];
            if (path.Count < 2) continue;
            if (Branches(skeleton, w, h, path[0].X, path[0].Y) >= 3) ends.Add((2 * p, path[0]));
            if (Branches(skeleton, w, h, path[^1].X, path[^1].Y) >= 3) ends.Add((2 * p + 1, path[^1]));
        }
        if (ends.Count < 2) return paths;

        // Pontas vizinhas (uma bifurcação no esqueleto costuma ter 2-3 pixels de largura) formam um mesmo encontro.
        var parent = Enumerable.Range(0, ends.Count).ToArray();
        int Find(int a) { while (parent[a] != a) a = parent[a] = parent[parent[a]]; return a; }
        for (int i = 0; i < ends.Count; i++)
            for (int j = i + 1; j < ends.Count; j++)
                if (Math.Abs(ends[i].P.X - ends[j].P.X) <= clusterRadius && Math.Abs(ends[i].P.Y - ends[j].P.Y) <= clusterRadius)
                    parent[Find(i)] = Find(j);

        var dropped = new bool[paths.Count];
        var link = new Dictionary<int, int>();   // token → token da ponta ligada a ele

        (double X, double Y) Direction(int token)
        {
            var path = paths[token / 2];
            bool atStart = token % 2 == 0;
            var from = atStart ? path[0] : path[^1];
            int k = Math.Min(path.Count - 1, lookAhead);
            var to = atStart ? path[k] : path[path.Count - 1 - k];
            double dx = to.X - from.X, dy = to.Y - from.Y, len = Math.Sqrt(dx * dx + dy * dy);
            return len < 1e-9 ? (0, 0) : (dx / len, dy / len);
        }

        foreach (var group in Enumerable.Range(0, ends.Count).GroupBy(Find).Where(g => g.Count() >= 2))
        {
            var members = group.Select(i => ends[i].Token).ToList();

            // Trecho curto com as duas pontas no mesmo encontro é só o miolo da bifurcação: não é um braço.
            foreach (var t in members.ToList())
            {
                int p = t / 2;
                if (!dropped[p] && paths[p].Count <= 2 * clusterRadius + 2 && members.Contains(2 * p) && members.Contains(2 * p + 1))
                {
                    dropped[p] = true;
                    members.Remove(2 * p);
                    members.Remove(2 * p + 1);
                }
            }

            var candidates = new List<(double Cos, int A, int B)>();
            for (int i = 0; i < members.Count; i++)
                for (int j = i + 1; j < members.Count; j++)
                {
                    var da = Direction(members[i]); var db = Direction(members[j]);
                    double cos = da.X * db.X + da.Y * db.Y;
                    if (cos <= maxCos) candidates.Add((cos, members[i], members[j]));
                }
            foreach (var (_, a, b) in candidates.OrderBy(c => c.Cos))
            {
                if (link.ContainsKey(a) || link.ContainsKey(b)) continue;
                link[a] = b;
                link[b] = a;
            }
        }
        if (link.Count == 0 && !dropped.Any(d => d)) return paths;

        // Monta as cadeias: de uma ponta sem ligação, segue o caminho, salta pela ligação, e assim por diante.
        var result = new List<List<PointI>>();
        var used = new bool[paths.Count];

        List<PointI> Oriented(int token) // pontos do caminho, começando pela ponta "token"
        {
            var p = paths[token / 2];
            return token % 2 == 0 ? p : Enumerable.Reverse(p).ToList();
        }

        List<PointI> Chain(int startToken)
        {
            var chain = new List<PointI>();
            int token = startToken;
            while (true)
            {
                int p = token / 2;
                used[p] = true;
                foreach (var pt in Oriented(token))
                    if (chain.Count == 0 || chain[^1] != pt) chain.Add(pt);
                int other = token ^ 1;                       // a outra ponta do mesmo caminho
                if (!link.TryGetValue(other, out int next) || used[next / 2]) break;
                token = next;
            }
            return chain;
        }

        for (int p = 0; p < paths.Count; p++)
        {
            if (used[p] || dropped[p]) continue;
            if (!link.ContainsKey(2 * p)) result.Add(Chain(2 * p));
            else if (!link.ContainsKey(2 * p + 1)) result.Add(Chain(2 * p + 1));
        }
        // O que sobrou está em ciclos (todas as pontas ligadas): vira um caminho fechado.
        for (int p = 0; p < paths.Count; p++)
        {
            if (used[p] || dropped[p]) continue;
            var chain = Chain(2 * p);
            if (chain.Count > 3 && chain[0] != chain[^1]) chain.Add(chain[0]);
            result.Add(chain);
        }
        return result;
    }
}
