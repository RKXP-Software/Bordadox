using Bordadox.Core.Formats;
using Bordadox.Core.Stitches;
using Xunit.Abstractions;

namespace Bordadox.Tests;

/// <summary>Abre todos os arquivos reais da pasta "amostras" e confere se a leitura faz sentido.</summary>
public class SampleFileTests(ITestOutputHelper output)
{
    public static string SamplesDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "amostras"))) dir = dir.Parent;
            return dir == null ? "" : Path.Combine(dir.FullName, "amostras");
        }
    }

    public static IEnumerable<object[]> Files() =>
        Directory.Exists(SamplesDir)
            ? Directory.EnumerateFiles(SamplesDir)
                .Where(f => EmbroideryFormats.ForFile(f) != null)
                .Select(f => new object[] { Path.GetFileName(f) })
            : Array.Empty<object[]>();

    [Theory]
    [MemberData(nameof(Files))]
    public void ReadsSampleFile(string name)
    {
        var p = EmbroideryFormats.ReadFile(Path.Combine(SamplesDir, name));
        var b = p.Bounds();
        double w = (b.MaxX - b.MinX) / 10.0, h = (b.MaxY - b.MinY) / 10.0;
        int colors = 1 + p.Stitches.Count(s => s.Command == StitchCommand.ColorChange);
        output.WriteLine($"{name}: {p.StitchCount} pontos, {colors} cores, {p.TrimCount} cortes, {w:0.0} x {h:0.0} mm, " +
                         $"linhas: {string.Join(", ", p.Threads.Select(t => t.Name))}");

        Assert.True(p.StitchCount > 0);
        Assert.InRange(w, 1, 400);
        Assert.InRange(h, 1, 400);
        Assert.Equal(colors, p.Threads.Count);

        // Ida e volta em PES (formato da máquina) preserva os pontos.
        using var ms = new MemoryStream();
        new PesFormat().Write(p, ms);
        ms.Position = 0;
        var back = new PesFormat().Read(ms);
        Assert.Equal(p.StitchCount, back.StitchCount);
    }
}
