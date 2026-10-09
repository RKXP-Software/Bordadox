using Bordadox.Core.Digitize;

namespace Bordadox.Core.Stitches;

public sealed record ThreadColor(byte R, byte G, byte B, string Name, string Code)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    public int DistanceSquared(ThreadColor o)
    {
        // Distância "redmean": aproximação barata da percepção humana de cor.
        int rm = (R + o.R) / 2;
        int dr = R - o.R, dg = G - o.G, db = B - o.B;
        return (((512 + rm) * dr * dr) >> 8) + 4 * dg * dg + (((767 - rm) * db * db) >> 8);
    }

    public override string ToString() => $"{Code} {Name}";
}

/// <summary>Paleta de linhas embutida no formato PEC das máquinas Brother (índices 1..64).</summary>
public static class BrotherPalette
{
    public static readonly IReadOnlyList<ThreadColor> Colors = new List<ThreadColor>
    {
        new(0, 0, 0, "Desconhecida", "0"),
        new(14, 31, 124, "Azul da Prússia", "1"),
        new(10, 85, 163, "Azul", "2"),
        new(0, 135, 119, "Verde-azulado", "3"),
        new(75, 107, 175, "Azul centáurea", "4"),
        new(237, 23, 31, "Vermelho", "5"),
        new(209, 92, 0, "Marrom avermelhado", "6"),
        new(145, 54, 151, "Magenta", "7"),
        new(228, 154, 203, "Lilás claro", "8"),
        new(145, 95, 172, "Lilás", "9"),
        new(158, 214, 125, "Verde menta", "10"),
        new(232, 169, 0, "Dourado escuro", "11"),
        new(254, 186, 53, "Laranja", "12"),
        new(255, 255, 0, "Amarelo", "13"),
        new(112, 188, 31, "Verde-limão", "14"),
        new(186, 152, 0, "Latão", "15"),
        new(168, 168, 168, "Prata", "16"),
        new(125, 111, 0, "Marrom ruivo", "17"),
        new(255, 255, 179, "Creme", "18"),
        new(79, 85, 86, "Estanho", "19"),
        new(0, 0, 0, "Preto", "20"),
        new(11, 61, 145, "Ultramarino", "21"),
        new(119, 1, 118, "Roxo real", "22"),
        new(41, 49, 51, "Cinza escuro", "23"),
        new(42, 19, 1, "Marrom escuro", "24"),
        new(246, 74, 138, "Rosa intenso", "25"),
        new(178, 118, 36, "Marrom claro", "26"),
        new(252, 187, 197, "Rosa salmão", "27"),
        new(254, 55, 15, "Vermelhão", "28"),
        new(240, 240, 240, "Branco", "29"),
        new(106, 28, 138, "Violeta", "30"),
        new(168, 221, 196, "Verde-mar", "31"),
        new(37, 132, 187, "Azul celeste", "32"),
        new(254, 179, 67, "Abóbora", "33"),
        new(255, 243, 107, "Amarelo creme", "34"),
        new(208, 166, 96, "Cáqui", "35"),
        new(209, 84, 0, "Marrom argila", "36"),
        new(102, 186, 73, "Verde folha", "37"),
        new(19, 74, 70, "Azul pavão", "38"),
        new(135, 135, 135, "Cinza", "39"),
        new(216, 204, 198, "Cinza quente", "40"),
        new(67, 86, 7, "Oliva escuro", "41"),
        new(253, 217, 222, "Rosa pele", "42"),
        new(249, 147, 188, "Rosa", "43"),
        new(0, 56, 34, "Verde profundo", "44"),
        new(178, 175, 212, "Lavanda", "45"),
        new(104, 106, 176, "Violeta glicínia", "46"),
        new(239, 227, 185, "Bege", "47"),
        new(247, 56, 102, "Carmim", "48"),
        new(181, 75, 100, "Vermelho âmbar", "49"),
        new(19, 43, 26, "Verde oliva", "50"),
        new(199, 1, 86, "Fúcsia escuro", "51"),
        new(254, 158, 50, "Tangerina", "52"),
        new(168, 222, 235, "Azul claro", "53"),
        new(0, 103, 62, "Verde esmeralda", "54"),
        new(78, 41, 144, "Roxo", "55"),
        new(47, 126, 32, "Verde musgo", "56"),
        new(255, 204, 204, "Rosa pele claro", "57"),
        new(255, 217, 17, "Ouro colheita", "58"),
        new(9, 91, 166, "Azul elétrico", "59"),
        new(240, 249, 112, "Amarelo limão", "60"),
        new(227, 243, 91, "Verde fresco", "61"),
        new(255, 153, 0, "Laranja vivo", "62"),
        new(255, 240, 141, "Amarelo creme claro", "63"),
        new(255, 200, 200, "Aplique", "64"),
    };

    /// <summary>Índices de cores bem distintas entre si, usadas quando o arquivo não informa as cores (ex.: DST).</summary>
    public static readonly IReadOnlyList<int> ContrastSequence = new[] { 2, 5, 37, 13, 20, 7, 12, 32, 24, 43, 3, 22, 58, 39, 28, 54 };

    private static readonly Lab[] PaletteLab = Colors.Select(c => ColorSpace.ToLab(c.R, c.G, c.B)).ToArray();

    /// <summary>Índice PEC (1..64) da cor mais próxima, medida com CIEDE2000 (percepção), não em RGB.</summary>
    public static int NearestIndex(ThreadColor c)
    {
        var lab = ColorSpace.ToLab(c.R, c.G, c.B);
        int best = 1;
        double bestD = double.MaxValue;
        for (int i = 1; i < Colors.Count; i++)
        {
            var d = DeltaE2000.Between(lab, PaletteLab[i]);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }
}
