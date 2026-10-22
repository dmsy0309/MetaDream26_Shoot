using System;
using System.Collections.Generic;
using UnityEngine;
using Metadream.Shoot.Player;

namespace Metadream.Shoot.Maps.Scripting
{
    /// <summary>
    /// Every command and function a map script may call (the whitelist). Each one goes through MapScriptApi.
    /// To add one: add a Signature line, a case in Call, and a row in Docs/MAP_SCRIPTING.md.
    /// </summary>
    public static class MdsLibrary
    {
        public readonly struct Signature
        {
            public readonly int Min, Max;
            public readonly bool Command;
            public readonly string Usage;
            public Signature(int min, int max, bool command, string usage) { Min = min; Max = max; Command = command; Usage = usage; }
        }

        public static readonly Dictionary<string, Signature> Signatures = new Dictionary<string, Signature>(StringComparer.Ordinal)
        {
            // objects
            { "move", new Signature(3, 4, true, "move(object, x, y, seconds?)") },
            { "move_by", new Signature(3, 4, true, "move_by(object, dx, dy, seconds?)") },
            { "rotate", new Signature(2, 3, true, "rotate(object, degrees, seconds?)") },
            { "rotate_by", new Signature(2, 3, true, "rotate_by(object, degrees, seconds?)") },
            { "resize", new Signature(3, 4, true, "resize(object, w, h, seconds?)") },
            { "show", new Signature(1, 1, true, "show(object)") },
            { "hide", new Signature(1, 1, true, "hide(object)") },
            { "toggle", new Signature(1, 1, true, "toggle(object)") },
            { "color", new Signature(2, 5, true, "color(object, \"#ff8800\") or color(object, r, g, b, a?) with 0–1 values") },
            { "reset_color", new Signature(1, 1, true, "reset_color(object)") },
            // players
            { "damage", new Signature(2, 2, true, "damage(player, amount)") },
            { "heal", new Signature(2, 2, true, "heal(player, amount)") },
            { "push", new Signature(3, 3, true, "push(player, vx, vy)") },
            { "teleport", new Signature(3, 3, true, "teleport(player, x, y)") },
            { "kill", new Signature(1, 1, true, "kill(player)") },
            { "slow", new Signature(3, 3, true, "slow(player, factor, seconds)") },
            { "poison", new Signature(3, 3, true, "poison(player, damagePerSecond, seconds)") },
            // world
            { "explode", new Signature(4, 5, true, "explode(x, y, radius, damage, knockback?)") },
            { "shockwave", new Signature(4, 4, true, "shockwave(x, y, radius, strength)") },
            { "shoot", new Signature(4, 7, true, "shoot(x, y, dirX, dirY, speed?, damage?, gravity?)") },
            { "effect", new Signature(3, 5, true, "effect(\"ring\"|\"burst\"|\"spark\"|\"shake\"|\"death\", x, y, size?, \"#color\"?)") },
            { "sound", new Signature(1, 3, true, "sound(\"id\") or sound(\"id\", x, y)") },
            { "banner", new Signature(1, 3, true, "banner(\"text\", seconds?, \"#color\"?)") },
            { "hud", new Signature(1, 1, true, "hud(\"text\") — \"\" clears it") },
            { "log", new Signature(1, 1, true, "log(\"text\") — developer debug log only") },
            // functions
            { "random", new Signature(2, 2, false, "random(min, max) → decimal") },
            { "randint", new Signature(2, 2, false, "randint(min, max) → whole number, both included") },
            { "chance", new Signature(1, 1, false, "chance(0.25) → true 25% of the time") },
            { "min", new Signature(2, 2, false, "min(a, b)") },
            { "max", new Signature(2, 2, false, "max(a, b)") },
            { "abs", new Signature(1, 1, false, "abs(x)") },
            { "floor", new Signature(1, 1, false, "floor(x)") },
            { "ceil", new Signature(1, 1, false, "ceil(x)") },
            { "round", new Signature(1, 1, false, "round(x)") },
            { "sqrt", new Signature(1, 1, false, "sqrt(x)") },
            { "sin", new Signature(1, 1, false, "sin(degrees)") },
            { "cos", new Signature(1, 1, false, "cos(degrees)") },
            { "atan2", new Signature(2, 2, false, "atan2(y, x) → degrees") },
            { "clamp", new Signature(3, 3, false, "clamp(x, min, max)") },
            { "dist", new Signature(4, 4, false, "dist(x1, y1, x2, y2)") },
            { "players", new Signature(0, 0, false, "players() → number of living players") },
            { "alive", new Signature(1, 1, false, "alive(team) → living players of team 0 or 1") },
            { "nearest", new Signature(2, 2, false, "nearest(x, y) → closest living player or none") },
            { "random_player", new Signature(0, 0, false, "random_player() → a living player or none") },
            { "inside", new Signature(2, 2, false, "inside(player, object) → true/false") },
            { "text", new Signature(1, 2, false, "text(value, decimals?) → text") },
        };

