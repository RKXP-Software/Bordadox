using System.Diagnostics;
using System.IO;
using Bordadox.App.Digitize;
using Bordadox.Core.Design;
using Bordadox.Core.Digitize;
using Xunit.Abstractions;

namespace Bordadox.App.Tests;

/// <summary>Digitaliza as imagens reais da pasta "amostras" (logos em JPG e um SVG) e confere o resultado.</summary>
public class RealImageTests(ITestOutputHelper output)
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

    public static string OutDir
    {
        get
        {
            var d = Path.Combine(Path.GetTempPath(), "bordadox-digitize");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    /// <summary>
    /// Imagens em amostras/. Os logos de terceiros não vão para o repositório; num clone novo não há nenhuma,
    /// então devolve uma linha vazia e os testes terminam logo (em vez de falhar por "sem dados").
    /// </summary>
    public static IEnumerable<object[]> Files()
    {
        var found = Directory.Exists(SamplesDir)
            ? Directory.EnumerateFiles(SamplesDir).Where(ImageLoader.IsImageFile).Select(f => new object[] { Path.GetFileName(f) }).ToList()
            : new List<object[]>();
        return found.Count > 0 ? found : new List<object[]> { new object[] { "" } };
    }

    /// <summary>O SVG é desenhado pelo WPF, que exige thread STA.</summary>
    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var t = new Thread(() => { try { result = work(); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw new Exception("falha na thread STA", error);
        return result;
    }

    // Imagens que ainda dependem de recursos não prontos (linhas finas → ponto corrido). Só conferimos que não travam.
    private static readonly HashSet<string> NeedsThinLines = new() { };

    [Theory]
    [MemberData(nameof(Files))]
    public void RealImage_AnalyzesAndDigitizes(string file)
    {
        if (file.Length == 0) return; // sem imagens de exemplo nesta máquina
        var path = Path.Combine(SamplesDir, file);
        var loaded = OnSta(() => ImageLoader.Load(path));
        var analysis = ImageAnalyzer.Analyze(loaded.Image);
        output.WriteLine($"{file}: {loaded.Image.Width}x{loaded.Image.Height}px  tipo={analysis.Kind}  cores={analysis.DistinctColors}  " +
                         $"textura={analysis.TextureFraction:0.000}  erro={analysis.QuantizationError:0.00}  transparencia={analysis.HasTransparency}");
        Assert.False(analysis.NeedsConfirmation, "logo não pode ser tratado como foto");
        if (NeedsThinLines.Contains(file)) output.WriteLine("   (logo de linha fina: depende do recurso de traços finos, ainda em desenvolvimento)");

        var sw = Stopwatch.StartNew();
        var result = AutoDigitizer.Run(loaded.Image, new DigitizeOptions { WidthMm = 100 });
        var tDigitize = sw.Elapsed;

        var design = new EmbDesign { HoopName = "Médio 130 × 180 mm" };
        design.Objects.AddRange(result.Objects);
        sw.Restart();
        var pattern = design.BuildPattern("teste");
        var tStitch = sw.Elapsed;

        output.WriteLine($"   -> {result.WidthMm:0.#} x {result.HeightMm:0.#} mm, {result.Layers.Count} cores, {result.Objects.Count} objetos, " +
                         $"{pattern.StitchCount:N0} pontos, {pattern.TrimCount} cortes (ignorados: {result.SkippedTiny} pequenos, {result.SkippedThin} finos)");
        output.WriteLine($"   -> digitalizar {tDigitize.TotalSeconds:0.0}s, gerar pontos {tStitch.TotalSeconds:0.0}s");
        foreach (var l in result.Layers)
            output.WriteLine($"      {l.Thread.Name,-22} {l.ObjectCount,3} objetos  {l.AreaMm2,7:0} mm²");

        design.Save(Path.Combine(OutDir, Path.GetFileNameWithoutExtension(file) + ".bdx"));
        if (NeedsThinLines.Contains(file)) return; // logo de linha fina: só conferimos que não trava
        Assert.NotEmpty(result.Objects);
        Assert.True(tDigitize < TimeSpan.FromSeconds(30), $"digitalizar demorou {tDigitize}");
        Assert.InRange(pattern.StitchCount, 500, 150_000);

        // O desenho (sem a margem da imagem) tem a largura pedida, e a prévia usa os mesmos rótulos da versão final.
        var b = design.Bounds();
        Assert.InRange(b.Width, 90, 101);   // a largura vale para o desenho (a folga do recorte é de ~1,5% de cada lado)
        var prep = AutoDigitizer.Prepare(loaded.Image, new DigitizeOptions { WidthMm = 100 });
        Assert.True(prep.Labels.Where(l => l >= 0).Distinct().Count() >= result.Layers.Count);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void RealImage_FullFlow_AddToEditor_Undo_ExportPes(string file)
    {
        if (file.Length == 0 || NeedsThinLines.Contains(file)) return;
        var loaded = OnSta(() => ImageLoader.Load(Path.Combine(SamplesDir, file)));
        var result = AutoDigitizer.Run(loaded.Image, new DigitizeOptions { WidthMm = 80 });

        var editor = new Bordadox.App.Editor.EditorController();
        editor.LoadDesign(new EmbDesign(), null);
        editor.AddObjects(result.Objects);
        Assert.Equal(result.Objects.Count, editor.Design.Objects.Count);
        Assert.Equal(result.Objects.Count, editor.Selection.Count);   // tudo selecionado: dá para mover o logo inteiro
        Assert.True(editor.Pattern.StitchCount > 500);

        // Move o desenho todo e confere que continua no lugar certo.
        var before = editor.Design.Bounds();
        editor.Nudge(5, -3);
        var after = editor.Design.Bounds();
        Assert.Equal(before.MinX + 5, after.MinX, 1);
        Assert.Equal(before.MinY - 3, after.MinY, 1);

        // Exporta para a máquina e lê de volta.
        var pattern = editor.Design.BuildMachinePattern("teste");
        using var ms = new MemoryStream();
        new Bordadox.Core.Formats.PesFormat().Write(pattern, ms);
        ms.Position = 0;
        var back = new Bordadox.Core.Formats.PesFormat().Read(ms);
        Assert.Equal(pattern.StitchCount, back.StitchCount);
        Assert.Equal(result.Layers.Count, back.Threads.Count);

        // Desfaz o movimento e depois a digitalização inteira, num passo só.
        editor.Undo();
        Assert.Equal(before.MinX, editor.Design.Bounds().MinX, 3);
        editor.Undo();
        Assert.Empty(editor.Design.Objects);
    }
}
