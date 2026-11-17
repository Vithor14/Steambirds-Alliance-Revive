// Older versions of the server used Portuguese file and folder names (mapas, dados, contas, progression.json...).
// LegacyFiles renames them to the current names when the server starts, so a host that updates keeps its maps,
// settings, accounts and game data. Nothing is overwritten: when both names exist, the new one is kept and the old
// one stays where it is (a warning goes to the log).
//
// The launcher has the same table (Launcher\Core\LegacyFiles.cs). Keep both in sync.
public static class LegacyFiles
{
    // Folders of the server folder: old name -> new name.
    private static readonly (string Old, string New)[] Folders =
    {
        ("mapas", "maps"),
        ("dados", "data"),
        ("contas", "accounts"),
        ("itens", "catalog"),
        ("envios-do-cliente", "client-uploads"),
    };

    // Files of the server folder.
    private static readonly (string Old, string New)[] RootFiles =
    {
        ("progressao.json", "progression.json"),
        ("itens.json", "items.json"),
        ("inimigos.json", "enemies.json"),
        ("rede.json", "network.json"),
        // the old single account (before one account per player)
        ("conta.json", "account.json"),
        ("inventario.json", "inventory.json"),
        ("aceites.json", "acceptances.json"),
    };

    // Files inside each account folder (accounts\<key>\).
    private static readonly (string Old, string New)[] AccountFiles =
    {
        ("conta.json", "account.json"),
        ("inventario.json", "inventory.json"),
        ("aceites.json", "acceptances.json"),
    };

    // Files inside the data folder (game data exported by the plugin).
    private static readonly (string Old, string New)[] DataFiles =
    {
        ("acoes_config.txt", "actions_config.txt"),
        ("unidades_config.txt", "units_config.txt"),
        ("retratos_config.txt", "portraits_config.txt"),
    };

    private static readonly (string Old, string New)[] CatalogFiles =
    {
        ("catalogo_de_itens.json", "item_catalog.json"),
        ("catalogo_de_itens.md", "item_catalog.md"),
        ("LEIA-ME.md", "README.md"),
    };

    // Old names of files that the plugin writes next to the game (BepInEx folder): GameDataFiles looks for both.
    public static readonly Dictionary<string, string> PluginFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["actions_config.txt"] = "acoes_config.txt",
        ["units_config.txt"] = "unidades_config.txt",
        ["portraits_config.txt"] = "retratos_config.txt",
    };

    public static void Migrate(string serverFolder, Action<string, string> log)
    {
        try
        {
            foreach ((string oldName, string newName) in Folders)
                MoveFolder(Path.Combine(serverFolder, oldName), Path.Combine(serverFolder, newName), log);
            foreach ((string oldName, string newName) in RootFiles)
                MoveFile(Path.Combine(serverFolder, oldName), Path.Combine(serverFolder, newName), log);

            string accounts = Path.Combine(serverFolder, "accounts");
            if (Directory.Exists(accounts))
            {
                foreach (string folder in Directory.GetDirectories(accounts))
                    foreach ((string oldName, string newName) in AccountFiles)
                        MoveFile(Path.Combine(folder, oldName), Path.Combine(folder, newName), log);
            }

            string data = Path.Combine(serverFolder, "data");
            foreach ((string oldName, string newName) in DataFiles)
                MoveFile(Path.Combine(data, oldName), Path.Combine(data, newName), log);

            string catalog = Path.Combine(serverFolder, "catalog");
            foreach ((string oldName, string newName) in CatalogFiles)
                MoveFile(Path.Combine(catalog, oldName), Path.Combine(catalog, newName), log);
        }
        catch (Exception e)
        {
            log("LEGACY", "could not rename old files: " + e.Message);
        }
    }

    private static void MoveFolder(string from, string to, Action<string, string> log)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(from)) return;
        if (!Directory.Exists(to))
        {
            Directory.Move(from, to);
            log("LEGACY", "renamed folder " + Path.GetFileName(from) + " -> " + Path.GetFileName(to));
            return;
        }
        // Both exist: move what the new folder does not have yet.
        foreach (string entry in Directory.GetFileSystemEntries(from))
        {
            string target = Path.Combine(to, Path.GetFileName(entry));
            if (File.Exists(target) || Directory.Exists(target)) continue;
            if (Directory.Exists(entry)) Directory.Move(entry, target); else File.Move(entry, target);
        }
        log("LEGACY", "folder " + Path.GetFileName(from) + " still exists next to " + Path.GetFileName(to) +
            ": what was missing in the new one was moved, the rest was left in place");
    }

    private static void MoveFile(string from, string to, Action<string, string> log)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase) || !File.Exists(from)) return;
        if (File.Exists(to))
        {
            log("LEGACY", Path.GetFileName(from) + " and " + Path.GetFileName(to) + " both exist: using the new one");
            return;
        }
        File.Move(from, to);
        log("LEGACY", "renamed " + Path.GetFileName(from) + " -> " + Path.GetFileName(to));
    }
}
