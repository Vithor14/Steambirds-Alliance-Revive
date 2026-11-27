using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.EnemyAi;

// The help of the Formation tab (Help menu, F1). English texts: the window translates every paragraph with L.T, so each
// paragraph is a line of lang\<code>.json. A paragraph starting with "## " is a sub-heading and "- " an item of a list.
internal static class FormationHelp
{
    public static readonly IReadOnlyList<HelpTopic> Topics = new[]
    {
        new HelpTopic("What a formation is",
            "A formation is who is born with a leader, where each piece sits, and the links between them. It is not an AI. Each piece still flies and shoots with its own AI, made in the Enemy AI tab.",
            "The map, a world site and a dungeon only place the leader. The server adds the other pieces when the leader's AI names this formation, when the AI is generic and \"Use this formation for the leader unit\" is on, or when the map or the biome names this formation on that unit.",
            "## On a map and in a biome",
            "- Map editor, Units, Enemy AI: the Formation field. The placed unit is born with every piece of that formation.",
            "- Server, World map, Enemies of each biome: the Formation box under the list. It applies when that enemy leads the group or spawns alone. The escorts of the group are not also created.",
            "## Formation and AI",
            "- Put a glued group here: the train, the Sergeant Pup, the Five Star ring. The pieces do not fly on their own; the formation holds them and their AI only shoots.",
            "- Put a fight that summons units during the battle in the leader's AI (Phase summons), not in the formation. The Ambassador does that: cats and diplomats appear in phases, they are not born with him.",
            "- Do not point the Ambassador AI at the ambassador_body formation. That file is an optional glued body. Using both spawns the cats twice.",
            "## Where the files are",
            "Files live in server\\ai\\formations, in the same folders as the AIs: generic, world (the list also groups those by biome) and dungeons\\<name>. The file name is the id. The running server reads the folder again when a file changes. Leaders that are already alive keep the old formation."),

        new HelpTopic("The list of files",
            "The list on the left is every formation, grouped like the AI brains: generic, world by biome, and one folder per dungeon. Pick one to edit it. Right-click the list, or use the File and Edit menus, for New, Duplicate, Rename, Delete, Save and Reload. The line under the list says how many there are, or the first problem. A * before the name means it is not saved.",
            "## The menu",
            "- New makes an empty formation in generic and saves it. Rename changes the id (letters, digits and _).",
            "- Duplicate copies the selected formation under a new id.",
            "- Rename changes the id. An AI that named the old id is not updated: change that AI too.",
            "- Delete moves the file to the backup folder. It asks first.",
            "- Save writes the formation (Ctrl+S) and moves the file when the leader unit belongs in another folder. Nothing reaches the game until Save.",
            "- Reload reads the folder again and drops changes that were not saved.",
            "## What changes in the game",
            "After Save, the next leader that is born with this formation uses the new file. A leader that is already in the world keeps the formation it was born with."),

        new HelpTopic("The tabs",
            "Each tab has a short note at the top, and every field has a tip when you rest on it. A field that the current choice does not read is gray. On a member, that value stays, so changing Place back brings it with you. Changing a link's Kind replaces that rule.",
            "## Formation",
            "The leader unit and the AI it runs, the name, and the chain spacing. Spacing stays gray until some member uses place chain.",
            "## Members",
            "One row is one kind of piece born with the leader. Place decides the rest.",
            "## Links",
            "One list for every rule. Kind is shield (holds while someone lives), kill all, unshield or heal. Changing Kind keeps the row here and turns on only the fields that kind reads.",
            "## Points",
            "Places around the leader, x to the right and y in front. Make polygon builds a regular one. Edit points on the preview does the same with the mouse.",
            "## Phase summons",
            "Units the leader's AI creates during a state. They are not born with the formation. Save summons to the AI writes that file. The formation's own Save does not."),

        new HelpTopic("The leader",
            "The leader is the only unit a map or a spawn site places. The other pieces are born from it.",
            "## Leader unit",
            "The game unit of the leader, for example trainA01, podleader or fivestarleader. The list is the same as the Enemy AI unit picker. With \"Use this formation for the leader unit when its AI is generic\" on, a generic AI (or no AI) on that unit still brings this formation. Turn it off when the formation must wait until an AI names it, or a map or a biome picks it.",
            "## Leader AI",
            "The AI file the leader runs. The list is the AIs made for that unit, then the generic ones. Open in Enemy AI jumps to that file. Phase summons are stored on this AI, not in the formation file.",
            "## Spacing",
            "How far apart the pieces of a chain sit, in map units. It stays gray until a member uses place chain. A ring and a slot use their own radius and offsets."),

        new HelpTopic("Members",
            "A member is one kind of piece that is born with the leader. Add creates one and the fields under the list edit it. Delete removes it (the Delete key too). Create AI makes a shooting AI for that unit in the world folder (it refuses a generic AI).",
            "With no unit chosen, the member is not spawned and every field except Id and Unit stays gray.",
            "## Place",
            "- chain: behind the leader, along the path it flew, one spacing apart. Only Count is used (each copy is the next car). Spacing is on the Formation tab.",
            "- slot: a fixed offset, x to the right of the leader and y in front. The piece is bolted there, so a sudden turn of the leader carries it. Count stays gray, because extra copies would sit on the same spot.",
            "- follow: the same x and y, but the piece flies to that point with its own turn and speed. A sudden turn does not teleport it. The Sergeant Pup pods use this.",
            "- ring: Count pieces on a circle (Radius, Spin degrees per second, Angle where the first sits).",
            "- points: the piece uses the points of this formation. Motion then decides the rest. patrol walks vertex to vertex and waits while it turns (Pause, Speed, Spin). fixed stays on a point. toward flies to a point (Speed). orbit circles a point (Orbit radius, Spin). Point names one vertex; leave it empty and Count spreads the copies across the vertices.",
            "- free: born near the leader, then flies with its own AI. It is not glued. Only Count is used.",
            "## Id, unit, AI and role",
            "Id is the name links use. Unit is the game unit. AI is the file that piece runs; the list is the AIs made for that unit, then the generic ones; empty uses the AI of the unit. Role is a name a link or a summonsAtMost condition can filter on, for example pod or outer."),

        new HelpTopic("Links",
            "Every rule is one row of this list. Add starts a kill-all. Kind changes what the row does, and the row stays here. Fields the chosen kind does not read stay gray.",
            "## Shield",
            "The named pieces take no damage while the condition holds. Who is leader, a member id, or role:name. Several names are separated by commas. While always stays up. whileAlive waits for one id to die. whileRole waits until that role is gone. whileAny waits until every listed id is dead. While value is that id or role, and it stays gray when While is always.",
            "Shield look is any client effect. (none) draws no bubble, and the pieces still take no damage. tankShield is the player tank's bubble, so an enemy should use another look. The train shields everyone while the tail tip lives.",
            "## Kill all on death",
            "When the chosen piece dies, every piece of the formation dies. When this dies is the member id (leader is the leader). The tail tip of the train.",
            "## Unshield the next at a health %",
            "When the first living piece of a role falls under that percent of health, the next piece of that role loses its shield. Role, Health % and Target are the fields. The spirit vessel example uses 33, and Target next.",
            "## Heal on death",
            "When the chosen piece dies, Target gains Heal % health. Target leader is the usual one.",
            "Member ids of this formation are listed under the fields."),

        new HelpTopic("Points",
            "A point is a place around the leader: x to the right, y in front. Add creates one. Delete removes it. Make polygon replaces the list with a regular polygon of Count corners at Radius.",
            "A member with place points is what walks them. A summon place point:formation/id, or point:id for this formation, puts a summoned unit on that point.",
            "Edit points on the preview: click to add, drag to move, right click a point to remove it. That switch turns off \"goes where I click\" for the leader."),

        new HelpTopic("Phase summons",
            "Phase summons are units the leader's AI creates during a state, not pieces born with it. Pick a state of the Leader AI, then edit the line in the fields under the list. Save summons to the AI writes that file. The formation's own Save does not.",
            "A line with no unit, or with Count 0, summons nothing, so AI, Role, Place and the rest stay gray. Count 0 is real: the game skips that line.",
            "## Place",
            "- behind, front, left and right use Distance. Angle stays gray.",
            "- ring and around use Distance and Angle (where the ring starts).",
            "- point:formation/id sits on that point. Distance and Angle stay gray.",
            "The Ambassador fight is this kind of list. Load the ambassador AI in the Enemy AI tab to edit those states. F1 there explains states and transitions."),

        new HelpTopic("The canvas and the game",
            "The column on the right is a grid, the same kind as the Enemy AI simulator, and it draws the game's pictures of the ships. It shows the points, the polygon, and every piece on the path the server uses. Pause stops it. Speed, leader motion (circle, still, or click), right-click damage, health lost per second, Heal and Comes back after dying are the test controls.",
            "## Right click",
            "Right click damages the piece under the cursor, so you can see a shield fall off and a kill-all link fire. It does not save anything.",
            "## In the game",
            "- Place only the leader. The server adds the formation.",
            "- An AI in the world folder whose Made for list names exactly one game unit is what that unit runs, with no line in enemies.json. The Ambassador is made for ufo. The Inquisitor is made for highmountainhunter.",
            "- Enemy types... on the Enemy AI File menu adds a /enemy type, including a death unit such as ufoDEATH.",
            "- The train, the Sergeant Pup and the Five Star still come from their formations. Spawning the leader is enough. The Five Star walks a pentagon, it does not spin in a circle.",
            "Press F1 on the Enemy AI tab for how to edit the flight, the guns and the transitions of one piece.")
    };
}
