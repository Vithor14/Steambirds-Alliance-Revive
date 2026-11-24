using System.Drawing.Drawing2D;
using SteambirdsAi;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.EnemyAi;

// What the simulator draws on top of the unit to explain the AI:
//   - the ways out of the current state (the conditions of its transitions): a circle for a distance, a mark on the health bar, a clock for
//     a time, a cone for "facing the player"..., each transition in a color of its own, and a small legend with all of them;
//   - the reach of the guns the state lets fire and the area where the player must be for them to shoot.
// Only a few conditions are drawn on the map (MaxDrawn), so a busy state does not fill the screen: the legend lists the rest.
internal static class AiSimOverlay
{
    public const int MaxDrawn = 6;
    private const int MaxIcons = 4;
    private const int MaxLegendLines = 4;

    private static readonly Color[] TransitionColors =
    {
        Color.FromArgb(240, 150, 40), Color.FromArgb(50, 190, 215), Color.FromArgb(225, 90, 175), Color.FromArgb(130, 200, 60)
    };

    // One condition of one transition of the current state.
    private sealed class Exit
    {
        public int Transition;
        public int Condition;
        public string Target = "";
        public string Type = "";
        public bool Not;
        public float Value;
        public float Max;
        public bool Drawn;
        public bool IsTrue;
        public Color Color;
    }

    // ------------------------------------------------------------------ the exits of the state

    private static List<Exit> Collect(AiSim sim) => Collect(sim, out _);

    private static List<Exit> Collect(AiSim sim, out int hidden)
    {
        hidden = 0;
        var list = new List<Exit>();
        AiCompiledState? state = sim.CurrentState;
        AiBrain? brain = sim.Brain;
        if (state == null || brain == null) return list;

        int drawn = 0;
        for (int t = 0; t < state.Transitions.Length; t++)
        {
            AiCompiledTransition tr = state.Transitions[t];
            string target = tr.To >= 0 && tr.To < brain.States.Length ? brain.States[tr.To].Id : "?";
            Color color = TransitionColors[t % TransitionColors.Length];
            if (tr.When.Length == 0)
            {
                list.Add(new Exit { Transition = t, Condition = -1, Target = target, Type = "always", Color = color, IsTrue = true, Drawn = false });
                continue;
            }
            for (int c = 0; c < tr.When.Length; c++)
            {
                AiCondition cond = tr.When[c];
                var exit = new Exit
                {
                    Transition = t, Condition = c, Target = target, Type = cond.Type, Not = cond.Not,
                    Value = cond.Type == "timeInState" ? sim.RolledValue(t, c) : cond.Value,
                    Max = cond.Max ?? 0f, Color = color
                };
                // "not within" is "beyond" and so on: drawn as the thing it really is.
                if (exit.Not)
                {
                    string opposite = exit.Type switch
                    {
                        "playerWithin" => "playerBeyond",
                        "playerBeyond" => "playerWithin",
                        "healthBelow" => "healthAbove",
                        "healthAbove" => "healthBelow",
                        _ => ""
                    };
                    if (opposite.Length > 0)
                    {
                        exit.Type = opposite;
                        exit.Not = false;
                    }
                }
                exit.IsTrue = Evaluate(sim, exit);
                bool drawable = IsDrawable(exit.Type) && (!exit.Not || IsIcon(exit.Type));
                if (drawable && drawn < MaxDrawn)
                {
                    exit.Drawn = true;
                    drawn++;
                }
                else if (drawable)
                {
                    hidden++;
                }
                list.Add(exit);
            }
        }
        return list;
    }

    private static bool IsIcon(string type) => type is "timeInState" or "recentlyHit" or "hasTarget" or "summonsAtMost" or "chance";

    private static bool IsDrawable(string type)
    {
        return type is "playerWithin" or "playerBeyond" or "healthBelow" or "healthAbove" or "timeInState" or "facingTarget" or "recentlyHit" or "hasTarget" or "summonsAtMost" or "chance";
    }

