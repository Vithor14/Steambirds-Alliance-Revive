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
            "The map, a world site and a dungeon only place the leader. The server adds the other pieces when the leader's AI names this formation, or when the AI is generic and \"Use this formation for the leader unit\" is on.",
            "## Formation and AI",
            "- Put a glued group here: the train, the Sergeant Pup, the Five Star ring. The pieces do not fly on their own; the formation holds them and their AI only shoots.",
            "- Put a fight that summons units during the battle in the leader's AI (Phase summons), not in the formation. The Ambassador does that: cats and diplomats appear in phases, they are not born with him.",
            "- Do not point the Ambassador AI at the ambassador_body formation. That file is an optional glued body. Using both spawns the cats twice.",
            "## Where the files are",
            "Every formation is a .json file in server\\ai\\formations. The file name is the id. The running server reads the folder again when a file changes. Leaders that are already alive keep the old formation."),

        new HelpTopic("The list of files",
            "The list on the left is every formation in the folder. Pick one to edit it. The line under the buttons says how many there are, or the first problem.",
            "## The buttons",
            "- New makes an empty formation and asks for an id (letters, digits and _).",
            "- Duplicate copies the selected formation under a new id.",
            "- Rename changes the id. An AI that named the old id is not updated: change that AI too.",
            "- Delete moves the file to the backup folder. It asks first.",
            "- Save writes the formation. Nothing reaches the game until Save.",
            "- Reload reads the folder again and drops changes that were not saved.",
            "## What changes in the game",
            "After Save, the next leader that is born with this formation uses the new file. A leader that is already in the world keeps the formation it was born with."),

        new HelpTopic("The leader",
            "The leader is the only unit a map or a spawn site places. The other pieces are born from it.",
            "## Leader unit",
            "The game unit of the leader, for example trainA01, podleader or fivestarleader. With \"Use this formation for the leader unit when its AI is generic\" on, a generic AI (or no AI) on that unit still brings this formation. Turn it off when the formation must wait until an AI names it.",
            "## Leader AI",
            "The AI file the leader runs. Open in Enemy AI jumps to that file. Phase summons are stored on this AI, not in the formation file.",
            "## Spacing",
            "How far apart the pieces of a chain sit, in map units. A ring and a slot use their own radius and offsets."),

        new HelpTopic("Members",
            "A member is one kind of piece that is born with the leader. Add creates one, Edit member changes it, Delete removes it, Create AI makes a shooting AI for that unit in the world folder (it refuses a generic AI).",
            "## Place",
            "- chain: behind the leader, along the path it flew, one spacing apart. The train.",
            "- slot: a fixed offset, x to the right of the leader and y in front. The pods of the Sergeant Pup.",
            "- ring: count pieces on a circle (radius, spin degrees per second, start angle). The Five Star.",
            "- free: born at the offset, then flies with its own AI. It is not glued.",
            "## Id, unit, AI and role",
            "Id is the name links use. Unit is the game unit. AI is the file that piece runs; empty uses the AI of the unit type. Role is a name a link or a summonsAtMost condition can filter on, for example pod or outer."),

        new HelpTopic("Links",
            "A link is a rule between pieces. They are checked while the formation lives.",
            "## Shield while someone lives",
            "The listed pieces take no damage while the chosen pieces are alive. An empty effect means no bubble. tankShield is the bubble the game draws. The train shields everyone while the tail tip lives.",
            "## Kill all on death",
            "When the chosen piece dies, every piece of the formation dies. The tail tip of the train.",
            "## Unshield the next at a health %",
            "When the first living piece of a role falls under that percent of health, the next piece of that role loses its shield. The spirit vessel example uses 33.",
            "## Heal the leader on death",
            "When the chosen piece dies, the leader gains that much health."),

        new HelpTopic("Phase summons",
            "Phase summons are units the leader's AI creates during a state, not pieces born with it. The box lists the states of the Leader AI. Add and Delete edit that state's summon list. Save writes the AI file.",
            "Each line names the unit type (a key of enemies.json or a game unit), an optional AI, a role, a place (behind, front, left, right, ring or around), how many, how often, and how many of that line may be alive.",
            "Create AI on a member is for a glued piece. A summoned unit should already have its own AI file, named on the summon line.",
            "The Ambassador fight is this kind of list: three cats around him, then diplomat waves, then inner and outer cats. Load the ambassador AI in the Enemy AI tab to edit those states. F1 there explains states and transitions."),

        new HelpTopic("The canvas and the game",
            "The picture on the right shows the leader and the glued pieces. A ring is drawn as a circle. A chain is drawn in a line behind the leader.",
            "## Right click",
            "Right click damages the piece under the cursor, so you can see a shield fall off and a kill-all link fire. It does not save anything.",
            "## In the game",
            "- Place only the leader. The server adds the formation.",
            "- Chat: /enemy ai_ambassador starts the Ambassador (he is also a world boss when the biome's boss is the ufo). /enemy ai_inquisitor starts the hunter that chases.",
            "- /enemy ambassador_diplomat, ambassador_cat, ambassador_cat_inner and ambassador_cat_outer start one piece of that fight on its own.",
            "- The train, the Sergeant Pup and the Five Star still come from their formations. Spawning the leader is enough.",
            "Press F1 on the Enemy AI tab for how to edit the flight, the guns and the transitions of one piece.")
    };
}