        public static readonly HashSet<string> ObjectProps = new HashSet<string> { "x", "y", "rotation", "w", "h", "visible", "power", "dps", "fx", "fy", "name" };
        public static readonly HashSet<string> SettableObjectProps = new HashSet<string> { "x", "y", "rotation", "w", "h", "visible", "power", "dps", "fx", "fy" };
        public static readonly HashSet<string> PlayerProps = new HashSet<string> { "x", "y", "vx", "vy", "hp", "maxhp", "team", "id", "alive", "grounded", "name" };

        // ------------------------------------------------------------ properties

        internal static MdsValue GetProp(MdsRuntime rt, MdsValue target, string prop, int line)
        {
            if (target.Type == MdsType.Object)
            {
                var o = (MapObject)target.Ref;
                switch (prop)
                {
                    case "x": return MdsValue.Num(o.Position.x);
                    case "y": return MdsValue.Num(o.Position.y);
                    case "rotation": return MdsValue.Num(o.CurrentRotation);
                    case "w": return MdsValue.Num(o.Size.x);
                    case "h": return MdsValue.Num(o.Size.y);
                    case "visible": return MdsValue.Bool(o.Visible);
                    case "power": return MdsValue.Num(o.Power);
                    case "dps": return MdsValue.Num(o.Dps);
                    case "fx": return MdsValue.Num(o.Force.x);
                    case "fy": return MdsValue.Num(o.Force.y);
                    case "name": return MdsValue.Text(o.Name);
                }
                throw new MdsError(line, "objects have no '." + prop + "'");
            }
            if (target.Type == MdsType.Player)
            {
                var f = (Fighter)target.Ref;
                Vector2 p = rt.Api.ToLocal(f.Position);
                switch (prop)
                {
                    case "x": return MdsValue.Num(p.x);
                    case "y": return MdsValue.Num(p.y);
                    case "vx": return MdsValue.Num(f.Velocity.x);
                    case "vy": return MdsValue.Num(f.Velocity.y);
                    case "hp": return MdsValue.Num(f.Health);
                    case "maxhp": return MdsValue.Num(f.MaxHealth);
                    case "team": return MdsValue.Num(f.TeamId);
                    case "id": return MdsValue.Num(f.PlayerId);
                    case "alive": return MdsValue.Bool(!f.IsDead && f.IsPlaying);
                    case "grounded": return MdsValue.Bool(f.IsGrounded);
                    case "name": return MdsValue.Text(f.DisplayName);
                }
                throw new MdsError(line, "player has no '." + prop + "'");
            }
            throw new MdsError(line, "'." + prop + "' needs an object or a player but got " + target);
        }

