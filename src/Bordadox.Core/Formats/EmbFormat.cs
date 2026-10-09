using System.IO.Compression;
using Bordadox.Core.Stitches;

namespace Bordadox.Core.Formats;

/// <summary>
/// O arquivo é um .emb da Wilcom, mas os pontos não podem ser lidos: ficam no fluxo "DesignDocument", num formato fechado.
/// Carrega o que dá para ver sem isso: o nome, a miniatura e os fluxos encontrados, para o aplicativo explicar o caminho alternativo.
/// </summary>
public sealed class WilcomEmbException : NotSupportedException
{
    public WilcomEmbException(string message, byte[]? previewBmp, long designDataBytes, IReadOnlyList<string> streams) : base(message)
    {
        PreviewBmp = previewBmp;
        DesignDataBytes = designDataBytes;
        Streams = streams;
    }

    /// <summary>Miniatura (BMP) guardada no arquivo, se houver.</summary>
    public byte[]? PreviewBmp { get; }

    /// <summary>Tamanho do fluxo "DesignDocument" (onde ficam os objetos e pontos do desenho).</summary>
    public long DesignDataBytes { get; }

    public IReadOnlyList<string> Streams { get; }
}

/// <summary>
/// .emb da Wilcom. É um arquivo composto OLE2 (contêiner documentado) cujos fluxos úteis, "DesignDocument" e "CorelDocument", não têm
/// estrutura legível: não são compactação comum (zlib/deflate) e têm entropia de dado cifrado. Sem especificação pública, o Bordadox
/// não abre os pontos do .emb; esta classe só reconhece o arquivo e devolve um erro explicativo
/// (<see cref="WilcomEmbException"/>), com a miniatura, para a janela orientar a exportar o desenho como DST ou PES a partir do Wilcom.
/// </summary>
public sealed class EmbFormat : IEmbroideryFormat
{
    public string Description => "Wilcom EMB (só reconhece; exporte como DST)";
    public string Extension => "emb";
    public bool CanRead => true;
    public bool CanWrite => false;

    public const string Explanation =
        "Este é um arquivo .emb da Wilcom. Os pontos dele ficam num formato fechado da Wilcom, que o Bordadox não consegue ler.\n\n" +
        "Para trazer o desenho: abra o arquivo no Wilcom, use Arquivo › Exportar (ou \"Salvar como\") e escolha DST ou PES, e abra esse arquivo aqui.";

    public StitchPattern Read(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var data = ms.ToArray();

        if (!CompoundFile.IsCompoundFile(data))
            throw new InvalidDataException("Este .emb não é um arquivo da Wilcom (talvez seja do Embird, que usa outro formato). Exporte o desenho como DST ou PES no programa que o criou.");

        var cf = new CompoundFile(data);
        var design = cf.Find("DesignDocument");
        var streams = cf.Entries.Where(e => e.IsStream).Select(e => e.Path).ToList();
        if (design == null)
            throw new InvalidDataException("Arquivo composto sem os dados de desenho da Wilcom (não parece um .emb dela).");

        byte[]? preview = null;
        try
        {
            if (cf.Find("DESIGN_ICON") is { } icon)
            {
                var raw = cf.Read(icon);
                if (raw.Length > 6)
                {
                    using var zin = new ZLibStream(new MemoryStream(raw, 4, raw.Length - 4), CompressionMode.Decompress);
                    using var bmp = new MemoryStream();
                    zin.CopyTo(bmp);
                    var bytes = bmp.ToArray();
                    if (bytes.Length > 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M') preview = bytes;
                }
            }
        }
        catch (Exception) { /* miniatura é só um extra */ }

        throw new WilcomEmbException(Explanation, preview, design.Size, streams);
    }

    public void Write(StitchPattern pattern, Stream stream) =>
        throw new NotSupportedException("O Bordadox não grava .emb. Exporte como PES, DST ou outro formato.");
}
