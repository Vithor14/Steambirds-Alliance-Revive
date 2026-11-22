using System.Globalization;
using Lidgren.Network;

// Client -> server messages of the pilot progression, and the chat commands to test it.
// Program.cs calls Handle before Game.Handle; Game calls Command for the "/" commands.
public static class ProgressionHandler
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const ushort MsgServerReceivePortrait = 12150;            // Portrait
    private const ushort MsgServerRequestSkillLoadoutRename = 12152;  // int loadoutIndex, string newName
    private const ushort MsgServerRetirePlane = 12153;                // string characterId (hangar "Retire plane")
    private const ushort MsgServerRecordLevelupUIShown = 12158;       // int lastXPLevelupShown, int lastLevelupShown
    private const ushort MsgServerRequestVagueCareerRewards = 12159;
    private const ushort MsgServerRequestCareerRewardDelivery = 12161;
    private const ushort MsgServerReportPlayerSkillTrigger = 13947;   // byte trigger, float amount, float, float
    private const ushort MsgServerReportPlaneLevel = 13948;           // int level, float, float
    private const ushort MsgServerReportDamageBoost = 13949;          // float boostFactor, float boostClock, float, float
    private const ushort MsgServerReportCloudHeal = 27910;            // int cloud, int unit, float effect, float heal, float, float
    private const ushort MsgServerAuditBuffAction = 48947;            // int auditId, float, float
    private const ushort MsgServerRequestSkillChange = 65039;         // string id, int targetLevel, float, float
    private const ushort MsgServerRequestSkillLoadoutChange = 65042;  // int loadoutIndex, float, float
    private const ushort MsgServerRequestSkillLoadoutWipe = 65044;    // int loadoutIndex, float, float

    // Messages that arrive many times: counted, logged the first times and then every 50.
    private static readonly Dictionary<string, int> counters = new Dictionary<string, int>();

    // Returns true when the message was handled here.
    public static bool Handle(NetServer server, NetIncomingMessage msg, ushort id, string role, double now, Action<string, string> log)
    {
        NetConnection connection = msg.SenderConnection;
        string tag = "[" + role + "]";
        switch (id)
        {
            case MsgServerRetirePlane:
                PlaneRetirement.Retire(server, connection, role, msg.ReadString() ?? "");
                return true;

            case MsgServerRecordLevelupUIShown:
            {
                int lastXp = msg.ReadInt32();
                int last = msg.ReadInt32();
                PilotProgression.LevelupUiShown(role, lastXp, last);
                return true;
            }
            case MsgServerRequestVagueCareerRewards:
                PilotExtras.SendVagueCareerRewards(server, connection, role);
                return true;

            case MsgServerRequestCareerRewardDelivery:
                log("CAREER", tag + " ServerRequestCareerRewardDelivery");
                lock (Game.Lock) PilotExtras.DeliverCareerRewards(server, connection, role);
                return true;

            case MsgServerRequestSkillChange:
            {
                string skill = msg.ReadString() ?? "";
                int target = msg.ReadInt32();
                PilotSkills.RequestSkillChange(server, connection, role, skill, target);
                return true;
            }
            case MsgServerRequestSkillLoadoutChange:
                PilotSkills.RequestLoadoutChange(server, connection, role, msg.ReadInt32());
                return true;

            case MsgServerRequestSkillLoadoutWipe:
                PilotSkills.RequestLoadoutWipe(server, connection, role, msg.ReadInt32());
                return true;

            case MsgServerRequestSkillLoadoutRename:
            {
                int index = msg.ReadInt32();
                string name = msg.ReadString() ?? "";
                PilotSkills.RequestLoadoutRename(server, connection, role, index, name);
                return true;
            }
            case MsgServerReceivePortrait:
                lock (Game.Lock) PilotExtras.PortraitReceived(server, connection, role, msg);
                return true;

            case MsgServerAuditBuffAction:
            {
                int auditId = msg.ReadInt32();
                log("BUFF", tag + " ServerAuditBuffAction: the client applied the buff change " + auditId);
                return true;
            }
            case MsgServerReportPlayerSkillTrigger:
            {
                int trigger = msg.ReadByte();
                float amount = msg.ReadSingle();
                string name = CopperReader.Name(CopperReader.TriggerNames, trigger);
                int count = Count(role + " trigger " + name);
                if (count <= 3 || count % 50 == 0)
                    log("SKILL", tag + " pilot skill fired on the client: " + name + " (" + CopperReader.Num(amount) + ") x" + count);
                return true;
            }
            case MsgServerReportDamageBoost:
            {
                float factor = msg.ReadSingle();
                float clock = msg.ReadSingle();
                int count = Count(role + " extra damage");
                if (count <= 3 || count % 50 == 0)
                    log("SKILL", tag + " extra damage received from an ally: x" + CopperReader.Num(factor) + " for " + CopperReader.Num(clock) + "s (x" + count + ")");
                return true;
            }
            case MsgServerReportCloudHeal:
            {
                int cloud = msg.ReadInt32();
                int unit = msg.ReadInt32();
                msg.ReadSingle();                // effect
                float heal = msg.ReadSingle();
                // In multiplayer the heal of another player's plane would go to that player with IncrementPlayerStats.
                // Alone, the healed plane is the player's own and the client already healed it.
                int count = Count(role + " heal");
                if (count <= 3 || count % 100 == 0)
                    log("SKILL", tag + " cloud " + cloud + " healed the unit " + unit + " in " + CopperReader.Num(heal) + " (x" + count + ")");
                return true;
            }
            case MsgServerReportPlaneLevel:
            {
                int level = msg.ReadInt32();
                string? characterId = Game.GetPlaneCharacterId(connection);
                Accounts.SavedPlane? plane = Accounts.GetPlane(characterId);
                if (plane != null && plane.Level != level)
                    log("XP", tag + " the client says level " + level + " and the account has " + plane.Level + " for the plane " + plane.Class);
                return true;
            }
        }
        return false;
    }

    private static int Count(string key)
    {
        lock (counters)
        {
            counters.TryGetValue(key, out int value);
            value++;
            counters[key] = value;
            return value;
        }
    }

    // ------------------------------------------------------------------ chat commands

    // "/" commands of the progression. Returns false if the command is not one of them.
    public static bool Command(NetServer server, NetConnection? connection, string role, List<string> pieces, double now, Action<string, string> log)
    {
        if (connection == null || pieces.Count == 0) return false;
        string command = pieces[0].Trim().ToLowerInvariant();
        if (!command.StartsWith("/")) command = "/" + command;
        string? argument = pieces.Count > 1 ? pieces[1].Trim() : null;
        int number = argument != null && int.TryParse(argument, NumberStyles.Integer, Inv, out int parsed) ? parsed : 0;

        switch (command)
        {
            case "/progression":
                Chat(server, connection, "Progression: /pilot (status), /xp N, /level N (plane), /rank N (pilot), /reset_pilot, " +
                    "/buff ID, /buffs, /no_buffs, /portrait. Host settings in progression.json.");
                return true;

            case "/pilot":
                lock (Game.Lock) Chat(server, connection, PilotProgression.StatusText(connection));
                return true;

            case "/xp":
            {
                string? characterId = Game.GetPlaneCharacterId(connection);
                if (characterId == null || number <= 0)
                {
                    Chat(server, connection, characterId == null ? "You need a live plane." : "Use /xp N (N greater than 0).");
                    return true;
                }
                int added = PilotProgression.AwardPlaneXp(server, connection, role, characterId, number, false, "/xp");
                Chat(server, connection, "+" + added + " XP to the plane.");
                return true;
            }
            case "/level":
            {
                string? characterId = Game.GetPlaneCharacterId(connection);
                if (characterId == null || number <= 0)
                {
                    Chat(server, connection, characterId == null ? "You need a live plane." :
                        "Use /level N (1 to " + ProgressionData.PlaneLevels.TopLevel + ").");
                    return true;
                }
                bool done = PilotProgression.SetPlaneLevel(server, connection, role, characterId, number);
                Chat(server, connection, done ? "Plane at level " + Math.Clamp(number, 1, ProgressionData.PlaneLevels.TopLevel) + "." : "The plane is not in the account.");
                return true;
            }
            case "/rank":
                if (number <= 0)
                {
                    Chat(server, connection, "Use /rank N (1 to " + ProgressionData.PilotLevels.TopLevel + ").");
                    return true;
                }
                PilotProgression.SetPilotLevel(server, connection, role, number);
                Chat(server, connection, "Pilot at rank " + Math.Clamp(number, 1, ProgressionData.PilotLevels.TopLevel) +
                    ". Open the Pilot School to spend the points.");
                return true;

            case "/reset_pilot":
                PilotProgression.WipePilot(server, connection, role);
                Chat(server, connection, "Pilot reset (rank, XP and skills).");
                return true;

            case "/buff":
                if (string.IsNullOrEmpty(argument))
                {
                    Chat(server, connection, "Use /buff ID. " + PilotExtras.BuffListText());
                    return true;
                }
                lock (Game.Lock) Chat(server, connection, PilotExtras.AddBuff(server, connection, role, argument));
                return true;

            case "/buffs":
                Chat(server, connection, PilotExtras.BuffListText());
                return true;

            case "/no_buffs":
                lock (Game.Lock) PilotExtras.ClearBuffs(server, connection, role);
                Chat(server, connection, "Buffs removed.");
                return true;

            case "/portrait":
                bool rolled;
                lock (Game.Lock) rolled = PilotExtras.RerollPortrait(server, connection, role);
                Chat(server, connection, rolled ? "New portrait." : "No portrait pieces: portraits_config.txt is missing (plugin 1.20.0).");
                return true;
        }
        return false;
    }

    private static void Chat(NetServer server, NetConnection connection, string text)
    {
        WorldObjects.SendChat(server, connection, text);
    }
}