        internal static void SetProp(MdsRuntime rt, MdsValue target, string prop, MdsValue value, int line)
        {
            if (target.Type != MdsType.Object) throw new MdsError(line, "only map objects have settable properties (use commands such as push/teleport for players)");
            var o = (MapObject)target.Ref;
            var api = rt.Api;
            if (prop == "visible") { api.SetVisible(o, value.Truthy); return; }
            float v = (float)rt.Number(value, line, "'." + prop + " ='");
            switch (prop)
            {
                case "x": api.Move(o, new Vector2(v, o.Position.y), 0f); break;
                case "y": api.Move(o, new Vector2(o.Position.x, v), 0f); break;
                case "rotation": api.Rotate(o, v, 0f); break;
                case "w": api.Resize(o, new Vector2(v, o.Size.y), 0f); break;
                case "h": api.Resize(o, new Vector2(o.Size.x, v), 0f); break;
                case "power": api.SetZone(o, v, null, null); break;
                case "dps": api.SetZone(o, null, v, null); break;
                case "fx": api.SetZone(o, null, null, new Vector2(v, o.Force.y)); break;
                case "fy": api.SetZone(o, null, null, new Vector2(o.Force.x, v)); break;
                default: throw new MdsError(line, "'." + prop + "' cannot be set");
            }
        }

        // ------------------------------------------------------------ calls

