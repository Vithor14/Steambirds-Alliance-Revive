using SteambirdsAi;
using SteambirdsLauncher.Editor;

namespace SteambirdsLauncher.EnemyAi;

// Turns what the Enemy AI tab knows about a unit (an AiUnitChoice: a unit type or a plain game unit) into what the simulator needs to fly
// it: the unit's stats, the settings of its type and the AI it runs. Used for the leader and the summoned units of the edited unit.
internal static class AiSimSetups
{
    // When there is no game data (units_config.txt): a plane with one gun, so the AI can still be tried.
    public static AiUnitStats TestPlane()
    {
        var stats = new AiUnitStats { Name = "test plane", MaxHealth = 100f, VelMin = 4f, VelMax = 9f, Turn = 120f, Radius = 1.2f };
        stats.Guns.Add(new AiGunStats { Index = 0, Name = "gun0", FireRate = 3f, Burst = 3, Reload = 1.5f, Damage = 1f, Speed = 18f, Duration = 1.4f, Range = 22f, Multi = 1 });
        return stats;
    }

    // The AI a unit really runs: the "ai" of its type, else the generic AI of its built-in behavior (chase when it has none, as in the open world).
    public static string AiIdOf(AiUnitChoice choice)
    {
        if (choice.OwnAi.Length > 0) return choice.OwnAi;
        string behavior = choice.BuiltInBehavior.Length > 0 ? choice.BuiltInBehavior : "chase";
        return "generic_" + behavior;
    }

    public static AiSimSetup Build(AiUnitChoice choice, Func<string, AiBrain?> brainOf)
    {
        AiUnitStats stats = choice.UnitName.Length > 0 && EditorAssets.Current.Units.TryGetValue(choice.UnitName, out GameUnit? unit)
            ? unit.ToStats()
            : TestPlane();
        string flight = string.IsNullOrWhiteSpace(choice.BuiltInBehavior) ? "chase" : choice.BuiltInBehavior.Trim().ToLowerInvariant();
        return new AiSimSetup
        {
            Label = choice.IsType ? choice.TypeName : choice.UnitName.Length > 0 ? choice.UnitName : choice.Label,
            Unit = stats,
            Tuning = new AiSimTuning
            {
                SpeedMult = choice.SpeedMult,
                TurnMult = choice.TurnMult,
                FireRateMult = choice.FireRateMult,
                AimLead = choice.AimLead,
                FreeAim = choice.FreeAim,
                Builtin = flight
            },
            Brain = brainOf(AiIdOf(choice))
        };
    }
}
