using Bordadox.App.Text;
using Xunit.Abstractions;

namespace Bordadox.App.Tests;

public class KerningTests(ITestOutputHelper output)
{
    [Fact]
    public void WpfProvider_ReportsKerningForClassicPairs()
    {
        var p = new WpfGlyphProvider();
        foreach (var font in new[] { "Arial", "Times New Roman", "Georgia", "Segoe UI" })
        {
            double av = p.Kerning(font, false, false, 'A', 'V');
            double to = p.Kerning(font, false, false, 'T', 'o');
            double ab = p.Kerning(font, false, false, 'A', 'B');
            output.WriteLine($"{font}: AV={av:0.000} To={to:0.000} AB={ab:0.000}");
        }
        Assert.True(p.Kerning("Arial", false, false, 'A', 'V') < -0.01, "AV deveria aproximar em Arial");
        Assert.Equal(0, p.Kerning("Arial", false, false, 'A', 'B'), 3);
    }
}
