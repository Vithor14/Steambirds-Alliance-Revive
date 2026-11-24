namespace SteambirdsLauncher.Core;

// The chat commands of our server, for the command bar of the Server tab (Console page). Same commands as "Chat commands.md":
// they are typed in the game chat by a player or here by the host, who picks the player they run for. The game's own client
// commands (/help, /options, /die...) never reach the server and are not here.
public static class ServerCommandList
{
    public sealed class Entry
    {
        public Entry(string group, string usage, string description)
        {
            Group = group;
            Usage = usage;
            Description = description;
        }

        public string Group { get; }
        // "/item ID [N]": the command and its arguments.
        public string Usage { get; }
        public string Description { get; }

        // "/item": what goes in the box when the entry is picked (the arguments are typed after it).
        public string Command
        {
            get
            {
                int space = Usage.IndexOf(' ');
                return space < 0 ? Usage : Usage.Substring(0, space);
            }
        }
    }

    // Built on each call, in the language of the launcher.
    public static List<Entry> All()
    {
        string network = L.T("Network and admin");
        string items = L.T("Items");
        string progression = L.T("Progression");
        string dungeons = L.T("Dungeons and world");
        string tutorial = L.T("Tutorial");
        string enemies = L.T("Enemies (tests)");
        string stores = L.T("Stores and social");
        return new List<Entry>
        {
            new Entry(network, "/players", L.T("Lists the players online (written in the server log; needs no player)")),
            new Entry(network, "/admin", L.T("Lists the admin and debug commands")),
            new Entry(network, "/debug [NAME VALUE]", L.T("Shows the debug settings of the plane, or sets one (damage, speed, health...)")),
            new Entry(network, "/god [off]", L.T("Nothing hurts the plane (damage received x 0)")),
            new Entry(network, "/sound [NAME]", L.T("Plays a sound of the game for the player; with no name, lists them")),
            new Entry(network, "/soundall NAME", L.T("Plays a sound for everybody on the server the player is on")),
            new Entry(network, "/screenshot", L.T("The player's game takes a picture and uploads it to the server (client-uploads)")),
            new Entry(network, "/stats", L.T("Server loop time, packets, memory and players")),
            new Entry(network, "/serverstats [on|off]", L.T("Sends the server time chart to the player's game")),
            new Entry(network, "/audit", L.T("Inventory audit of the player's connection")),
            new Entry(network, "/watchai [ID]", L.T("AI text of an enemy (the closest one when there is no id)")),
            new Entry(network, "/passwd NAME PASSWORD", L.T("Changes the password of an account with username and password")),

            new Entry(items, "/items", L.T("Lists the item commands")),
            new Entry(items, "/item ID [N]", L.T("Puts N of the item in the cargo (up to 20), for example /item armor_3")),
            new Entry(items, "/loot [UNIT] [TIER]", L.T("Rolls the loot of a kill in front of the plane")),
            new Entry(items, "/inventory", L.T("Shows the plane's inventory as text")),
            new Entry(items, "/clear_cargo", L.T("Empties the cargo")),
            new Entry(items, "/currency [ID N]", L.T("Shows the currencies, or adds N to one")),
            new Entry(items, "/ingredient ID N", L.T("Adds N of an ingredient")),
            new Entry(items, "/bags", L.T("Counts the player's loot bags in the world and lists the loot tables")),
            new Entry(items, "/clear_bags", L.T("Removes the player's loot bags")),
            new Entry(items, "/export_items", L.T("Writes the item catalog again (catalog folder)")),

            new Entry(progression, "/progression", L.T("Lists the progression commands")),
            new Entry(progression, "/pilot", L.T("Shows the pilot's rank, XP and status")),
            new Entry(progression, "/xp N", L.T("Gives N XP to the plane")),
            new Entry(progression, "/level N", L.T("Puts the plane at level N")),
            new Entry(progression, "/rank N", L.T("Puts the pilot at rank N")),
            new Entry(progression, "/reset_pilot", L.T("Resets the pilot's rank, XP and skills")),
            new Entry(progression, "/buff [ID]", L.T("Gives a buff; with no id, lists the buffs")),
            new Entry(progression, "/buffs", L.T("Lists the buffs")),
            new Entry(progression, "/no_buffs", L.T("Takes every buff off")),
            new Entry(progression, "/portrait", L.T("Rolls a new portrait for the pilot")),

            new Entry(dungeons, "/dungeons", L.T("Lists the dungeons and whether they are open")),
            new Entry(dungeons, "/dungeon NAME", L.T("Sends the player to a dungeon, for example /dungeon bomb")),
            new Entry(dungeons, "/dungeonhelp", L.T("Help and the state of the current dungeon")),
            new Entry(dungeons, "/room [N]", L.T("In a dungeon: lists the rooms, or jumps to room N")),
            new Entry(dungeons, "/boss", L.T("In a dungeon: jumps to the boss room")),
            new Entry(dungeons, "/restart", L.T("In a dungeon or the tutorial: starts it again")),
            new Entry(dungeons, "/leave", L.T("In a dungeon or the tutorial: goes back to the Nexus")),
            new Entry(dungeons, "/world", L.T("Sends the player to the open world")),
            new Entry(dungeons, "/worldinfo", L.T("Seed, players, spawn sites and carriers of the open world")),
            new Entry(dungeons, "/carrier [NAME]", L.T("In the open world: puts a dungeon carrier next to the player")),

            new Entry(tutorial, "/tutorialhelp", L.T("Help and the current step of the tutorial")),
            new Entry(tutorial, "/step N", L.T("In the tutorial: jumps to step N (1 drones ... 5 portal)")),
            new Entry(tutorial, "/portal", L.T("In the tutorial: creates the portal to the Nexus in front of the plane")),

            new Entry(enemies, "/enemies", L.T("Lists the enemy commands and the state of the encounter")),
            new Entry(enemies, "/wave N", L.T("Jumps to wave N of the encounter")),
            new Entry(enemies, "/next", L.T("Calls the next wave")),
            new Entry(enemies, "/enemy TYPE [COUNT]", L.T("Creates enemies of a type of enemies.json near the player (up to 20)")),
            new Entry(enemies, "/spawn TEMPLATE [COUNT]", L.T("Creates any unit of the game by its template name (up to 20)")),
            new Entry(enemies, "/clear", L.T("Removes every enemy and stops the waves")),
            new Entry(enemies, "/reset", L.T("Restarts the encounter")),
            new Entry(enemies, "/encounter [NAME]", L.T("Lists the encounters or starts one")),
            new Entry(enemies, "/types", L.T("Lists the enemy types of enemies.json")),
            new Entry(enemies, "/catalog [FILTER]", L.T("Searches the armed units of the game")),
            new Entry(enemies, "/brains", L.T("Lists the AI files the server loaded")),

            new Entry(stores, "/store", L.T("Lists the store commands")),
            new Entry(stores, "/store info", L.T("Purchases on or off, price mode, slots and the player's currencies")),
            new Entry(stores, "/store list [STORE]", L.T("Lists the shelves of a store")),
            new Entry(stores, "/store buy PRODUCT [PRICE]", L.T("Buys a product for the player (PRICE: normal or iron)")),
            new Entry(stores, "/store reset", L.T("Forgets the purchases of the account")),
            new Entry(stores, "/store restock", L.T("Refills the server stock")),
            new Entry(stores, "/friends", L.T("Lists the player's friends and where each one is"))
        };
    }
}
