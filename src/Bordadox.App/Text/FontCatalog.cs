using System.Collections.Concurrent;
using System.Windows.Media;

namespace Bordadox.App.Text;

/// <summary>
/// Lista das fontes instaladas, preparada para quem tem milhares delas:
/// <list type="bullet">
/// <item>os nomes são lidos numa thread própria na abertura do programa (nunca na tela);</item>
/// <item>só depois, em prioridade baixa, cada fonte é conferida (tem letras latinas?) e as inúteis
/// (símbolos, ícones, só árabes...) são marcadas para sair da busca;</item>
/// <item>a busca é só texto em memória; as fontes em si só são abertas quando aparecem na lista ou são usadas.</item>
/// </list>
/// </summary>
public static class FontCatalog
{
    private static readonly object Gate = new();
    private static Task<IReadOnlyList<string>>? _names;
    private static Task? _scan;
    private static readonly ConcurrentDictionary<string, byte> Unusable = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Começa a ler as fontes em segundo plano. Pode ser chamado várias vezes.</summary>
    public static void Start()
    {
        lock (Gate)
        {
            if (_names != null) return;
            var names = new TaskCompletionSource<IReadOnlyList<string>>();
            var scan = new TaskCompletionSource();
            _names = names.Task;
            _scan = scan.Task;

            var thread = new Thread(() =>
            {
                IReadOnlyList<string> list;
                try
                {
                    list = Fonts.SystemFontFamilies
                        .Select(f => f.Source)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();
                }
                catch (Exception)
                {
                    list = Array.Empty<string>();
                }
                names.SetResult(list);

                try
                {
                    foreach (var name in list)
                        if (!WpfGlyphProvider.HasLatinLetters(name)) Unusable[name] = 0;
                }
                finally
                {
                    scan.SetResult();
                }
            })
            { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Bordadox.Fontes" };
            thread.Start();
        }
    }

    /// <summary>Todos os nomes de família, em ordem alfabética. Espera só a leitura dos nomes (rápida).</summary>
    public static IReadOnlyList<string> Names
    {
        get
        {
            Start();
            return _names!.GetAwaiter().GetResult();
        }
    }

    /// <summary>Termina quando a conferência de todas as fontes acabou.</summary>
    public static Task ScanCompleted
    {
        get
        {
            Start();
            return _scan!;
        }
    }

    public static bool IsScanComplete => _scan?.IsCompleted == true;

    /// <summary>true se já se sabe que a fonte não desenha letras latinas.</summary>
    public static bool IsKnownUnusable(string family) => Unusable.ContainsKey(family);

    /// <summary>
    /// Filtra por todas as palavras digitadas (em qualquer ordem, sem diferenciar maiúsculas), mostrando antes
    /// os nomes que começam com o que foi digitado. Esconde as fontes já conhecidas como inúteis.
    /// </summary>
    public static List<string> Search(IReadOnlyList<string> names, string? query)
    {
        var tokens = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<string>();
        var later = new List<string>();
        string prefix = (query ?? "").Trim();

        foreach (var name in names)
        {
            if (Unusable.ContainsKey(name)) continue;
            bool all = true;
            foreach (var t in tokens)
                if (name.IndexOf(t, StringComparison.CurrentCultureIgnoreCase) < 0) { all = false; break; }
            if (!all) continue;

            if (prefix.Length > 0 && name.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase)) result.Add(name);
            else later.Add(name);
        }
        result.AddRange(later);
        return result;
    }
}