    // What can be known from the simulation alone (a chance is never "true" here).
    private static bool Evaluate(AiSim sim, Exit e)
    {
        bool has = sim.PlayerVisible && sim.PlayerAlive;
        float dist = AiMath.Distance(sim.X, sim.Y, sim.PlayerX, sim.PlayerY);
        float health = sim.MaxHealth > 0f ? sim.Health / sim.MaxHealth : 0f;
        bool result;
        switch (e.Type)
        {
            case "always": result = true; break;
            case "playerWithin": result = has && dist <= e.Value; break;
            case "playerBeyond": result = has && dist > e.Value; break;
            case "healthBelow": result = health <= e.Value; break;
            case "healthAbove": result = health >= e.Value; break;
            case "timeInState": result = sim.StateTime >= e.Value; break;
            case "hasTarget": result = has; break;
            case "summonsAtMost": result = sim.MinionsAlive <= e.Value; break;
            case "facingTarget":
                result = has && Math.Abs(AiMath.AngleDifference(sim.Rot, AiMath.Heading(sim.X, sim.Y, sim.PlayerX, sim.PlayerY))) <= e.Value;
                break;
            default: return false;
        }
        return e.Not ? !result : result;
    }

    private static string Describe(Exit e)
    {
        string v = e.Value.ToString("0.##");
        string text = e.Type switch
        {
            "always" => L.T("always"),
            "playerWithin" => L.F("player closer than {0}", v),
            "playerBeyond" => L.F("player farther than {0}", v),
            "healthBelow" => L.F("health at or under {0}%", (e.Value * 100f).ToString("0")),
            "healthAbove" => L.F("health at or over {0}%", (e.Value * 100f).ToString("0")),
            "timeInState" => L.F("{0}s in the state", v),
            "recentlyHit" => L.F("hit in the last {0}s", v),
            "facingTarget" => L.F("facing the player (within {0} degrees)", v),
            "hasTarget" => L.T("has a player"),
            "summonsAtMost" => L.F("summons alive at most {0}", v),
            "chance" => L.F("chance {0} per second", v),
            _ => e.Type + " " + v
        };
        return e.Not ? L.T("not") + " " + text : text;
    }

    // ------------------------------------------------------------------ drawn on the map

    // Circles, cones and icons around the unit. unitRadiusPx: the radius the unit is drawn with.
    public static void DrawConditions(Graphics g, AiSim sim, PointF at, float unitRadiusPx, float zoom, Font font, Theme.Palette p, Size canvas)
    {
        if (sim.Dead) return;
        List<Exit> exits = Collect(sim);
        if (exits.Count == 0) return;

        int icon = 0;
        foreach (Exit e in exits.Where(x => x.Drawn))
        {
            switch (e.Type)
            {
                case "playerWithin":
                case "playerBeyond":
                {
                    float r = Math.Max(2f, e.Value * zoom);
                    using var pen = new Pen(Color.FromArgb(e.IsTrue ? 235 : 150, e.Color), e.IsTrue ? 2.5f : 1.5f)
                    {
                        DashStyle = e.Type == "playerWithin" ? DashStyle.Solid : DashStyle.Dash
                    };
                    g.DrawEllipse(pen, at.X - r, at.Y - r, r * 2, r * 2);
                    string label = (e.Type == "playerWithin" ? "< " : "> ") + e.Value.ToString("0.#");
                    DrawLabel(g, label, font, new PointF(at.X, at.Y - r - 9f), e.Color, canvas);
                    break;
                }
                case "facingTarget":
                {
                    float r = Math.Max(40f, 14f * zoom);
                    float half = Math.Clamp(e.Value, 1f, 179f);
                    using var fill = new SolidBrush(Color.FromArgb(e.IsTrue ? 70 : 34, e.Color));
                    using var pen = new Pen(Color.FromArgb(e.IsTrue ? 230 : 150, e.Color), 1.5f);
                    var box = new RectangleF(at.X - r, at.Y - r, r * 2, r * 2);
                    g.FillPie(fill, box.X, box.Y, box.Width, box.Height, sim.Rot - 90f - half, half * 2f);
                    g.DrawPie(pen, box.X, box.Y, box.Width, box.Height, sim.Rot - 90f - half, half * 2f);
                    break;
                }
                case "timeInState":
                case "recentlyHit":
                case "hasTarget":
                case "summonsAtMost":
                case "chance":
                    if (icon < MaxIcons) DrawIcon(g, sim, e, new PointF(at.X + unitRadiusPx + 20f, at.Y - unitRadiusPx + icon * 24f), font, p);
                    icon++;
                    break;
            }
        }
    }

