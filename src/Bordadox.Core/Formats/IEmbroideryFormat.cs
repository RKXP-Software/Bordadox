using Bordadox.Core.Stitches;

namespace Bordadox.Core.Formats;

public interface IEmbroideryFormat
{
    string Description { get; }
    string Extension { get; }
    bool CanRead { get; }
    bool CanWrite { get; }
    StitchPattern Read(Stream stream);
    void Write(StitchPattern pattern, Stream stream);
}

public static class EmbroideryFormats
{
    public static readonly IReadOnlyList<IEmbroideryFormat> All = new IEmbroideryFormat[]
    {
        new PesFormat(),
        new DstFormat(),
    };

    public static IEmbroideryFormat? ForFile(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return All.FirstOrDefault(f => f.Extension == ext);
    }

    public static StitchPattern ReadFile(string path)
    {
        var format = ForFile(path) ?? throw new NotSupportedException($"Formato não suportado: {Path.GetExtension(path)}");
        using var fs = File.OpenRead(path);
        var p = format.Read(fs);
        p.Name = Path.GetFileNameWithoutExtension(path);
        p.FixThreadCount();
        return p;
    }

    public static void WriteFile(StitchPattern pattern, string path)
    {
        var format = ForFile(path) ?? throw new NotSupportedException($"Formato não suportado: {Path.GetExtension(path)}");
        using var fs = File.Create(path);
        format.Write(pattern, fs);
    }
}
