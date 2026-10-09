using Bordadox.Core.Digitize;
using Xunit.Abstractions;

namespace Bordadox.Tests;

public class ImageAnalyzerTests(ITestOutputHelper output)
{
    private static RgbaImage Logo(bool noisy)
    {
        var img = Synth.Blank(600, 400);
        Synth.Paint(img, Synth.Rect(60, 60, 300, 340), 58, 58, 60);
        Synth.Paint(img, Synth.Rect(320, 60, 520, 200), 192, 142, 46);
        Synth.Paint(img, Synth.Disc(420, 280, 70), 192, 142, 46);
        Synth.Paint(img, Synth.Ring(420, 280, 30, 45), 255, 255, 255);
        if (noisy)
        {
            var rng = new Random(3);
            for (int i = 0; i < img.Data.Length; i += 4)
                for (int c = 0; c < 3; c++) img.Data[i + c] = (byte)Math.Clamp(img.Data[i + c] + rng.Next(-4, 5), 0, 255);
        }
        return img;
    }

    private static RgbaImage Gradient()
    {
        var img = new RgbaImage(600, 400);
        for (int y = 0; y < 400; y++)
            for (int x = 0; x < 600; x++)
            {
                double t = x / 599.0, u = y / 399.0;
                img.Set(x, y, (byte)(20 + 220 * t), (byte)(60 + 120 * u), (byte)(200 - 150 * t));
            }
        return img;
    }

    /// <summary>Imagem "de foto": manchas suaves de cor em várias escalas mais granulação.</summary>
    private static RgbaImage PhotoLike(int seed)
    {
        var rng = new Random(seed);
        var img = new RgbaImage(600, 400);
        var phase = Enumerable.Range(0, 18).Select(_ => rng.NextDouble() * 6.28).ToArray();
        for (int y = 0; y < 400; y++)
            for (int x = 0; x < 600; x++)
            {
                double v(int c) =>
                    128 + 55 * Math.Sin(x / 37.0 + phase[c]) * Math.Cos(y / 29.0 + phase[c + 3]) +
                    35 * Math.Sin((x + y) / 11.0 + phase[c + 6]) + 22 * Math.Sin(x / 5.0 + y / 7.0 + phase[c + 9]) + rng.Next(-9, 10);
                img.Set(x, y, (byte)Math.Clamp(v(0), 0, 255), (byte)Math.Clamp(v(1), 0, 255), (byte)Math.Clamp(v(2), 0, 255));
            }
        return img;
    }

    private void Print(string name, ImageAnalysis a) =>
        output.WriteLine($"{name}: {a.Kind}  cores={a.DistinctColors}  textura={a.TextureFraction:0.000}  erro={a.QuantizationError:0.00}");

    [Fact]
    public void FlatLogo_IsFlat()
    {
        var a = ImageAnalyzer.Analyze(Logo(noisy: false));
        Print("logo", a);
        Assert.Equal(ImageKind.Flat, a.Kind);
        Assert.False(a.NeedsConfirmation);
    }

    [Fact]
    public void LogoWithJpegNoise_IsStillFlat()
    {
        var a = ImageAnalyzer.Analyze(Logo(noisy: true));
        Print("logo com ruído", a);
        Assert.Equal(ImageKind.Flat, a.Kind);
    }

    [Fact]
    public void StrongSmoothGradient_IsGradient_AndAsksForConfirmation()
    {
        var a = ImageAnalyzer.Analyze(Gradient());
        Print("degradê forte", a);
        Assert.Equal(ImageKind.Gradient, a.Kind);   // liso, sem textura: não é foto
        Assert.True(a.NeedsConfirmation);           // mas vira muitas faixas de cor
    }

    [Fact]
    public void MildGradientInsideAFlatLogo_DoesNotAskForConfirmation()
    {
        var img = Logo(noisy: false);
        for (int y = 0; y < 40; y++)                               // faixa superior com um degradê leve
            for (int x = 0; x < 600; x++) img.Set(x, y, (byte)(235 + 20 * x / 600), 240, (byte)(235 - 15 * x / 600));
        var a = ImageAnalyzer.Analyze(img);
        Print("logo com degradê leve", a);
        Assert.NotEqual(ImageKind.Photo, a.Kind);
        Assert.False(a.NeedsConfirmation);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PhotoLike_IsPhotoAndNeedsConfirmation(int seed)
    {
        var a = ImageAnalyzer.Analyze(PhotoLike(seed));
        Print($"foto {seed}", a);
        Assert.Equal(ImageKind.Photo, a.Kind);
        Assert.True(a.NeedsConfirmation);
    }

    [Fact]
    public void TransparentLogo_IsNotConfusedByTransparency()
    {
        var img = Synth.Blank(300, 300, 0, 0, 0, a: 0);
        Synth.Paint(img, Synth.Disc(150, 150, 100), 20, 140, 60);
        var a = ImageAnalyzer.Analyze(img);
        Assert.True(a.HasTransparency);
        Assert.Equal(ImageKind.Flat, a.Kind);
    }

    [Fact]
    public void EmptyImage_DoesNotCrash()
    {
        var a = ImageAnalyzer.Analyze(Synth.Blank(50, 50, 0, 0, 0, a: 0));
        Assert.Equal(ImageKind.Flat, a.Kind);
    }
}
