namespace Bordadox.Core.Geometry;

/// <summary>Ponto/vetor 2D em milímetros. Y cresce para baixo (mesma orientação da tela e do formato PES).</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);
    public static Vec2 operator /(Vec2 a, double s) => new(a.X / s, a.Y / s);

    public double Length => Math.Sqrt(X * X + Y * Y);
    public double LengthSquared => X * X + Y * Y;

    public Vec2 Normalized()
    {
        var len = Length;
        return len < 1e-12 ? Zero : new Vec2(X / len, Y / len);
    }

    /// <summary>Perpendicular (rotação de 90°).</summary>
    public Vec2 Perp() => new(-Y, X);

    public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
    public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;
    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    public Vec2 Rotate(double radians)
    {
        var c = Math.Cos(radians);
        var s = Math.Sin(radians);
        return new Vec2(X * c - Y * s, X * s + Y * c);
    }

    public Vec2 RotateAround(Vec2 center, double radians) => (this - center).Rotate(radians) + center;

    public override string ToString() => $"({X:0.##}; {Y:0.##})";
}
