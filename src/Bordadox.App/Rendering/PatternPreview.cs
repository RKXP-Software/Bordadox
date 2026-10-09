using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bordadox.Core.Stitches;

namespace Bordadox.App.Rendering;

/// <summary>Desenha o bordado numa imagem (cada cor na sua linha), para a ficha de produção. Precisa rodar numa thread de interface (STA).</summary>
public static class PatternPreview
{
    public static byte[] RenderPng(StitchPattern pattern, int maxPixels = 720)
    {
        pattern.FixThreadCount();
        var b = pattern.Bounds();
        double wMm = Math.Max(10, (b.MaxX - b.MinX) / 10.0), hMm = Math.Max(10, (b.MaxY - b.MinY) / 10.0);
        const double padMm = 4;
        double scale = Math.Clamp(maxPixels / Math.Max(wMm + 2 * padMm, hMm + 2 * padMm), 1, 30);   // pixels por mm
        int w = (int)Math.Ceiling((wMm + 2 * padMm) * scale), h = (int)Math.Ceiling((hMm + 2 * padMm) * scale);
        double ox = b.MinX / 10.0 - padMm, oy = b.MinY / 10.0 - padMm;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
            foreach (var (colorIndex, runs) in pattern.ColorBlocks())
            {
                var t = pattern.Threads[Math.Min(colorIndex, pattern.Threads.Count - 1)];
                var brush = new SolidColorBrush(Color.FromRgb(t.R, t.G, t.B));
                brush.Freeze();
                var pen = new Pen(brush, Math.Max(1.0, 0.45 * scale)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                pen.Freeze();
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    foreach (var run in runs)
                    {
                        if (run.Count < 2) continue;
                        ctx.BeginFigure(new Point((run[0].X - ox) * scale, (run[0].Y - oy) * scale), false, false);
                        for (int i = 1; i < run.Count; i++) ctx.LineTo(new Point((run[i].X - ox) * scale, (run[i].Y - oy) * scale), true, false);
                    }
                }
                geometry.Freeze();
                dc.DrawGeometry(null, pen, geometry);
            }
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
