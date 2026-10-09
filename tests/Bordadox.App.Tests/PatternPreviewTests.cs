using System.IO;
using System.Windows.Media.Imaging;
using Bordadox.App.Rendering;
using Bordadox.Core.Design;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;

namespace Bordadox.App.Tests;

public class PatternPreviewTests
{
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

    [Fact]
    public void RendersAPngWithTheThreadColors()
    {
        var d = new EmbDesign();
        d.Objects.Add(new FillObject
        {
            Outline = new() { new(new Vec2(0, 0)), new(new Vec2(40, 0)), new(new Vec2(40, 20)), new(new Vec2(0, 20)) },
            Color = BrotherPalette.Colors[5],
        });
        var pattern = d.BuildPattern("t");

        var png = OnSta(() => PatternPreview.RenderPng(pattern, 600));
        Assert.True(png.Length > 1000);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);

        var (w, h, red) = OnSta(() =>
        {
            var frame = BitmapFrame.Create(new MemoryStream(png));
            var conv = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            var pixels = new byte[conv.PixelWidth * conv.PixelHeight * 4];
            conv.CopyPixels(pixels, conv.PixelWidth * 4, 0);
            int reds = 0;
            for (int i = 0; i < pixels.Length; i += 4)
                if (pixels[i + 2] > 180 && pixels[i + 1] < 80 && pixels[i] < 80) reds++;
            return (conv.PixelWidth, conv.PixelHeight, reds);
        });
        Assert.True(w > 300 && h > 100 && w > h, $"{w}x{h}: a prévia segue a proporção do desenho (40 x 20 mm)");
        Assert.True(red > w * h / 10, "o preenchimento vermelho deveria cobrir boa parte da imagem");
    }
}
