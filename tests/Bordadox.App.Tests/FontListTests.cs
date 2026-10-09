using System.Diagnostics;
using Bordadox.App.Text;

namespace Bordadox.App.Tests;

public class FontListTests
{
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(120);

    [Fact]
    public void Names_AreReadWithoutOpeningEveryFont_AndSorted()
    {
        var sw = Stopwatch.StartNew();
        var names = FontCatalog.Names;
        sw.Stop();

        Assert.Contains("Arial", names);
        Assert.Equal(names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase), names);
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"leitura dos nomes demorou {sw.Elapsed}");
    }

    [Fact]
    public void AfterScan_SymbolFontsAreHidden_AndEveryListedFontDrawsLetters()
    {
        Assert.True(FontCatalog.ScanCompleted.Wait(ScanTimeout), "a conferência das fontes não terminou");
        var listed = FontCatalog.Search(FontCatalog.Names, "");
        var provider = new WpfGlyphProvider();

        Assert.Contains("Arial", listed);
        Assert.DoesNotContain("Marlett", listed);
        Assert.DoesNotContain("Segoe MDL2 Assets", listed);
        Assert.True(FontCatalog.IsKnownUnusable("Marlett"));
        Assert.All(listed, f => Assert.NotEmpty(provider.GetGlyph(f, false, false, 'A')!.Contours));
    }

    [Fact]
    public void Search_MatchesAllWordsInAnyOrder_IgnoringCase()
    {
        var names = new[] { "Arial", "Arial Black", "Arial Narrow", "Bahnschrift", "Times New Roman", "Comic Sans MS" };
        Assert.Equal(new[] { "Arial Black" }, FontCatalog.Search(names, "arial bla"));
        Assert.Equal(new[] { "Arial Black" }, FontCatalog.Search(names, "BLACK ARIAL"));
        Assert.Equal(new[] { "Times New Roman" }, FontCatalog.Search(names, "new rom"));
        Assert.Empty(FontCatalog.Search(names, "zzz"));
        Assert.Equal(names, FontCatalog.Search(names, "   "));
    }

    [Fact]
    public void Search_ListsNamesStartingWithTheQueryFirst()
    {
        var names = new[] { "Bahnschrift", "Royal Script", "Algerian", "Alef", "Segoe Print", "Agency FB" };
        var result = FontCatalog.Search(names, "al");
        // Os que começam com "al" vêm primeiro; "Royal Script" só contém "al" no meio e vem depois.
        Assert.Equal(new[] { "Algerian", "Alef", "Royal Script" }, result);
    }

    [Fact]
    public void Search_IsFastOnAHugeFontCollection()
    {
        var names = Enumerable.Range(0, 20_000).Select(i => $"Fonte Fake {i:D5} Pro").ToList();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 20; i++) FontCatalog.Search(names, "fake 1999");
        sw.Stop();
        // Só pega lentidão grave (ex.: busca quadrática). Folga grande: com a máquina ocupada, outros testes rodam em paralelo.
        Assert.True(sw.ElapsedMilliseconds < 8000, $"20 buscas em 20 mil fontes levaram {sw.ElapsedMilliseconds} ms");
    }
}