    // The marks of "health at or under / over" on the health bar of the unit.
    public static void DrawHealthMarks(Graphics g, AiSim sim, RectangleF bar, Font font)
    {
        if (sim.Dead) return;
        foreach (Exit e in Collect(sim).Where(x => x.Drawn && (x.Type == "healthBelow" || x.Type == "healthAbove")))
        {
            float x = bar.X + bar.Width * Math.Clamp(e.Value, 0f, 1f);
            using var pen = new Pen(Color.FromArgb(255, e.Color), e.IsTrue ? 3f : 2f);
            g.DrawLine(pen, x, bar.Y - 4f, x, bar.Bottom + 4f);
            string text = (e.Type == "healthBelow" ? "<" : ">") + (e.Value * 100f).ToString("0") + "%";
            Size size = TextRenderer.MeasureText(text, font);
            TextRenderer.DrawText(g, text, font, new Point((int)(x - size.Width / 2f), (int)(bar.Y - 4f - size.Height)), e.Color);
        }
    }

    // A small round icon with a text next to it (a clock for a time, "!" for a hit...).
    private static void DrawIcon(Graphics g, AiSim sim, Exit e, PointF center, Font font, Theme.Palette p)
    {
        const float r = 9f;
        var box = new RectangleF(center.X - r, center.Y - r, r * 2, r * 2);
        using (var back = new SolidBrush(Color.FromArgb(200, p.Input))) g.FillEllipse(back, box);
        string text;
        if (e.Type == "timeInState")
        {
            float fraction = e.Value > 0.01f ? Math.Clamp((float)sim.StateTime / e.Value, 0f, 1f) : 1f;
            using (var fill = new SolidBrush(Color.FromArgb(200, e.Color))) g.FillPie(fill, box.X, box.Y, box.Width, box.Height, -90f, 360f * fraction);
            text = sim.StateTime.ToString("0.0") + " / " + e.Value.ToString("0.0") + "s";
        }
        else
        {
            string mark = e.Type switch { "recentlyHit" => "!", "hasTarget" => "o", "summonsAtMost" => "#", _ => "?" };
            TextRenderer.DrawText(g, mark, font, Point.Round(new PointF(center.X - 4f, center.Y - 8f)), e.Color);
            text = Describe(e);
        }
        using (var pen = new Pen(Color.FromArgb(230, e.Color), e.IsTrue ? 2.5f : 1.5f)) g.DrawEllipse(pen, box);
        TextRenderer.DrawText(g, text, font, new Point((int)(center.X + r + 4f), (int)(center.Y - 8f)), e.Color);
    }

    private static void DrawLabel(Graphics g, string text, Font font, PointF center, Color color, Size canvas)
    {
        Size size = TextRenderer.MeasureText(text, font);
        float x = Math.Clamp(center.X - size.Width / 2f, 2f, Math.Max(2f, canvas.Width - size.Width - 2f));
        float y = Math.Clamp(center.Y - size.Height / 2f, 2f, Math.Max(2f, canvas.Height - size.Height - 2f));
        TextRenderer.DrawText(g, text, font, new Point((int)x, (int)y), color);
    }

    // ------------------------------------------------------------------ the legend

