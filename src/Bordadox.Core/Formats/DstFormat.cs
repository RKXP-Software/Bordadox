using System.Text;
using Bordadox.Core.Stitches;

namespace Bordadox.Core.Formats;

/// <summary>Formato Tajima DST. Não guarda cores; registros de 3 bytes com deslocamento máximo de ±121 (12,1 mm).</summary>
public sealed class DstFormat : IEmbroideryFormat
{
    public string Description => "Tajima DST";
    public string Extension => "dst";
    public bool CanRead => true;
    public bool CanWrite => true;

    private const int MaxMove = 121;

    public StitchPattern Read(Stream stream)
    {
        var pattern = new StitchPattern();
        stream.Seek(512, SeekOrigin.Begin);
        var rec = new byte[3];
        int x = 0, y = 0;
        int jumpRun = 0;
        long trimInsertIndex = -1;

        while (stream.Read(rec, 0, 3) == 3)
        {
            byte b0 = rec[0], b1 = rec[1], b2 = rec[2];
            if ((b2 & 0xF3) == 0xF3) break;

            int dx = 0, dy = 0;
            if ((b0 & 0x01) != 0) dx += 1;
            if ((b0 & 0x02) != 0) dx -= 1;
            if ((b0 & 0x04) != 0) dx += 9;
            if ((b0 & 0x08) != 0) dx -= 9;
            if ((b1 & 0x01) != 0) dx += 3;
            if ((b1 & 0x02) != 0) dx -= 3;
            if ((b1 & 0x04) != 0) dx += 27;
            if ((b1 & 0x08) != 0) dx -= 27;
            if ((b2 & 0x04) != 0) dx += 81;
            if ((b2 & 0x08) != 0) dx -= 81;
            if ((b0 & 0x80) != 0) dy += 1;
            if ((b0 & 0x40) != 0) dy -= 1;
            if ((b0 & 0x20) != 0) dy += 9;
            if ((b0 & 0x10) != 0) dy -= 9;
            if ((b1 & 0x80) != 0) dy += 3;
            if ((b1 & 0x40) != 0) dy -= 3;
            if ((b1 & 0x20) != 0) dy += 27;
            if ((b1 & 0x10) != 0) dy -= 27;
            if ((b2 & 0x20) != 0) dy += 81;
            if ((b2 & 0x10) != 0) dy -= 81;
            x += dx; y -= dy; // DST: Y positivo para cima

            if ((b2 & 0xC3) == 0xC3)
            {
                pattern.Add(x, y, StitchCommand.ColorChange);
                jumpRun = 0;
            }
            else if ((b2 & 0x83) == 0x83)
            {
                if (jumpRun == 0) trimInsertIndex = pattern.Stitches.Count;
                jumpRun++;
                pattern.Add(x, y, StitchCommand.Jump);
                // Convenção Tajima: 3+ saltos seguidos significam corte de linha.
                if (jumpRun == 3)
                {
                    var prev = trimInsertIndex > 0 ? pattern.Stitches[(int)trimInsertIndex - 1] : new Stitch(0, 0, StitchCommand.Stitch);
                    pattern.Stitches.Insert((int)trimInsertIndex, new Stitch(prev.X, prev.Y, StitchCommand.Trim));
                }
            }
            else
            {
                pattern.Add(x, y, StitchCommand.Stitch);
                jumpRun = 0;
            }
        }
        pattern.Add(x, y, StitchCommand.End);
        return pattern;
    }

    public void Write(StitchPattern source, Stream stream)
    {
        var pattern = source.Centered().WithMaxMove(MaxMove);
        var b = pattern.Bounds();
        int colorChanges = pattern.Stitches.Count(s => s.Command == StitchCommand.ColorChange);

        var records = new List<byte[]>();
        int x = 0, y = 0;
        foreach (var s in pattern.Stitches)
        {
            switch (s.Command)
            {
                case StitchCommand.Stitch:
                case StitchCommand.Jump:
                    records.Add(Encode(s.X - x, s.Y - y, s.Command == StitchCommand.Jump ? 0x83 : 0x03));
                    x = s.X; y = s.Y;
                    break;
                case StitchCommand.Trim:
                    records.Add(Encode(2, 2, 0x83));
                    records.Add(Encode(-4, -4, 0x83));
                    records.Add(Encode(2, 2, 0x83));
                    break;
                case StitchCommand.ColorChange:
                case StitchCommand.Stop:
                    records.Add(Encode(0, 0, 0xC3));
                    break;
            }
        }
        records.Add(new byte[] { 0x00, 0x00, 0xF3 });

        var name = new string((pattern.Name ?? "").Where(c => c < 128 && !char.IsControl(c)).Take(16).ToArray());
        var header = new StringBuilder();
        header.Append($"LA:{name,-16}\r");
        header.Append($"ST:{records.Count,7}\r");
        header.Append($"CO:{colorChanges,3}\r");
        header.Append($"+X:{Math.Max(0, b.MaxX),5}\r");
        header.Append($"-X:{Math.Max(0, -b.MinX),5}\r");
        header.Append($"+Y:{Math.Max(0, -b.MinY),5}\r");
        header.Append($"-Y:{Math.Max(0, b.MaxY),5}\r");
        header.Append($"AX:+{0,5}\r");
        header.Append($"AY:+{0,5}\r");
        header.Append($"MX:+{0,5}\r");
        header.Append($"MY:+{0,5}\r");
        header.Append("PD:******\r");
        var headerBytes = new byte[512];
        Array.Fill(headerBytes, (byte)0x20);
        var hb = Encoding.ASCII.GetBytes(header.ToString());
        Array.Copy(hb, headerBytes, hb.Length);
        headerBytes[hb.Length] = 0x1A;

        stream.Write(headerBytes);
        foreach (var r in records) stream.Write(r);
    }

    private static byte[] Encode(int dx, int dy, int flags)
    {
        int x = dx, y = -dy;
        byte b0 = 0, b1 = 0, b2 = (byte)flags;
        if (x > 40) { b2 |= 0x04; x -= 81; }
        if (x < -40) { b2 |= 0x08; x += 81; }
        if (x > 13) { b1 |= 0x04; x -= 27; }
        if (x < -13) { b1 |= 0x08; x += 27; }
        if (x > 4) { b0 |= 0x04; x -= 9; }
        if (x < -4) { b0 |= 0x08; x += 9; }
        if (x > 1) { b1 |= 0x01; x -= 3; }
        if (x < -1) { b1 |= 0x02; x += 3; }
        if (x > 0) { b0 |= 0x01; }
        if (x < 0) { b0 |= 0x02; }
        if (y > 40) { b2 |= 0x20; y -= 81; }
        if (y < -40) { b2 |= 0x10; y += 81; }
        if (y > 13) { b1 |= 0x20; y -= 27; }
        if (y < -13) { b1 |= 0x10; y += 27; }
        if (y > 4) { b0 |= 0x20; y -= 9; }
        if (y < -4) { b0 |= 0x10; y += 9; }
        if (y > 1) { b1 |= 0x80; y -= 3; }
        if (y < -1) { b1 |= 0x40; y += 3; }
        if (y > 0) { b0 |= 0x80; }
        if (y < 0) { b0 |= 0x40; }
        return new[] { b0, b1, b2 };
    }
}
