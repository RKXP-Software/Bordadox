using System.Globalization;
using System.IO;
using System.Text.Json;
using Bordadox.Core.Design;

namespace Bordadox.App.Editor;

/// <summary>O que o usuário informa ao criar ou editar um texto.</summary>
public sealed record TextSpec(string Text, string FontFamily, double HeightMm, bool Bold, bool Italic)
{
    public const double MinHeightMm = 2;
    public const double MaxHeightMm = 100;

    /// <summary>Último texto usado (fonte, altura, estilo), para repetir sem digitar tudo de novo.</summary>
    public static TextSpec FromPreferences() =>
        new("", UserPrefs.Current.LastFont, UserPrefs.Current.LastHeight, UserPrefs.Current.LastBold, UserPrefs.Current.LastItalic);

    public static TextSpec From(TextObject t) => new(t.Text, t.FontFamily, t.HeightMm, t.Bold, t.Italic);

    public void Apply(TextObject t)
    {
        t.Text = Text;
        t.FontFamily = FontFamily;
        t.HeightMm = HeightMm;
        t.Bold = Bold;
        t.Italic = Italic;
    }

    public bool Matches(TextObject t) =>
        t.Text == Text && t.FontFamily == FontFamily && Math.Abs(t.HeightMm - HeightMm) < 1e-9 && t.Bold == Bold && t.Italic == Italic;

    /// <summary>Lê a altura digitada (aceita vírgula ou ponto) e confere se está no intervalo permitido.</summary>
    public static bool TryParseHeight(string? s, out double height)
    {
        height = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();
        bool ok = double.TryParse(s, NumberStyles.Float, new CultureInfo("pt-BR"), out height) ||
                  double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out height);
        return ok && height >= MinHeightMm && height <= MaxHeightMm;
    }
}

/// <summary>Preferências do usuário guardadas em %APPDATA%\Bordadox\prefs.json.</summary>
public sealed class UserPrefs
{
    public string LastFont { get; set; } = "Arial";
    public double LastHeight { get; set; } = 8;
    public bool LastBold { get; set; }
    public bool LastItalic { get; set; }
    public bool ShowRuler { get; set; }

    /// <summary>Local do arquivo; os testes trocam por uma pasta temporária.</summary>
    public static string FilePath { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bordadox", "prefs.json");

    private static UserPrefs? _current;
    public static UserPrefs Current => _current ??= Load();

    public static void Reset() => _current = null;

    private static UserPrefs Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var prefs = JsonSerializer.Deserialize<UserPrefs>(File.ReadAllText(FilePath));
                if (prefs != null && TextSpec.TryParseHeight(prefs.LastHeight.ToString(CultureInfo.InvariantCulture), out _)) return prefs;
            }
        }
        catch (Exception) { /* arquivo ausente ou corrompido: usa o padrão */ }
        return new UserPrefs();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { /* preferências são só um conforto: falha de gravação não atrapalha */ }
    }
}
