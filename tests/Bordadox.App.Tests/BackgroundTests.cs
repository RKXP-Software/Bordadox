using Bordadox.Core.Design;

namespace Bordadox.App.Tests;

public class BackgroundTests
{
    [Fact]
    public void BackgroundImage_SurvivesProjectRoundTrip_AndKeepsAspect()
    {
        var design = new EmbDesign
        {
            Background = new BackgroundImage { Data = new byte[] { 1, 2, 3 }, FileName = "x.png", Aspect = 0.75, WidthMm = 80 },
        };
        Assert.Equal(60, design.Background.HeightMm, 3);

        var back = EmbDesign.FromJson(design.ToJson());
        Assert.Equal(new byte[] { 1, 2, 3 }, back.Background!.Data);
        Assert.Equal(80, back.Background.WidthMm);
        Assert.Equal(60, back.Background.HeightMm, 3);

        back.Background.WidthMm = 40; // altura acompanha
        Assert.Equal(30, back.Background.HeightMm, 3);
    }
}
