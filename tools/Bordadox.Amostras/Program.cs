// Gera arquivos de teste para validar os formatos na máquina (Brother NQ1400E).
// Uso: dotnet run --project tools/Bordadox.Amostras -- [pasta de saída]
using Bordadox.Core.Design;
using Bordadox.Core.Formats;
using Bordadox.Core.Geometry;
using Bordadox.Core.Machine;
using Bordadox.Core.Stitches;

var outDir = args.Length > 0 ? args[0] : Path.Combine(Directory.GetCurrentDirectory(), "amostras");
Directory.CreateDirectory(outDir);

Save(TestSampler(), "teste_nq1400e");
Save(HoopOutline(150, 250), "contorno_150x250");

var demo = DemoDesign();
demo.Save(Path.Combine(outDir, "demo_editor.bdx"));
Save(demo.BuildPattern("DEMO"), "demo_editor");

// Projeto com todos os tipos de objeto do editor (cabe no bastidor 100 × 100).
static EmbDesign DemoDesign()
{
    PathNode C(double x, double y) => new(new Vec2(x, y));
    PathNode Cv(double x, double y) => new(new Vec2(x, y), true);

    var d = new EmbDesign { HoopName = Hoops.NQ1400E[0].Name };
    // Círculo (nós de curva) com furo quadrado, tatami 30°.
    d.Objects.Add(new FillObject
    {
        Name = "Círculo com furo",
        Color = BrotherPalette.Colors[32],
        Angle = 30,
        Outline = { Cv(0, -40), Cv(20, -20), Cv(0, 0), Cv(-20, -20) },
        Holes = { new() { C(-6, -26), C(6, -26), C(6, -14), C(-6, -14) } },
    });
    // Folha em satin fill.
    d.Objects.Add(new FillObject
    {
        Name = "Folha",
        Color = BrotherPalette.Colors[37],
        FillType = FillType.Satin,
        Angle = 70,
        Outline = { C(25, -10), Cv(38, -24), C(45, -40), Cv(30, -30) },
    });
    // Coluna satin curva (S).
    d.Objects.Add(new SatinObject
    {
        Name = "Coluna S",
        Color = BrotherPalette.Colors[5],
        Nodes =
        {
            C(-40, 10), C(-40, 15),
            Cv(-20, 5), Cv(-20, 11),
            Cv(0, 20), Cv(0, 26),
            Cv(20, 10), Cv(20, 16),
            C(40, 20), C(40, 25),
        },
    });
    // Contorno em ponto triplo.
    d.Objects.Add(new RunObject
    {
        Name = "Moldura",
        Color = BrotherPalette.Colors[20],
        Triple = true,
        Closed = true,
        Nodes = { C(-45, -45), C(45, -45), C(45, 40), Cv(0, 46), C(-45, 40) },
    });
    return d;
}

void Save(StitchPattern p, string name)
{
    foreach (var ext in new[] { "pes", "dst" })
    {
        var path = Path.Combine(outDir, $"{name}.{ext}");
        EmbroideryFormats.WriteFile(p, path);
        Console.WriteLine($"{path}  ({p.StitchCount} pontos, {p.Threads.Count} cores)");
    }
}

