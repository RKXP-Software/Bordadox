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

public static class DeltaE2000
{
    /// <summary>Diferença de cor CIEDE2000: bem mais fiel ao olho que a distância euclidiana em Lab, principalmente em amarelos e dourados.</summary>
    public static double Between(in Lab p, in Lab q)
    {
        double l1 = p.L, a1 = p.A, b1 = p.B, l2 = q.L, a2 = q.A, b2 = q.B;
        double c1 = Math.Sqrt(a1 * a1 + b1 * b1), c2 = Math.Sqrt(a2 * a2 + b2 * b2);
        double cm = (c1 + c2) / 2, cm7 = Math.Pow(cm, 7);
        double g = 0.5 * (1 - Math.Sqrt(cm7 / (cm7 + 6103515625.0))); // 25^7
        double a1p = (1 + g) * a1, a2p = (1 + g) * a2;
        double c1p = Math.Sqrt(a1p * a1p + b1 * b1), c2p = Math.Sqrt(a2p * a2p + b2 * b2);
        double h1p = Hue(b1, a1p), h2p = Hue(b2, a2p);

        double dL = l2 - l1, dC = c2p - c1p, dh = 0;
        if (c1p * c2p != 0)
        {
            dh = h2p - h1p;
            if (dh > 180) dh -= 360; else if (dh < -180) dh += 360;
        }
        double dH = 2 * Math.Sqrt(c1p * c2p) * Math.Sin(dh * Math.PI / 360);

        double lm = (l1 + l2) / 2, cmp = (c1p + c2p) / 2, hm = h1p + h2p;
        if (c1p * c2p == 0) hm = h1p + h2p;
        else if (Math.Abs(h1p - h2p) <= 180) hm /= 2;
        else hm = (hm + (hm < 360 ? 360 : -360)) / 2;

        double t = 1 - 0.17 * Math.Cos(Rad(hm - 30)) + 0.24 * Math.Cos(Rad(2 * hm)) + 0.32 * Math.Cos(Rad(3 * hm + 6)) - 0.20 * Math.Cos(Rad(4 * hm - 63));
        double sl = 1 + 0.015 * (lm - 50) * (lm - 50) / Math.Sqrt(20 + (lm - 50) * (lm - 50));
        double sc = 1 + 0.045 * cmp, sh = 1 + 0.015 * cmp * t;
        double dTheta = 30 * Math.Exp(-Math.Pow((hm - 275) / 25, 2));
        double cmp7 = Math.Pow(cmp, 7);
        double rc = 2 * Math.Sqrt(cmp7 / (cmp7 + 6103515625.0));
        double rt = -Math.Sin(Rad(2 * dTheta)) * rc;

        double x = dL / sl, y = dC / sc, z = dH / sh;
        return Math.Sqrt(x * x + y * y + z * z + rt * y * z);
    }

    private static double Hue(double b, double a)
    {
        if (a == 0 && b == 0) return 0;
        double h = Math.Atan2(b, a) * 180 / Math.PI;
        return h < 0 ? h + 360 : h;
    }

    private static double Rad(double deg) => deg * Math.PI / 180;
}
