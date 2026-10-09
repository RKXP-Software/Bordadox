using System.IO;
using System.Runtime.CompilerServices;
using Bordadox.App.Editor;

namespace Bordadox.App.Tests;

internal static class TestSetup
{
    /// <summary>Os testes nunca devem ler nem gravar as preferências reais do usuário em %APPDATA%.</summary>
    [ModuleInitializer]
    public static void IsolatePreferences()
    {
        UserPrefs.FilePath = Path.Combine(Path.GetTempPath(), "bordadox-tests-" + Environment.ProcessId, "prefs.json");
        UserPrefs.Reset();
    }
}
