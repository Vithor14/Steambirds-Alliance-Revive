using Lidgren.Network;

// Pilot skill tree and skill loadouts (Pilot School).
//
// Client side (PilotSchoolUI, Pilot.UpdateSkillsOnClient): the client asks and waits; the server answers with
// UpdatePlayerSkills(loadoutIndex, loadoutName, skills) and the client replaces that loadout and makes it active.
// On a refused change it gets ClientSkillChangeError(skillId) and shows "could not learn <skill>".
//
// Rules (Pilot.GetSkillChangeError, copied here):
//  - points available = pilot rank - 1 - points used (cost + costPerLevel x (level - 1) for each skill);
//  - the skill's group needs its requirements (pilot rank, class level reached, or/and/not);
//  - level up to maxLevel; going down is always allowed;
//  - a group holds ONE skill: learning one gives back the points of the other skill of the same group,
//    and that other skill leaves the loadout.
public static class PilotSkills
{
    private static Action<string, string> log = (_, _) => { };

    public static void Start(Action<string, string> log)
    {
        PilotSkills.log = log;
    }

    // ServerRequestSkillChange(string id, int targetLevel, float, float)
    public static void RequestSkillChange(NetServer server, NetConnection connection, string role, string skillId, int targetLevel)
    {
        lock (Game.Lock)
        {
            ProgressionSettings settings = ProgressionSettingsFile.Current;
            int slots = PilotProgression.SkillLoadoutSlots();
            bool validate = settings.ValidateSkills && ProgressionData.HasSkillData;

            (string? error, int levelBefore, int levelAfter, List<string> removed) = Accounts.Change(d =>
            {
                PilotSave pilot = d.Pilot;
                pilot.EnsureLoadouts(Math.Min(slots, Math.Max(1, pilot.SkillLoadoutIndex + 1)));
                Dictionary<string, int> skills = pilot.ActiveSkills();
                int current = skills.TryGetValue(skillId, out int have) ? have : 0;
                int target = Math.Max(0, targetLevel);

                string? problem = validate ? GetSkillChangeError(pilot, skillId, target) : null;
                if (problem == null && !validate && target > 99) problem = "absurd level";
                var gone = new List<string>();
                if (problem != null) return (problem, current, current, gone);

                if (target > current && ProgressionData.GroupBySkill.TryGetValue(skillId, out ProgressionData.SkillGroup? group))
                {
                    foreach (string other in group.Skills)
                    {
                        if (other != skillId && skills.Remove(other)) gone.Add(other);
                    }
                }
                if (target <= 0) skills.Remove(skillId);
                else skills[skillId] = target;
                return ((string?)null, current, target, gone);
            });

            if (error != null)
            {
                ProgressionMessages.Send(server, connection, m => ProgressionMessages.WriteClientSkillChangeError(m, skillId));
                log("SKILL", "[" + role + "] skill request '" + skillId + "' level " + targetLevel + " RECUSADO: " + error);
                return;
            }

            SendSkills(server, connection);
            PilotSave after = Accounts.Pilot();
            log("SKILL", "[" + role + "] skill '" + skillId + "' " + levelBefore + " -> " + levelAfter +
                (removed.Count > 0 ? " (left the group: " + string.Join(", ", removed) + ")" : "") +
                ", loadout " + (after.SkillLoadoutIndex + 1) + ", free points " + PointsAvailable(after) +
                (validate ? "" : " [not validated]"));
        }
    }

    // ServerRequestSkillLoadoutChange(int loadoutIndex, float, float). Also used right after buying a slot.
    public static void RequestLoadoutChange(NetServer server, NetConnection connection, string role, int index)
    {
        lock (Game.Lock)
        {
            int slots = PilotProgression.SkillLoadoutSlots();
            if (index < 0 || index >= slots)
            {
                log("SKILL", "[" + role + "] switch to loadout " + (index + 1) + " refused: there are only " + slots + " loadout(s) (progression.json: skillLoadoutSlots)");
                SendSkills(server, connection);
                return;
            }
            Accounts.Change(d =>
            {
                d.Pilot.EnsureLoadouts(index + 1);
                d.Pilot.SkillLoadoutIndex = index;
                return 0;
            });
            SendSkills(server, connection);
            log("SKILL", "[" + role + "] loadout " + (index + 1) + " active");
        }
    }

    // ServerRequestSkillLoadoutWipe(int loadoutIndex, float, float): forgets every skill of the loadout.
    public static void RequestLoadoutWipe(NetServer server, NetConnection connection, string role, int index)
    {
        lock (Game.Lock)
        {
            int slots = PilotProgression.SkillLoadoutSlots();
            if (index < 0 || index >= slots)
            {
                SendSkills(server, connection);
                return;
            }
            int forgotten = Accounts.Change(d =>
            {
                d.Pilot.EnsureLoadouts(index + 1);
                int count = d.Pilot.SkillLoadouts[index].Count;
                d.Pilot.SkillLoadouts[index].Clear();
                d.Pilot.SkillLoadoutIndex = index;
                return count;
            });
            SendSkills(server, connection);
            log("SKILL", "[" + role + "] loadout " + (index + 1) + " reset (" + forgotten + " skill(s) forgotten)");
        }
    }

