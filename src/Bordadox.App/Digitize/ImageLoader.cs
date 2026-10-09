using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bordadox.Core.Digitize;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace Bordadox.App.Digitize;

/// <summary>Imagem já convertida para o formato do núcleo, mais o que a janela precisa mostrar.</summary>
public sealed class LoadedImage
{
    public RgbaImage Image { get; init; } = null!;
    public string Name { get; init; } = "";
    public bool IsVector { get; init; }
    /// <summary>Tamanho do arquivo original em pixels (ou unidades, para SVG).</summary>
    public int OriginalWidth { get; init; }
    public int OriginalHeight { get; init; }

}

/// <summary>Lê PNG, JPG, BMP, GIF e SVG. Imagens muito grandes são reduzidas (o bordado não precisa de mais que isso).</summary>
public static class ImageLoader
{
    private static readonly string[] RasterExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };
    public const string Filter = "Imagens e vetores|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.svg|Todos os arquivos|*.*";

    /// <summary>Imagem do núcleo como bitmap do WPF (congelado, pronto para qualquer thread).</summary>
    public static BitmapSource ToBitmap(RgbaImage img)
    {
        var bgra = new byte[img.Data.Length];
        for (int i = 0; i < img.Data.Length; i += 4)
        {
            bgra[i] = img.Data[i + 2]; bgra[i + 1] = img.Data[i + 1]; bgra[i + 2] = img.Data[i]; bgra[i + 3] = img.Data[i + 3];
        }
        var bmp = BitmapSource.Create(img.Width, img.Height, 96, 96, PixelFormats.Bgra32, null, bgra, img.Width * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>PNG reduzido (maior lado ≤ <paramref name="maxSide"/>), para guardar como imagem de referência no projeto.</summary>
    public static byte[] EncodePng(RgbaImage img, int maxSide = 1200)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(ToBitmap(img.Limit(maxSide))));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    public static bool IsSvg(string path) => Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase);

    public static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".svg" || RasterExtensions.Contains(ext);
    }

    /// <summary>Precisa rodar na thread da interface (o WPF desenha o SVG).</summary>
    public static LoadedImage Load(string path, int maxSide = 2400)
    {
        path = Path.GetFullPath(path); // o decodificador do WPF exige caminho completo
        if (!File.Exists(path)) throw new FileNotFoundException("Arquivo não encontrado.", path);
        return IsSvg(path) ? LoadSvg(path, maxSide) : LoadRaster(path, maxSide);
    }

    private static LoadedImage LoadRaster(string path, int maxSide)
    {
        BitmapSource frame = BitmapDecoder.Create(new Uri(path, UriKind.Absolute), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        int ow = frame.PixelWidth, oh = frame.PixelHeight;
        int longSide = Math.Max(ow, oh);
        if (longSide > maxSide)
        {
            double f = (double)maxSide / longSide;
            frame = new TransformedBitmap(frame, new ScaleTransform(f, f));
        }
        return new LoadedImage { Image = ToRgba(frame), Name = Path.GetFileNameWithoutExtension(path), OriginalWidth = ow, OriginalHeight = oh };
    }

    private static LoadedImage LoadSvg(string path, int maxSide)
    {
        var settings = new WpfDrawingSettings { IncludeRuntime = false, TextAsGeometry = true, OptimizePath = true };
        using var reader = new FileSvgReader(settings);
        var drawing = reader.Read(path) ?? throw new InvalidDataException("Não foi possível ler o SVG.");

        var bounds = drawing.Bounds;
        if (bounds.IsEmpty || bounds.Width < 1e-6 || bounds.Height < 1e-6) throw new InvalidDataException("O SVG está vazio.");

        double scale = maxSide / Math.Max(bounds.Width, bounds.Height);
        int w = Math.Max(1, (int)Math.Round(bounds.Width * scale)), h = Math.Max(1, (int)Math.Round(bounds.Height * scale));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
            dc.DrawDrawing(drawing);
        }
        var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        return new LoadedImage
        {
            Image = ToRgba(target),
            Name = Path.GetFileNameWithoutExtension(path),
            IsVector = true,
            OriginalWidth = (int)Math.Round(bounds.Width),
            OriginalHeight = (int)Math.Round(bounds.Height),
        };
    }

    private static RgbaImage ToRgba(BitmapSource source)
    {
        // Bgra32 (sem pré-multiplicar): o WPF converte de qualquer formato de origem.
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int w = converted.PixelWidth, h = converted.PixelHeight;
        var bgra = new byte[w * h * 4];
        converted.CopyPixels(bgra, w * 4, 0);
        var rgba = new byte[bgra.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            rgba[i] = bgra[i + 2]; rgba[i + 1] = bgra[i + 1]; rgba[i + 2] = bgra[i]; rgba[i + 3] = bgra[i + 3];
        }
        return new RgbaImage(w, h, rgba);
    }
}
