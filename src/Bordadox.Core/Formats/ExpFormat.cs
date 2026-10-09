using Bordadox.Core.Stitches;

namespace Bordadox.Core.Formats;

/// <summary>
/// Formato Melco EXP. Sem cabeçalho e sem cores: pares de bytes (dx, dy) em 0,1 mm, com o Y invertido, e comandos que começam em 0x80
/// (0x80 0x01 = troca de linha, 0x80 0x04 dx dy = salto, 0x80 0x80 0x07 0x00 = corte). Deslocamento máximo de ±127 (12,7 mm) por registro.
/// </summary>
public sealed class ExpFormat : IEmbroideryFormat
{
    public string Description => "Melco EXP";
    public string Extension => "exp";
    public bool CanRead => true;
    public bool CanWrite => true;

    private const int MaxMove = 127;

    public StitchPattern Read(Stream stream)
    {
        var pattern = new StitchPattern();
        int x = 0, y = 0;
        var pair = new byte[2];
        while (stream.Read(pair, 0, 2) == 2)
        {
            if (pair[0] != 0x80)
            {
                x += (sbyte)pair[0];
                y -= (sbyte)pair[1];
                pattern.Add(x, y, StitchCommand.Stitch);
                continue;
            }

            var arg = new byte[2];
            if (stream.Read(arg, 0, 2) != 2) break;
            int dx = (sbyte)arg[0], dy = -(sbyte)arg[1];
            switch (pair[1])
            {
                case 0x80:                                 // corte (os dois bytes seguintes são 0x07 0x00)
                    pattern.Add(x, y, StitchCommand.Trim);
                    break;
                case 0x04:                                 // salto
                case 0x02:                                 // movimento (não deveria existir, mas há arquivos que o usam)
                    x += dx; y += dy;
                    pattern.Add(x, y, StitchCommand.Jump);
                    break;
                case 0x01:                                 // troca de linha, com um movimento opcional depois
                    pattern.Add(x, y, StitchCommand.ColorChange);
                    if (dx != 0 || dy != 0)
                    {
                        x += dx; y += dy;
                        pattern.Add(x, y, StitchCommand.Jump);
                    }
                    break;
                default:
                    goto done;
            }
        }
    done:
        pattern.Add(x, y, StitchCommand.End);
        return pattern;
    }

    public void Write(StitchPattern source, Stream stream)
    {
        var pattern = source.Centered().WithMaxMove(MaxMove);
        int x = 0, y = 0;
        foreach (var s in pattern.Stitches)
        {
            int dx = s.X - x, dy = s.Y - y;
            switch (s.Command)
            {
                case StitchCommand.Stitch:
                    stream.Write(new[] { (byte)(sbyte)dx, (byte)(sbyte)-dy });
                    x = s.X; y = s.Y;
                    break;
                case StitchCommand.Jump:
                    stream.Write(new byte[] { 0x80, 0x04, (byte)(sbyte)dx, (byte)(sbyte)-dy });
                    x = s.X; y = s.Y;
                    break;
                case StitchCommand.Trim:
                    stream.Write(new byte[] { 0x80, 0x80, 0x07, 0x00 });
                    break;
                case StitchCommand.ColorChange:
                case StitchCommand.Stop:
                    stream.Write(new byte[] { 0x80, 0x01, 0x00, 0x00 });
                    break;
            }
        }
    }
}