    // ServerRequestSkillLoadoutRename(int loadoutIndex, string newName)
    public static void RequestLoadoutRename(NetServer server, NetConnection connection, string role, int index, string name)
    {
        lock (Game.Lock)
        {
            int slots = PilotProgression.SkillLoadoutSlots();
            if (index < 0 || index >= slots)
            {
                SendSkills(server, connection);
                return;
            }
            string clean = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (clean.Length > 32) clean = clean.Substring(0, 32);
            Accounts.Change(d =>
            {
                d.Pilot.EnsureLoadouts(index + 1);
                d.Pilot.SkillLoadoutNames[index] = clean;
                d.Pilot.SkillLoadoutIndex = index;
                return 0;
            });
            SendSkills(server, connection);
            log("SKILL", "[" + role + "] loadout " + (index + 1) + " renamed to '" + clean + "'");
        }
    }

    // UpdatePlayerSkills with the active loadout.
    public static void SendSkills(NetServer server, NetConnection connection)
    {
        PilotSave pilot = Accounts.Pilot();
        int index = pilot.SkillLoadoutIndex;
        pilot.EnsureLoadouts(index + 1);
        Dictionary<string, int> skills = pilot.SkillLoadouts[index];
        string name = pilot.SkillLoadoutNames[index] ?? "";
        ProgressionMessages.Send(server, connection, m => ProgressionMessages.WriteUpdatePlayerSkills(m, index, name, skills));
    }

    // ------------------------------------------------------------------ rules

    public static int PointsAvailable(PilotSave pilot)
    {
        return pilot.Level - 1 - PointsUsed(pilot.ActiveSkills());
    }

    // Pilot.GetSkillPointsUsed
    public static int PointsUsed(IReadOnlyDictionary<string, int> skills)
    {
        int used = 0;
        foreach (KeyValuePair<string, int> pair in skills)
        {
            if (ProgressionData.Skills.TryGetValue(pair.Key, out ProgressionData.SkillInfo? skill))
                used += skill.Cost + skill.GetCostPerLevel() * (pair.Value - 1);
        }
        return used;
    }

    // Pilot.GetSkillChangeError. null = allowed.
    private static string? GetSkillChangeError(PilotSave pilot, string skillId, int targetLevel)
    {
        Dictionary<string, int> skills = pilot.ActiveSkills();
        int current = skills.TryGetValue(skillId, out int have) ? have : 0;
        if (targetLevel < 0) targetLevel = 0;

        if (!ProgressionData.Skills.TryGetValue(skillId, out ProgressionData.SkillInfo? skill)) return "unknown skill";
        if (!ProgressionData.GroupBySkill.TryGetValue(skillId, out ProgressionData.SkillGroup? group)) return "skill outside the tree";
        if (!RequirementsMet(group.Requirements, pilot.Level, pilot.HighestLevels))
            return "requirements of group '" + group.Id + "' not met (rank " + pilot.Level + ")";
        if (targetLevel > skill.MaxLevel) return "maximum level is " + skill.MaxLevel;
        if (targetLevel < current) return null;

        int cost = 0;
        if (targetLevel > current)
        {
            foreach (string other in group.Skills)
            {
                if (other == skillId || !skills.TryGetValue(other, out int otherLevel)) continue;
                if (ProgressionData.Skills.TryGetValue(other, out ProgressionData.SkillInfo? otherSkill))
                    cost -= otherSkill.Cost + otherSkill.GetCostPerLevel() * (otherLevel - 1);
            }
        }

        int level = current;
        while (level != targetLevel)
        {
            if (level < targetLevel)
            {
                if (level <= 0)
                {
                    cost += skill.Cost;
                    level = 1;
                }
                else
                {
                    cost += skill.GetCostPerLevel();
                    level++;
                }
            }
            else if (targetLevel <= 0)
            {
                cost -= skill.Cost;
                level = 0;
            }
            else
            {
                cost -= skill.GetCostPerLevel();
                level--;
            }
        }

        int available = pilot.Level - 1 - PointsUsed(skills);
        if (available < cost) return "not enough points (costs " + cost + ", free " + available + ")";
        return null;
    }

    // Pilot.CheckGroupRequirementMatch: every set field must match; "or" lists need just one.
    public static bool RequirementsMet(List<ProgressionData.SkillRequirement>? requirements, int pilotLevel,
        IReadOnlyDictionary<string, int> highestLevels, bool booleanOr = false)
    {
        if (requirements == null) return true;
        foreach (ProgressionData.SkillRequirement r in requirements)
        {
            if (r.Or != null)
            {
                if (RequirementsMet(r.Or, pilotLevel, highestLevels, true)) { if (booleanOr) return true; }
                else if (!booleanOr) return false;
            }
            if (r.Not != null)
            {
                if (!RequirementsMet(r.Not, pilotLevel, highestLevels)) { if (booleanOr) return true; }
                else if (!booleanOr) return false;
            }
            if (r.And != null)
            {
                if (RequirementsMet(r.And, pilotLevel, highestLevels)) { if (booleanOr) return true; }
                else if (!booleanOr) return false;
            }
            if (r.PilotLevelAtLeast.HasValue)
            {
                if (pilotLevel >= r.PilotLevelAtLeast.Value) { if (booleanOr) return true; }
                else if (!booleanOr) return false;
            }
            if (r.ClassLevelPlaneClass != null)
            {
                int reached = highestLevels.TryGetValue(r.ClassLevelPlaneClass, out int level) ? level : 0;
                if (reached >= r.ClassLevel) { if (booleanOr) return true; }
                else if (!booleanOr) return false;
            }
        }
        return !booleanOr;
    }
}
