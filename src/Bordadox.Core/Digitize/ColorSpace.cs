namespace Bordadox.Core.Digitize;

/// <summary>Cor no espaço CIE L*a*b*, onde a distância euclidiana se aproxima da diferença percebida.</summary>
public readonly record struct Lab(float L, float A, float B)
{
    public static float DeltaE(in Lab p, in Lab q)
    {
        float dl = p.L - q.L, da = p.A - q.A, db = p.B - q.B;
        return MathF.Sqrt(dl * dl + da * da + db * db);
    }

    public static float DistSq(in Lab p, in Lab q)
    {
        float dl = p.L - q.L, da = p.A - q.A, db = p.B - q.B;
        return dl * dl + da * da + db * db;
    }
}

public static class ColorSpace
{
    private static readonly float[] Linear = BuildLinear();

    private static float[] BuildLinear()
    {
        var t = new float[256];
        for (int i = 0; i < 256; i++)
        {
            double c = i / 255.0;
            t[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }
        return t;
    }

    public static Lab ToLab(byte r, byte g, byte b)
    {
        float lr = Linear[r], lg = Linear[g], lb = Linear[b];
        // sRGB → XYZ (D65)
        float x = 0.4124564f * lr + 0.3575761f * lg + 0.1804375f * lb;
        float y = 0.2126729f * lr + 0.7151522f * lg + 0.0721750f * lb;
        float z = 0.0193339f * lr + 0.1191920f * lg + 0.9503041f * lb;
        float fx = F(x / 0.95047f), fy = F(y), fz = F(z / 1.08883f);
        return new Lab(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    private static float F(float t) => t > 0.008856f ? MathF.Cbrt(t) : 7.787f * t + 16f / 116f;

    public static (byte R, byte G, byte B) ToRgb(Lab lab)
    {
        float fy = (lab.L + 16) / 116f, fx = fy + lab.A / 500f, fz = fy - lab.B / 200f;
        float x = 0.95047f * Finv(fx), y = Finv(fy), z = 1.08883f * Finv(fz);
        float r = 3.2404542f * x - 1.5371385f * y - 0.4985314f * z;
        float g = -0.9692660f * x + 1.8760108f * y + 0.0415560f * z;
        float b = 0.0556434f * x - 0.2040259f * y + 1.0572252f * z;
        return (Gamma(r), Gamma(g), Gamma(b));
    }

    private static float Finv(float t) => t * t * t > 0.008856f ? t * t * t : (t - 16f / 116f) / 7.787f;

    private static byte Gamma(float c)
    {
        c = Math.Clamp(c, 0f, 1f);
        double v = c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
        return (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
    }
}