        internal static MdsValue Call(MdsRuntime rt, string name, MdsValue[] a, int line)
        {
            var api = rt.Api;
            float N(int i) => (float)rt.Number(a[i], line, name + "() argument " + (i + 1));
            float Opt(int i, float fallback) => a.Length > i ? N(i) : fallback;
            Vector2 V(int i) => new Vector2(N(i), N(i + 1));
            MapObject O(int i)
            {
                if (a[i].Type != MdsType.Object) throw new MdsError(line, name + "() argument " + (i + 1) + " must be a map object name");
                return (MapObject)a[i].Ref;
            }
            Fighter P(int i)
            {
                if (a[i].Type == MdsType.None) return null; // e.g. nearest() found nobody: do nothing
                if (a[i].Type != MdsType.Player) throw new MdsError(line, name + "() argument " + (i + 1) + " must be a player (player, nearest(…), random_player())");
                return (Fighter)a[i].Ref;
            }
            string T(int i) => a[i].Display();
            Color C(int i, Color fallback)
            {
                if (a.Length <= i) return fallback;
                if (ColorUtility.TryParseHtmlString(T(i), out var c)) return c;
                throw new MdsError(line, "'" + T(i) + "' is not a colour (use \"#rrggbb\" or a name such as \"red\")");
            }

            switch (name)
            {
                case "move": api.Move(O(0), V(1), Opt(3, 0f)); return MdsValue.None;
                case "move_by": api.MoveBy(O(0), V(1), Opt(3, 0f)); return MdsValue.None;
                case "rotate": api.Rotate(O(0), N(1), Opt(2, 0f)); return MdsValue.None;
                case "rotate_by": api.RotateBy(O(0), N(1), Opt(2, 0f)); return MdsValue.None;
                case "resize": api.Resize(O(0), V(1), Opt(3, 0f)); return MdsValue.None;
                case "show": api.SetVisible(O(0), true); return MdsValue.None;
                case "hide": api.SetVisible(O(0), false); return MdsValue.None;
                case "toggle": { var o = O(0); api.SetVisible(o, !o.Visible); return MdsValue.None; }
                case "color":
                    if (a.Length == 2) api.SetColor(O(0), C(1, Color.white));
                    else if (a.Length >= 4) api.SetColor(O(0), new Color(N(1), N(2), N(3), Opt(4, 1f)));
                    else throw new MdsError(line, "color(object, \"#hex\") or color(object, r, g, b, a?)");
                    return MdsValue.None;
                case "reset_color": api.ResetColor(O(0)); return MdsValue.None;

                case "damage": api.Damage(P(0), N(1)); return MdsValue.None;
                case "heal": api.Heal(P(0), N(1)); return MdsValue.None;
                case "push": api.Push(P(0), V(1)); return MdsValue.None;
                case "teleport": api.Teleport(P(0), V(1)); return MdsValue.None;
                case "kill": api.Kill(P(0)); return MdsValue.None;
                case "slow": api.Slow(P(0), N(1), N(2)); return MdsValue.None;
                case "poison": api.Poison(P(0), N(1), N(2)); return MdsValue.None;

                case "explode": api.Explode(V(0), N(2), N(3), Opt(4, 10f)); return MdsValue.None;
                case "shockwave": api.Shockwave(V(0), N(2), N(3)); return MdsValue.None;
                case "shoot": return MdsValue.Bool(api.Shoot(V(0), V(2), Opt(4, 40f), Opt(5, 20f), Opt(6, 0f)));
                case "effect":
                {
                    string kind = T(0);
                    if (kind != "ring" && kind != "burst" && kind != "spark" && kind != "shake" && kind != "death")
                        throw new MdsError(line, "effect kind must be \"ring\", \"burst\", \"spark\", \"shake\" or \"death\"");
                    api.Effect(kind, V(1), Opt(3, 2f), C(4, Color.white));
                    return MdsValue.None;
                }
                case "sound":
                    if (a.Length == 2) throw new MdsError(line, "sound(\"id\") or sound(\"id\", x, y)");
                    api.Sound(T(0), a.Length == 3 ? V(1) : (Vector2?)null);
                    return MdsValue.None;
                case "banner": api.Banner(T(0), Opt(1, 2f), C(2, Color.white)); return MdsValue.None;
                case "hud": api.Hud(T(0)); return MdsValue.None;
                case "log": api.Log(T(0)); return MdsValue.None;

                case "random": { double lo = N(0), hi = N(1); return MdsValue.Num(lo + rt.Rng.NextDouble() * (hi - lo)); }
                case "randint":
                {
                    int lo = Mathf.RoundToInt(N(0)), hi = Mathf.RoundToInt(N(1));
                    if (hi < lo) { int t = lo; lo = hi; hi = t; }
                    return MdsValue.Num(rt.Rng.Next(lo, hi + 1));
                }
                case "chance": return MdsValue.Bool(rt.Rng.NextDouble() < N(0));
                case "min": return MdsValue.Num(Math.Min(N(0), N(1)));
                case "max": return MdsValue.Num(Math.Max(N(0), N(1)));
                case "abs": return MdsValue.Num(Math.Abs(N(0)));
                case "floor": return MdsValue.Num(Math.Floor(N(0)));
                case "ceil": return MdsValue.Num(Math.Ceiling(N(0)));
                case "round": return MdsValue.Num(Math.Round(N(0), MidpointRounding.AwayFromZero));
                case "sqrt": return MdsValue.Num(Math.Sqrt(Math.Max(0f, N(0))));
                case "sin": return MdsValue.Num(Math.Sin(N(0) * Mathf.Deg2Rad));
                case "cos": return MdsValue.Num(Math.Cos(N(0) * Mathf.Deg2Rad));
                case "atan2": return MdsValue.Num(Math.Atan2(N(0), N(1)) * Mathf.Rad2Deg);
                case "clamp": return MdsValue.Num(Mathf.Clamp(N(0), N(1), N(2)));
                case "dist": return MdsValue.Num(Vector2.Distance(V(0), V(2)));
                case "players": return MdsValue.Num(api.Players.Count);
                case "alive": return MdsValue.Num(api.AliveInTeam(Mathf.RoundToInt(N(0))));
                case "nearest": return MdsValue.Plr(api.Nearest(V(0)));
                case "random_player":
                {
                    var list = api.Players;
                    return list.Count == 0 ? MdsValue.None : MdsValue.Plr(list[rt.Rng.Next(list.Count)]);
                }
                case "inside":
                {
                    var f = P(0);
                    var o = O(1);
                    return MdsValue.Bool(f != null && o.Contains(api.ToLocal(f.Position), f.Radius * 0.8f));
                }
                case "text":
                    if (a.Length == 2 && a[0].Type == MdsType.Number)
                        return MdsValue.Text(a[0].Number.ToString("F" + Mathf.Clamp(Mathf.RoundToInt(N(1)), 0, 6), System.Globalization.CultureInfo.InvariantCulture));
                    return MdsValue.Text(a[0].Display());
            }
            throw new MdsError(line, "unknown command '" + name + "'");
        }
    }
}

