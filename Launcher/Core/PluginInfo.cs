using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace SteambirdsLauncher.Core;

// The Steambirds Quieter plugin (BepInEx). The server does not need it to run, but some fixes live in it
// (Flock Hall art, death screen numbers, pilot portrait parts in portraits_config.txt).
public static class PluginInfo
{
    public enum Status { NoGame, NoBepInEx, NotInstalled, SameAsBundled, DifferentFromBundled, InstalledNoBundle }

    public sealed class Report
    {
        public Status State { get; init; }
        public string InstalledVersion { get; init; } = "";
        public string BundledVersion { get; init; } = "";
        public bool HasBundled { get; init; }
    }

    public static Report Check(string? gameFolder)
    {
        bool hasBundled = File.Exists(LauncherPaths.BundledPlugin);
        string bundledVersion = hasBundled ? ReadVersion(LauncherPaths.BundledPlugin) ?? "" : "";

        if (!GameInstall.HasGame(gameFolder))
            return new Report { State = Status.NoGame, HasBundled = hasBundled, BundledVersion = bundledVersion };
        if (!GameInstall.HasBepInEx(gameFolder))
            return new Report { State = Status.NoBepInEx, HasBundled = hasBundled, BundledVersion = bundledVersion };

        string installed = GameInstall.InstalledPluginPath(gameFolder!);
        if (!File.Exists(installed))
            return new Report { State = Status.NotInstalled, HasBundled = hasBundled, BundledVersion = bundledVersion };

        string installedVersion = ReadVersion(installed) ?? "";
        Status state = !hasBundled ? Status.InstalledNoBundle
            : SameContent(installed, LauncherPaths.BundledPlugin) ? Status.SameAsBundled
            : Status.DifferentFromBundled;
        return new Report { State = state, InstalledVersion = installedVersion, BundledVersion = bundledVersion, HasBundled = hasBundled };
    }

    // Copies plugin\QuieterPlugin.dll into <game>\BepInEx\plugins (what "Build plugin.ps1" did).
    public static void Install(string gameFolder)
    {
        string target = GameInstall.InstalledPluginPath(gameFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(LauncherPaths.BundledPlugin, target, overwrite: true);
    }

    // Version from the [BepInPlugin(guid, name, version)] attribute, read from the metadata without loading the dll.
    public static string? ReadVersion(string dllPath)
    {
        try
        {
            using var stream = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return null;
            MetadataReader metadata = pe.GetMetadataReader();
            foreach (CustomAttributeHandle handle in metadata.CustomAttributes)
            {
                CustomAttribute attribute = metadata.GetCustomAttribute(handle);
                if (AttributeTypeName(metadata, attribute) != "BepInPlugin") continue;

                BlobReader blob = metadata.GetBlobReader(attribute.Value);
                if (blob.ReadUInt16() != 1) continue; // custom attribute prolog
                blob.ReadSerializedString();          // guid
                blob.ReadSerializedString();          // name
                return blob.ReadSerializedString();   // version
            }
        }
        catch
        {
        }
        return null;
    }

    private static string AttributeTypeName(MetadataReader metadata, CustomAttribute attribute)
    {
        EntityHandle constructor = attribute.Constructor;
        if (constructor.Kind == HandleKind.MemberReference)
        {
            MemberReference member = metadata.GetMemberReference((MemberReferenceHandle)constructor);
            if (member.Parent.Kind == HandleKind.TypeReference)
            {
                TypeReference type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
                return metadata.GetString(type.Name);
            }
        }
        else if (constructor.Kind == HandleKind.MethodDefinition)
        {
            MethodDefinition method = metadata.GetMethodDefinition((MethodDefinitionHandle)constructor);
            TypeDefinition type = metadata.GetTypeDefinition(method.GetDeclaringType());
            return metadata.GetString(type.Name);
        }
        return "";
    }

    private static bool SameContent(string a, string b)
    {
        try
        {
            var infoA = new FileInfo(a);
            var infoB = new FileInfo(b);
            if (infoA.Length != infoB.Length) return false;
            return Hash(a).SequenceEqual(Hash(b));
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return SHA256.HashData(stream);
    }
}
