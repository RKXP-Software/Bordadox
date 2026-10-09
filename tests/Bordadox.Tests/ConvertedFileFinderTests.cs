using Bordadox.Core.Formats;

namespace Bordadox.Tests;

public sealed class TempDirs : IDisposable
{
    public string Emb { get; } = Path.Combine(Path.GetTempPath(), "bdx-emb-" + Guid.NewGuid().ToString("N"));
    public string Downloads { get; } = Path.Combine(Path.GetTempPath(), "bdx-dl-" + Guid.NewGuid().ToString("N"));

    public TempDirs()
    {
        Directory.CreateDirectory(Emb);
        Directory.CreateDirectory(Downloads);
    }

    public void Dispose()
    {
        foreach (var d in new[] { Emb, Downloads })
            try { Directory.Delete(d, recursive: true); } catch (IOException) { }
    }
}

public class ConvertedFileFinderTests
{
    private static string Touch(string dir, string name, string content = "x")
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void FindsTheConvertedFile_OnlyAfterItStopsChanging()
    {
        using var t = new TempDirs();
        var emb = Touch(t.Emb, "IMPERIO.EMB");
        var finder = new ConvertedFileFinder(emb, new[] { t.Downloads }, DateTime.Now);

        Assert.Null(finder.Poll());                                  // nada ainda
        var dst = Touch(t.Downloads, "imperio.dst", "dados do desenho");
        Assert.Null(finder.Poll());                                  // apareceu agora: pode estar sendo gravado
        Assert.Equal(dst, finder.Poll());                            // segunda consulta igual: pronto
    }

    [Fact]
    public void AFileStillGrowing_IsNotReturned()
    {
        using var t = new TempDirs();
        var emb = Touch(t.Emb, "A.EMB");
        var finder = new ConvertedFileFinder(emb, new[] { t.Downloads }, DateTime.Now);
        var path = Touch(t.Downloads, "A.pes", "1");
        Assert.Null(finder.Poll());
        File.AppendAllText(path, "mais dados");
        Assert.Null(finder.Poll());                                  // mudou de tamanho entre as consultas
        Assert.Equal(path, finder.Poll());
    }

    [Fact]
    public void IgnoresOldFiles_OtherNames_AndTheEmbItself()
    {
        using var t = new TempDirs();
        var emb = Touch(t.Emb, "IMPERIO.EMB");
        var old = Touch(t.Emb, "IMPERIO.DST");                       // o DST que já existia ao lado do .emb
        File.SetLastWriteTime(old, DateTime.Now.AddHours(-3));
        Touch(t.Downloads, "OUTRO.dst");
        Touch(t.Downloads, "IMPERIO.txt");
        Touch(t.Downloads, "IMPERIO.emb");
        var finder = new ConvertedFileFinder(emb, new[] { t.Downloads }, DateTime.Now);
        for (int i = 0; i < 3; i++) Assert.Null(finder.Poll());
    }

    [Fact]
    public void AcceptsNamesThatStartWithTheEmbName_AndPrefersDstOverPes()
    {
        using var t = new TempDirs();
        var emb = Touch(t.Emb, "URSO.EMB");
        var finder = new ConvertedFileFinder(emb, new[] { t.Downloads }, DateTime.Now);
        Touch(t.Downloads, "URSO (1).pes");
        var dst = Touch(t.Downloads, "urso_final.DST");
        finder.Poll();
        Assert.Equal(dst, finder.Poll());
    }

    [Fact]
    public void WatchesTheFolderOfTheEmbToo_AndSurvivesMissingFolders()
    {
        using var t = new TempDirs();
        var emb = Touch(t.Emb, "X.EMB");
        var finder = new ConvertedFileFinder(emb, new[] { Path.Combine(t.Downloads, "nao-existe") }, DateTime.Now);
        var jef = Touch(t.Emb, "X.jef");
        finder.Poll();
        Assert.Equal(jef, finder.Poll());
        Assert.Contains(t.Emb, finder.Folders);
    }

    [Fact]
    public void CommonFolders_ListsDownloadsAndDesktop()
    {
        var folders = ConvertedFileFinder.CommonFolders().ToList();
        Assert.Contains(folders, f => f.EndsWith("Downloads", StringComparison.OrdinalIgnoreCase));
    }
}