    // Every way out of the state, one line per transition (the first few), bottom left.
    public static void DrawLegend(Graphics g, AiSim sim, Font font, Theme.Palette p, Size canvas, int bottom)
    {
        if (sim.Dead) return;
        List<Exit> exits = Collect(sim, out int hidden);
        if (exits.Count == 0)
        {
            DrawLines(g, new List<(string, Color, bool)> { (L.T("No way out of this state: the AI stays in it."), p.Muted, false) }, font, p, canvas, bottom);
            return;
        }

        var lines = new List<(string Text, Color Color, bool Strong)> { (L.T("Ways out of this state:"), p.Muted, false) };
        var groups = exits.GroupBy(x => x.Transition).ToList();
        foreach (var group in groups.Take(MaxLegendLines))
        {
            Exit first = group.First();
            bool all = group.All(x => x.IsTrue);
            string conditions = string.Join("  +  ", group.Select(x => (x.IsTrue && x.Condition >= 0 ? "(ok) " : "") + Describe(x)));
            lines.Add((L.F("to {0}: {1}", first.Target, conditions), first.Color, all));
        }
        if (groups.Count > MaxLegendLines) lines.Add((L.F("and {0} more way(s) out", groups.Count - MaxLegendLines), p.Muted, false));
        if (hidden > 0) lines.Add((L.T("Only the first conditions are drawn on the map; the list above has all of them."), p.Muted, false));
        DrawLines(g, lines, font, p, canvas, bottom);
    }

    private static void DrawLines(Graphics g, List<(string Text, Color Color, bool Strong)> lines, Font font, Theme.Palette p, Size canvas, int bottom)
    {
        int lineHeight = font.Height + 2;
        int width = 0;
        foreach (var line in lines) width = Math.Max(width, TextRenderer.MeasureText(line.Text, font).Width);
        width = Math.Min(width + 12, Math.Max(60, canvas.Width - 12));
        int top = bottom - lines.Count * lineHeight - 4;
        using (var back = new SolidBrush(Color.FromArgb(185, p.Input))) g.FillRectangle(back, 6, top, width, lines.Count * lineHeight + 4);
        int y = top + 2;
        foreach (var line in lines)
        {
            using var bold = new Font(font, line.Strong ? FontStyle.Bold : FontStyle.Regular);
            TextRenderer.DrawText(g, line.Text, bold, new Rectangle(10, y, width - 8, lineHeight), line.Color,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            y += lineHeight;
        }
    }

    // ------------------------------------------------------------------ the guns

    // The reach of every gun the state lets fire, and the area where the player has to be for it to shoot. gunColor: the color of a gun
    // (the same as its shots), with an alpha.
    public static void DrawWeapons(Graphics g, AiSim sim, PointF at, float zoom, Font font, Func<int, int, Color> gunColor, Size canvas)
    {
        if (sim.Dead) return;
        int order = 0;
        foreach (AiGunStats gun in sim.ActiveGuns().Take(6))
        {
            float range = sim.WeaponRange(gun);
            float r = Math.Max(4f, range * zoom);
            float half = AiGunAim.FiringHalfWidth(gun.Mode, gun.Arc, sim.Tuning.FreeAim);
            float center = sim.Rot + gun.Angle;
            using var fill = new SolidBrush(gunColor(gun.Index, 30));
            using var pen = new Pen(gunColor(gun.Index, 190), 1.5f) { DashStyle = DashStyle.Dash };
            if (half >= 179f)
            {
                g.FillEllipse(fill, at.X - r, at.Y - r, r * 2, r * 2);
                g.DrawEllipse(pen, at.X - r, at.Y - r, r * 2, r * 2);
            }
            else
            {
                g.FillPie(fill, at.X - r, at.Y - r, r * 2, r * 2, center - 90f - half, half * 2f);
                g.DrawPie(pen, at.X - r, at.Y - r, r * 2, r * 2, center - 90f - half, half * 2f);
            }

            order++;
        }
    }

    // "range 30, ±60°" for the gun list of the HUD (the area on the map has the color of the gun).
    public static string GunSummary(AiSim sim, AiGunStats gun)
    {
        float half = AiGunAim.FiringHalfWidth(gun.Mode, gun.Arc, sim.Tuning.FreeAim);
        string where = half >= 179f ? L.T("all around") : L.F("±{0}° from where it points", ((int)MathF.Round(half)).ToString());
        return L.F("range {0}, {1}", sim.WeaponRange(gun).ToString("0"), where);
    }
}
