using Lidgren.Network;

// Conditions and actions of the script (see MapScript.cs for what each field means).
public sealed partial class MapScriptRunner
{
    // ================================================================== conditions

    private bool Eval(MapScript.CondDef c)
    {
        if (c.Line.Length > 0 && !(players.Count > 0 && playersMaxB >= map.GetLineB(c.Line))) return false;

        if (c.Done.Length > 0)
        {
            if (!done.TryGetValue(c.Done, out double at)) return false;
            if (now < at + c.AfterSeconds) return false;
        }
        if (c.Unless.Length > 0 && done.ContainsKey(c.Unless)) return false;

        if (c.GateOpen.Length > 0 && !host.GateOpen(c.GateOpen)) return false;
        if (c.GateNear != null)
        {
            string gate = c.GateNear.Gate;
            if (host.GateOpen(gate) || !host.GateDamageable(gate)) return false;
            if (players.Count == 0 || playersMaxB < host.GateB(gate) - c.GateNear.Distance) return false;
        }

        if (c.Cleared.Length > 0 && !(attemptedGroups.Contains(c.Cleared) && Enemies.CountAlive(host.Connection, c.Cleared) == 0)) return false;
        if (c.Alive.Length > 0 && Enemies.CountAlive(host.Connection, c.Alive) == 0) return false;
        if (c.AliveAtMost != null && Enemies.CountAlive(host.Connection, c.AliveAtMost.Group) > c.AliveAtMost.Count) return false;

        if (c.Dead.Length > 0)
        {
            if (!aliasIds.TryGetValue(c.Dead, out int id) || Enemies.IsAlive(host.Connection, id)) return false;
        }
        if (c.Failed.Length > 0 && !failedAliases.Contains(c.Failed)) return false;
        if (c.Awake.Length > 0 && !Enemies.GroupAwake(host.Connection, c.Awake)) return false;

        if (c.Within != null)
        {
            map.GetPoint(c.Within.Point).World(out float px, out float py);
            float radius2 = c.Within.Radius * c.Within.Radius;
            bool near = false;
            foreach ((float x, float y) in players)
            {
                if ((x - px) * (x - px) + (y - py) * (y - py) <= radius2)
                {
                    near = true;
                    break;
                }
            }
            if (!near) return false;
        }

        if (c.RoomCleared.Length > 0 && !host.RoomCleared(c.RoomCleared)) return false;
        if (c.TypeDead.Length > 0 && !host.TypeDead(c.TypeDead)) return false;

        if (c.Any != null && c.Any.Count > 0 && !c.Any.Any(Eval)) return false;
        if (c.All != null && !c.All.All(Eval)) return false;
        if (c.Not != null && Eval(c.Not)) return false;
        return true;
    }

    // ================================================================== actions

    private void Execute(MapScript.ActionDef a, double time)
    {
        now = time;
        if (a.Unless.Length > 0 && done.ContainsKey(a.Unless)) return;
        if (a.If != null && !Eval(a.If)) return;

        if (a.Say.Length > 0)
        {
            string by = a.By.Length > 0 ? a.By : "instructor";
            int unit = a.ByGate.Length > 0 ? host.GateSpeaker(a.ByGate) : Resolve(by);
            if (unit == 0) return;
            string portrait = a.Portrait;
            if (portrait.Length == 0 && a.ByGate.Length == 0 && def.Speakers != null && def.Speakers.TryGetValue(by, out string? known)) portrait = known;
            host.Speak(unit, a.Say, portrait, !string.Equals(a.Mode, "bubble", StringComparison.OrdinalIgnoreCase));
        }
        else if (a.Chat.Length > 0)
        {
            host.Chat(a.Chat);
        }
        else if (a.Sound.Length > 0)
        {
            host.PlaySound(a.Sound);
        }
        else if (a.Guide.Length > 0)
        {
            Maps.Point p = map.GetPoint(a.Guide);
            p.World(out guideX, out guideY);
            guideSet = true;
            if (a.Who.Length > 0) guideWho = a.Who;
            guideUntil = time + a.Seconds;
            guideUntilRule = a.Until;
        }
        else if (a.Wake.Length > 0)
        {
            WakeGroup(a, time);
        }
        else if (a.Quiet.Length > 0)
        {
            QuietGroup(a.Quiet);
        }
        else if (a.Area.Length > 0)
        {
            Enemies.SetDefaultArea(host.Server, host.Connection, host.Role, map.GetArea(a.Area), time);
        }
        else if (a.Stage.HasValue)
        {
            Stage = a.Stage.Value;
            host.Log("[script] step " + Stage + " (" + StageName() + ")");
        }
        else if (a.Unlock.Length > 0)
        {
            host.GateUnlock(a.Unlock, time);
        }
        else if (a.Open.Length > 0)
        {
            if (!host.GateOpen(a.Open)) host.GateOpenNow(a.Open, a.Explode, time);
        }
        else if (a.Spawn.Length > 0)
        {
            MapScript.SpawnDef? sp = (def.Spawns ?? new List<MapScript.SpawnDef>()).FirstOrDefault(s => string.Equals(s.Name, a.Spawn, StringComparison.OrdinalIgnoreCase));
            if (sp != null) RunSpawn(sp, time);
        }
        else if (a.Remove.Length > 0)
        {
            Enemies.RemoveGroup(host.Connection, a.Remove, a.Explode, time);
        }
        else if (a.Portal.Length > 0)
        {
            host.ExitPortal(a.Portal, a.AutoExitSeconds, time);
        }
        else if (a.Log.Length > 0)
        {
            host.Log("[script] " + a.Log);
        }
    }

    // Wakes a group; with "stagger" one unit every that many seconds (the hangar turrets start one after another).
    private void WakeGroup(MapScript.ActionDef a, double time)
    {
        if (a.Stagger <= 0f || !groupIds.TryGetValue(a.Wake, out List<int>? ids))
        {
            Enemies.WakeGroup(host.Connection, a.Wake, time);
            return;
        }
        for (int i = 0; i < ids.Count; i++)
        {
            int id = ids[i];
            double when = time + a.Stagger * i;
            string unless = a.Unless;
            Act(when, t =>
            {
                if (unless.Length > 0 && done.ContainsKey(unless)) return;
                Enemies.WakeUnit(host.Connection, id, t);
            });
        }
    }

    private void Act(double when, Action<double> act)
    {
        pending.Add(new Pending { When = when, Act = act });
    }
}
