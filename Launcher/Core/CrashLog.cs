namespace SteambirdsLauncher.Core;

// Unexpected launcher errors: written to logs\launcher-errors.txt and, on the UI thread, shown in a dialog.
public static class CrashLog
{
    private static readonly object Gate = new object();

    public static void Report(Exception error, bool showDialog)
    {
        try
        {
            lock (Gate)
            {
                LauncherPaths.EnsureLogsFolder();
                File.AppendAllText(LauncherPaths.ErrorLogFile,
                    "=== " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") + " (launcher " + LauncherInfo.Version + ") ===" +
                    Environment.NewLine + error + Environment.NewLine + Environment.NewLine);
            }
        }
        catch
        {
            // Nothing else to do if even the log cannot be written.
        }

        if (!showDialog) return;
        MessageBox.Show(
            L.F("Unexpected launcher error:\n\n{0}\n\nThe details were saved in {1}.", error.Message, LauncherPaths.ErrorLogFile),
            LauncherInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
