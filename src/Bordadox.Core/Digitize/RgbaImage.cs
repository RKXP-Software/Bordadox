namespace Bordadox.Core.Digitize;

/// <summary>Imagem RGBA de 8 bits por canal (4 bytes por pixel, linha a linha). Sem dependência de nenhuma biblioteca gráfica.</summary>
public sealed class RgbaImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }

    public RgbaImage(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Imagem vazia.");
        Width = width;
        Height = height;
        Data = new byte[checked(width * height * 4)];
    }

    public RgbaImage(int width, int height, byte[] data) : this(width, height)
    {
        if (data.Length != Data.Length) throw new ArgumentException("Tamanho dos dados não confere com a imagem.", nameof(data));
        Array.Copy(data, Data, data.Length);
    }

    public int PixelCount => Width * Height;

    public void Set(int x, int y, byte r, byte g, byte b, byte a = 255)
    {
        int i = (y * Width + x) * 4;
        Data[i] = r; Data[i + 1] = g; Data[i + 2] = b; Data[i + 3] = a;
    }

    public (byte R, byte G, byte B, byte A) Get(int x, int y)
    {
        int i = (y * Width + x) * 4;
        return (Data[i], Data[i + 1], Data[i + 2], Data[i + 3]);
    }

    /// <summary>
    /// Reduz (média por área, com alfa pré-multiplicado) ou amplia (bilinear) para o novo tamanho.
    /// A média por área suaviza serrilhado e ruído de JPEG ao reduzir.
    /// </summary>
    public RgbaImage Resize(int newWidth, int newHeight)
    {
        newWidth = Math.Max(1, newWidth);
        newHeight = Math.Max(1, newHeight);
        if (newWidth == Width && newHeight == Height) return new RgbaImage(Width, Height, Data);

        var dst = new RgbaImage(newWidth, newHeight);
        double sx = (double)Width / newWidth, sy = (double)Height / newHeight;

        for (int y = 0; y < newHeight; y++)
        {
            double y0 = y * sy, y1 = (y + 1) * sy;
            if (sy < 1) { y0 = Math.Clamp((y + 0.5) * sy - 0.5, 0, Height - 1); y1 = y0; }
            for (int x = 0; x < newWidth; x++)
            {
                double x0 = x * sx, x1 = (x + 1) * sx;
                if (sx < 1) { x0 = Math.Clamp((x + 0.5) * sx - 0.5, 0, Width - 1); x1 = x0; }
                SamplePixel(x0, x1, y0, y1, dst, x, y);
            }
        }
        return dst;
    }

    private void SamplePixel(double x0, double x1, double y0, double y1, RgbaImage dst, int dx, int dy)
    {
        double r = 0, g = 0, b = 0, a = 0, wsum = 0;

        if (x1 - x0 < 1e-9 || y1 - y0 < 1e-9)
        {
            // ampliação: bilinear
            int ix = (int)Math.Floor(x0), iy = (int)Math.Floor(y0);
            double fx = x0 - ix, fy = y0 - iy;
            for (int j = 0; j <= 1; j++)
                for (int i = 0; i <= 1; i++)
                {
                    int px = Math.Min(Width - 1, ix + i), py = Math.Min(Height - 1, iy + j);
                    double w = (i == 0 ? 1 - fx : fx) * (j == 0 ? 1 - fy : fy);
                    Accumulate(px, py, w, ref r, ref g, ref b, ref a);
                    wsum += w;
                }
        }
        else
        {
            int ix0 = (int)Math.Floor(x0), ix1 = Math.Min(Width - 1, (int)Math.Ceiling(x1) - 1);
            int iy0 = (int)Math.Floor(y0), iy1 = Math.Min(Height - 1, (int)Math.Ceiling(y1) - 1);
            for (int py = iy0; py <= iy1; py++)
            {
                double wy = Math.Min(y1, py + 1) - Math.Max(y0, py);
                if (wy <= 0) continue;
                for (int px = ix0; px <= ix1; px++)
                {
                    double wx = Math.Min(x1, px + 1) - Math.Max(x0, px);
                    if (wx <= 0) continue;
                    double w = wx * wy;
                    Accumulate(px, py, w, ref r, ref g, ref b, ref a);
                    wsum += w;
                }
            }
        }

        int o = (dy * dst.Width + dx) * 4;
        if (wsum <= 0 || a <= 0)
        {
            dst.Data[o] = dst.Data[o + 1] = dst.Data[o + 2] = dst.Data[o + 3] = 0;
            return;
        }
        // r,g,b estão pré-multiplicados pelo alfa (0..1 × peso); "a" guarda o alfa em 0..255 × peso. Desfaz.
        double alphaWeight = a / 255.0;
        dst.Data[o] = (byte)Math.Clamp(Math.Round(r / alphaWeight), 0, 255);
        dst.Data[o + 1] = (byte)Math.Clamp(Math.Round(g / alphaWeight), 0, 255);
        dst.Data[o + 2] = (byte)Math.Clamp(Math.Round(b / alphaWeight), 0, 255);
        dst.Data[o + 3] = (byte)Math.Clamp(Math.Round(a / wsum), 0, 255);
    }

    private void Accumulate(int x, int y, double w, ref double r, ref double g, ref double b, ref double a)
    {
        int i = (y * Width + x) * 4;
        double alpha = Data[i + 3] / 255.0 * w;
        r += Data[i] * alpha;
        g += Data[i + 1] * alpha;
        b += Data[i + 2] * alpha;
        a += alpha * 255.0;
    }

    /// <summary>Cópia do retângulo indicado (limitado à imagem).</summary>
    public RgbaImage Crop(int x, int y, int width, int height)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        width = Math.Clamp(width, 1, Width - x);
        height = Math.Clamp(height, 1, Height - y);
        var dst = new RgbaImage(width, height);
        for (int row = 0; row < height; row++)
            Buffer.BlockCopy(Data, ((y + row) * Width + x) * 4, dst.Data, row * width * 4, width * 4);
        return dst;
    }

    /// <summary>Reduz para que o maior lado tenha no máximo <paramref name="maxSide"/> pixels (não amplia).</summary>
    public RgbaImage Limit(int maxSide)
    {
        int longSide = Math.Max(Width, Height);
        if (longSide <= maxSide) return this;
        double f = (double)maxSide / longSide;
        return Resize((int)Math.Round(Width * f), (int)Math.Round(Height * f));
    }
}
