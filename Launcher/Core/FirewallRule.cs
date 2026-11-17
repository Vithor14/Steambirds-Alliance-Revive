using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace SteambirdsLauncher.Core;

// Windows Firewall rule that lets players of other PCs (local network, Radmin, Hamachi, internet) reach the server of
// this PC: one inbound rule for server\SteambirdsServer.exe, on every network profile (VPN adapters are often
// "public" networks, which the first "allow access" window of Windows leaves blocked). Adding it needs the
// administrator: Windows asks for permission (UAC).
public static class FirewallRule
{
    public const string RuleName = "Steambirds Alliance Revive server";

    // true = the rule exists, false = it does not, null = could not check.
    public static bool? Exists()
    {
        try
        {
            var start = new ProcessStartInfo("netsh", "advfirewall firewall show rule name=\"" + RuleName + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using Process? process = Process.Start(start);
            if (process == null) return null;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(5000)) return null;
            return process.ExitCode == 0;
        }
        catch
        {
            return null;
        }
    }

    // Creates (or creates again) the rule. Returns null when it worked, or the error ("" = the player said no).
    public static string? Add()
    {
        string script = Path.Combine(LauncherPaths.LogsFolder, "firewall-regra.cmd");
        try
        {
            LauncherPaths.EnsureLogsFolder();
            var text = new StringBuilder();
            text.AppendLine("@echo off");
            text.AppendLine("chcp 65001 >nul");
            text.AppendLine("netsh advfirewall firewall delete rule name=\"" + RuleName + "\" >nul 2>&1");
            text.AppendLine("netsh advfirewall firewall add rule name=\"" + RuleName + "\" dir=in action=allow program=\"" +
                            LauncherPaths.ServerExe + "\" enable=yes profile=any");
            text.AppendLine("exit /b %errorlevel%");
            File.WriteAllText(script, text.ToString(), new UTF8Encoding(false));

            var start = new ProcessStartInfo(script)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using Process? process = Process.Start(start);
            if (process == null) return L.T("Windows did not start netsh.");
            if (!process.WaitForExit(30000)) return L.T("netsh did not finish in 30 seconds.");
            return process.ExitCode == 0 ? null : L.F("netsh ended with code {0}.", process.ExitCode);
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            return ""; // the player said no to the administrator window
        }
        catch (Exception e)
        {
            return e.Message;
        }
        finally
        {
            try
            {
                if (File.Exists(script)) File.Delete(script);
            }
            catch
            {
            }
        }
    }
}
