using System.Security.Cryptography;
using System.Text;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // One launcher per folder: two launchers would fight over the same server, ports and settings.
        string mutexName = "SteambirdsLauncher_" + StableHash(LauncherPaths.BaseFolder.ToLowerInvariant());
        using var mutex = new Mutex(true, mutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            L.Load(LauncherSettings.Load(out _).Language, out _);
            MessageBox.Show(L.T("The launcher is already open."), LauncherInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Server files with the old Portuguese names (mapas, contas, progression.json...) get their new names.
        if (Directory.Exists(LauncherPaths.ServerFolder)) LegacyFiles.Migrate(LauncherPaths.ServerFolder, (_, _) => { });

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(new Font("Segoe UI", 9f));

        // Unexpected errors go to logs\launcher-errors.txt instead of closing the launcher silently.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => CrashLog.Report(e.Exception, showDialog: true);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) CrashLog.Report(ex, showDialog: false);
        };

        Application.Run(new MainForm());
    }

    private static string StableHash(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash, 0, 8);
    }
}
