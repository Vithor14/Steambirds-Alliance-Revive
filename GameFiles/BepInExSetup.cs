using System.IO.Compression;
using System.Security.Cryptography;

namespace SteambirdsGameFiles;

// BepInEx 5 (the mod loader that runs the Steambirds Quieter plugin inside the game) and the plugin itself.
// BepInEx is not part of this project (LGPL, by the BepInEx team): the tool downloads the official release from GitHub,
// or uses a BepInEx_win_x64_*.zip / BepInEx_win_x86_*.zip placed next to the tool (for PCs without internet).
internal static class BepInExSetup
{
    public const string Version = "5.4.23.5";
    private const string ReleaseUrl = "https://github.com/BepInEx/BepInEx/releases/download/v{0}/BepInEx_win_{1}_{0}.zip";
    public const string ReleasesPage = "https://github.com/BepInEx/BepInEx/releases";

    public static bool IsInstalled(string game) =>
        File.Exists(Path.Combine(GameInstallation.BepInExFolder(game), "core", "BepInEx.dll"));

    public static string PluginPath(string game) =>
        Path.Combine(GameInstallation.BepInExFolder(game), "plugins", "QuieterPlugin.dll");

    public static string DownloadUrl(bool x64) => string.Format(ReleaseUrl, Version, x64 ? "x64" : "x86");

    // Extracts BepInEx into the game folder. Returns an error or null.
    public static string? Install(string game, string toolFolder, bool x64)
    {
        string architecture = x64 ? "x64" : "x86";
        string? zip = LocalZip(toolFolder, architecture);
        string? temporary = null;
        try
        {
            if (zip == null)
            {
                string url = DownloadUrl(x64);
                ConsoleUi.Info("downloading " + url);
                temporary = Path.Combine(Path.GetTempPath(), "SteambirdsGameFiles-BepInEx-" + Guid.NewGuid().ToString("N") + ".zip");
                using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("SteambirdsGameFiles/1.0");
                    byte[] data = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
                    File.WriteAllBytes(temporary, data);
                }
                zip = temporary;
            }
            else
            {
                ConsoleUi.Info("using " + zip);
            }

            ZipFile.ExtractToDirectory(zip, game, overwriteFiles: true);
            return IsInstalled(game) ? null : "the archive has no BepInEx\\core\\BepInEx.dll";
        }
        catch (UnauthorizedAccessException e)
        {
            return "no permission to write in the game folder (" + e.Message + "). Run the tool as administrator.";
        }
        catch (Exception e)
        {
            return e.GetType().Name + ": " + e.Message;
        }
        finally
        {
            if (temporary != null)
            {
                try { File.Delete(temporary); } catch { }
            }
        }
    }

    // Copies the plugin of the package into BepInEx\plugins when it is missing or different. Returns an error or null.
    public static string? InstallPlugin(string game, string pluginFile, out bool changed)
    {
        changed = false;
        try
        {
            string target = PluginPath(game);
            if (File.Exists(target) && SameContent(target, pluginFile)) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(pluginFile, target, overwrite: true);
            changed = true;
            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    // A BepInEx zip for this architecture next to the tool (the newest by name), or null.
    private static string? LocalZip(string toolFolder, string architecture)
    {
        try
        {
            return Directory.GetFiles(toolFolder, "BepInEx*.zip")
                .Where(path => Path.GetFileName(path).Contains("_" + architecture + "_", StringComparison.OrdinalIgnoreCase))
                .Where(path => !Path.GetFileName(path).Contains("unix", StringComparison.OrdinalIgnoreCase) &&
                               !Path.GetFileName(path).Contains("linux", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public static bool SameContent(string a, string b)
    {
        try
        {
            if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
            using FileStream streamA = File.OpenRead(a);
            using FileStream streamB = File.OpenRead(b);
            byte[] hashA = SHA256.HashData(streamA);
            byte[] hashB = SHA256.HashData(streamB);
            return Convert.ToHexString(hashA) == Convert.ToHexString(hashB);
        }
        catch
        {
            return false;
        }
    }
}