// Amostra 60 × 60 mm em 3 cores: contorno em ponto corrido, faixa satin e retângulo tatami.
// Cabe no bastidor de 100 × 100 mm. Unidades: 0,1 mm.
static StitchPattern TestSampler()
{
    var p = new StitchPattern { Name = "TESTE" };
    p.Threads.Add(BrotherPalette.Colors[5]);  // vermelho
    p.Threads.Add(BrotherPalette.Colors[2]);  // azul
    p.Threads.Add(BrotherPalette.Colors[37]); // verde folha

    // 1) Contorno do quadrado, ponto corrido de 2,5 mm.
    var corners = new[] { (-300, -300), (300, -300), (300, 300), (-300, 300), (-300, -300) };
    p.Add(-300, -300, StitchCommand.Jump);
    TieIn(p, -300, -300, 1, 0);
    for (int i = 1; i < corners.Length; i++) Line(p, corners[i - 1], corners[i], 25);
    TieOff(p, -300, -300, 0, 1);
    p.Add(-300, -300, StitchCommand.Trim);

    // 2) Faixa satin 50 × 4 mm, densidade 0,4 mm.
    p.Add(-300, -300, StitchCommand.ColorChange);
    p.Add(-250, 180, StitchCommand.Jump);
    TieIn(p, -250, 180, 1, 0);
    for (int x = -250, i = 0; x <= 250; x += 2, i++) p.Add(x, i % 2 == 0 ? 180 : 220, StitchCommand.Stitch);
    TieOff(p, 250, 220, -1, 0);
    p.Add(250, 220, StitchCommand.Trim);

    // 3) Retângulo tatami 30 × 20 mm: linhas a cada 0,4 mm, ponto de 3 mm, deslocamento de 1/3 por linha.
    p.Add(250, 220, StitchCommand.ColorChange);
    p.Add(-150, -200, StitchCommand.Jump);
    TieIn(p, -150, -200, 1, 0);
    int row = 0;
    for (int y = -200; y <= 0; y += 4, row++)
    {
        var xs = new List<int> { -150 };
        int offset = row % 3 * 10;
        for (int x = -150 + offset + 30; x < 150 - 8; x += 30) xs.Add(x);
        xs.Add(150);
        if (row % 2 == 1) xs.Reverse();
        foreach (var x in xs) p.Add(x, y, StitchCommand.Stitch);
    }
    var last = p.Stitches[^1];
    TieOff(p, last.X, last.Y, 0, -1);
    p.Add(last.X, last.Y, StitchCommand.Trim);
    p.Add(last.X, last.Y, StitchCommand.End);
    return p;
}

// Retângulo para conferir o alinhamento no bastidor grande (160 × 260 mm).
static StitchPattern HoopOutline(int wMm, int hMm)
{
    var p = new StitchPattern { Name = "CONTORNO" };
    p.Threads.Add(BrotherPalette.Colors[20]);
    int w = wMm * 5, h = hMm * 5;
    var corners = new[] { (-w, -h), (w, -h), (w, h), (-w, h), (-w, -h) };
    p.Add(-w, -h, StitchCommand.Jump);
    TieIn(p, -w, -h, 1, 0);
    for (int i = 1; i < corners.Length; i++) Line(p, corners[i - 1], corners[i], 30);
    // Cruz central de 20 mm.
    p.Add(-w, -h, StitchCommand.Trim);
    p.Add(-100, 0, StitchCommand.Jump);
    Line(p, (-100, 0), (100, 0), 30);
    p.Add(100, 0, StitchCommand.Trim);
    p.Add(0, -100, StitchCommand.Jump);
    Line(p, (0, -100), (0, 100), 30);
    p.Add(0, 100, StitchCommand.Trim);
    p.Add(0, 100, StitchCommand.End);
    return p;
}

static void Line(StitchPattern p, (int X, int Y) a, (int X, int Y) b, int step)
{
    double len = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
    int n = Math.Max(1, (int)Math.Ceiling(len / step));
    for (int k = 1; k <= n; k++)
        p.Add(a.X + (b.X - a.X) * k / n, a.Y + (b.Y - a.Y) * k / n, StitchCommand.Stitch);
}

// Arremates: pequenos pontos de vai-e-vem (0,6 mm) para a linha não soltar.
static void TieIn(StitchPattern p, int x, int y, int dx, int dy)
{
    for (int i = 0; i < 2; i++)
    {
        p.Add(x + dx * 6, y + dy * 6, StitchCommand.Stitch);
        p.Add(x, y, StitchCommand.Stitch);
    }
}

static void TieOff(StitchPattern p, int x, int y, int dx, int dy) => TieIn(p, x, y, dx, dy);
