using System.Text;

namespace Bordadox.Core.Formats;

/// <summary>Uma entrada (pasta ou fluxo) de um arquivo composto OLE2.</summary>
public sealed class CompoundEntry
{
    public string Name { get; init; } = "";
    /// <summary>Caminho completo, com as pastas separadas por "/".</summary>
    public string Path { get; set; } = "";
    /// <summary>1 = pasta, 2 = fluxo, 5 = raiz.</summary>
    public int Type { get; init; }
    public long Size { get; init; }
    internal int StartSector { get; init; }
    internal int Id { get; init; }
    internal int Left { get; init; }
    internal int Right { get; init; }
    internal int Child { get; init; }
    public bool IsStream => Type == 2;
}

/// <summary>
/// Leitor mínimo do "Compound File Binary" (OLE2, o contêiner usado por arquivos .doc, .xls e também pelo .emb da Wilcom).
/// O formato é documentado publicamente (MS-CFB): setores com tabela de alocação (FAT), diretório em árvore e um "mini fluxo"
/// para fluxos pequenos. Só lê.
/// </summary>
public sealed class CompoundFile
{
    public static readonly byte[] Signature = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

    private const int EndOfChain = -2, FreeSector = -1, NoStream = -1;

    private readonly byte[] _data;
    private readonly int _sectorSize, _miniSectorSize, _miniCutoff;
    private readonly int[] _fat;
    private int[] _miniFat = Array.Empty<int>();
    private byte[] _miniStream = Array.Empty<byte>();

    public List<CompoundEntry> Entries { get; } = new();

    public static bool IsCompoundFile(ReadOnlySpan<byte> data) => data.Length >= 512 && data[..8].SequenceEqual(Signature);

    public CompoundFile(byte[] data)
    {
        if (!IsCompoundFile(data)) throw new InvalidDataException("Não é um arquivo composto OLE2.");
        _data = data;
        int sectorShift = BitConverter.ToUInt16(data, 0x1E);
        if (sectorShift is not (9 or 12)) throw new InvalidDataException("Tamanho de setor não suportado.");
        _sectorSize = 1 << sectorShift;
        _miniSectorSize = 1 << BitConverter.ToUInt16(data, 0x20);
        _miniCutoff = BitConverter.ToInt32(data, 0x38);

        // Tabela de alocação: os setores da FAT vêm da lista no cabeçalho (109 entradas) e, se preciso, da cadeia DIFAT.
        int fatCount = BitConverter.ToInt32(data, 0x2C);
        var fatSectors = new List<int>();
        for (int i = 0; i < 109 && fatSectors.Count < fatCount; i++)
        {
            int s = BitConverter.ToInt32(data, 0x4C + i * 4);
            if (s >= 0) fatSectors.Add(s);
        }
        int difat = BitConverter.ToInt32(data, 0x44), difatCount = BitConverter.ToInt32(data, 0x48);
        for (int d = 0; d < difatCount && difat >= 0 && fatSectors.Count < fatCount; d++)
        {
            int off = SectorOffset(difat);
            for (int i = 0; i < _sectorSize / 4 - 1 && fatSectors.Count < fatCount; i++)
            {
                int s = BitConverter.ToInt32(data, off + i * 4);
                if (s >= 0) fatSectors.Add(s);
            }
            difat = BitConverter.ToInt32(data, off + _sectorSize - 4);
        }

        _fat = new int[fatSectors.Count * (_sectorSize / 4)];
        for (int f = 0; f < fatSectors.Count; f++)
            for (int i = 0; i < _sectorSize / 4; i++)
                _fat[f * (_sectorSize / 4) + i] = BitConverter.ToInt32(data, SectorOffset(fatSectors[f]) + i * 4);

        ReadDirectory(BitConverter.ToInt32(data, 0x30));

        var root = Entries.FirstOrDefault(e => e.Type == 5);
        if (root != null)
        {
            _miniStream = ReadChain(root.StartSector, root.Size);
            int miniFatStart = BitConverter.ToInt32(data, 0x3C);
            var mini = ReadChain(miniFatStart, long.MaxValue);
            _miniFat = new int[mini.Length / 4];
            for (int i = 0; i < _miniFat.Length; i++) _miniFat[i] = BitConverter.ToInt32(mini, i * 4);
        }
    }

    private int SectorOffset(int sector) => (sector + 1) * _sectorSize;

    private byte[] ReadChain(int start, long size)
    {
        var ms = new MemoryStream();
        int s = start, guard = 0;
        while (s >= 0 && s < _fat.Length && guard++ < _fat.Length + 2)
        {
            int off = SectorOffset(s);
            if (off + _sectorSize > _data.Length) { ms.Write(_data, off, Math.Max(0, _data.Length - off)); break; }
            ms.Write(_data, off, _sectorSize);
            s = _fat[s];
            if (ms.Length >= size) break;
        }
        var all = ms.ToArray();
        return size == long.MaxValue || all.Length <= size ? all : all[..(int)size];
    }

    private void ReadDirectory(int firstSector)
    {
        var dir = ReadChain(firstSector, long.MaxValue);
        for (int i = 0; i + 128 <= dir.Length; i += 128)
        {
            int type = dir[i + 0x42];
            if (type == 0) continue;
            int nameLen = BitConverter.ToUInt16(dir, i + 0x40);
            string name = Encoding.Unicode.GetString(dir, i, Math.Max(0, nameLen - 2));
            long size = _sectorSize == 512 ? BitConverter.ToUInt32(dir, i + 0x78) : BitConverter.ToInt64(dir, i + 0x78);
            Entries.Add(new CompoundEntry
            {
                Name = name, Type = type, Size = size, Id = i / 128,
                Left = BitConverter.ToInt32(dir, i + 0x44), Right = BitConverter.ToInt32(dir, i + 0x48), Child = BitConverter.ToInt32(dir, i + 0x4C),
                StartSector = BitConverter.ToInt32(dir, i + 0x74),
            });
        }
        // Caminhos: desce a árvore de irmãos a partir da raiz.
        var byId = Entries.ToDictionary(e => e.Id);
        void Walk(int id, string parent)
        {
            if (id == NoStream || !byId.TryGetValue(id, out var e)) return;
            Walk(e.Left, parent);
            e.Path = parent.Length == 0 ? e.Name : parent + "/" + e.Name;
            if (e.Type == 1) Walk(e.Child, e.Path);
            Walk(e.Right, parent);
        }
        var root = Entries.FirstOrDefault(e => e.Type == 5);
        if (root != null) { root.Path = ""; Walk(root.Child, ""); }
    }

    /// <summary>Conteúdo de um fluxo.</summary>
    public byte[] Read(CompoundEntry entry)
    {
        if (!entry.IsStream) throw new ArgumentException("A entrada não é um fluxo.");
        if (entry.Size >= _miniCutoff) return ReadChain(entry.StartSector, entry.Size);

        var result = new byte[entry.Size];
        int s = entry.StartSector, pos = 0, guard = 0;
        while (s >= 0 && s < _miniFat.Length && pos < result.Length && guard++ < _miniFat.Length + 2)
        {
            int off = s * _miniSectorSize, n = Math.Min(_miniSectorSize, result.Length - pos);
            if (off + n > _miniStream.Length) break;
            Buffer.BlockCopy(_miniStream, off, result, pos, n);
            pos += n;
            s = _miniFat[s];
        }
        return result;
    }

    public CompoundEntry? Find(string path) =>
        Entries.FirstOrDefault(e => e.IsStream && e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
}
