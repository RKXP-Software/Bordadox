namespace Bordadox.Core.Formats;

/// <summary>
/// Espera o usuário converter um .emb em outro programa (Wilcom, TrueSizer...) e acha o arquivo convertido: um DST, PES, EXP, JEF ou VP3
/// com o mesmo nome do .emb, salvo DEPOIS de começar a espera, na pasta do .emb ou em outras pastas (Downloads, Área de Trabalho).
/// Só devolve o arquivo quando ele parou de crescer (duas consultas seguidas iguais) e já pode ser lido, para não abrir pela metade.
/// </summary>
public sealed class ConvertedFileFinder
{
    public static readonly string[] Extensions = { "dst", "pes", "exp", "jef", "vp3" };

    private readonly string _baseName;
    private readonly List<string> _dirs;
    private readonly DateTime _since;
    private readonly Dictionary<string, (long Size, DateTime Write)> _seen = new(StringComparer.OrdinalIgnoreCase);

    public ConvertedFileFinder(string embPath, IEnumerable<string> extraDirs, DateTime since)
    {
        _baseName = Path.GetFileNameWithoutExtension(embPath);
        _dirs = new List<string>();
        var own = Path.GetDirectoryName(Path.GetFullPath(embPath));
        if (own != null) _dirs.Add(own);
        _dirs.AddRange(extraDirs.Where(d => !string.IsNullOrEmpty(d)));
        _dirs = _dirs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _since = since;
    }

    /// <summary>Pastas onde o usuário costuma salvar o arquivo convertido (Downloads e Área de Trabalho).</summary>
    public static IEnumerable<string> CommonFolders()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0) yield return Path.Combine(profile, "Downloads");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (desktop.Length > 0) yield return desktop;
    }

    public IReadOnlyList<string> Folders => _dirs;

    /// <summary>Consulta as pastas. Devolve o caminho do arquivo convertido quando ele estiver pronto, ou null.</summary>
    public string? Poll()
    {
        foreach (var candidate in Candidates())
        {
            var info = new FileInfo(candidate);
            var now = (info.Length, info.LastWriteTimeUtc);
            bool stable = _seen.TryGetValue(candidate, out var before) && before == now && info.Length > 0;
            _seen[candidate] = now;
            if (stable && CanRead(candidate)) return candidate;
        }
        return null;
    }

    private IEnumerable<string> Candidates()
    {
        foreach (var dir in _dirs)
        {
            if (!Directory.Exists(dir)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir).ToList(); }
            catch (Exception) { continue; }              // pasta sem permissão: ignora
            foreach (var f in files.OrderBy(f => ExtensionRank(f)).ThenBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                int rank = ExtensionRank(f);
                if (rank < 0) continue;
                var name = Path.GetFileNameWithoutExtension(f);
                if (!name.StartsWith(_baseName, StringComparison.OrdinalIgnoreCase)) continue;   // "IMPERIO", "IMPERIO (1)", "IMPERIO_final"
                // O arquivo tem que ser novo (salvo depois de começar a espera): o DST antigo ao lado do .emb não conta.
                if (File.GetLastWriteTime(f) < _since.AddSeconds(-2)) continue;
                yield return f;
            }
        }
    }

    private static int ExtensionRank(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return Array.IndexOf(Extensions, ext);
    }

    private static bool CanRead(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return fs.Length > 0;
        }
        catch (IOException) { return false; }           // ainda sendo gravado por outro programa
        catch (UnauthorizedAccessException) { return false; }
    }
}
