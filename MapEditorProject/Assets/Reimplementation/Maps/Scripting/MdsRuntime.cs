using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Metadream.Shoot.Combat;
using Metadream.Shoot.Player;

namespace Metadream.Shoot.Maps.Scripting
{
    public enum MdsType : byte { None, Number, Text, Object, Player }

    public readonly struct MdsValue
    {
        public readonly MdsType Type;
        public readonly double Number;
        public readonly object Ref;

        private MdsValue(MdsType type, double number, object reference)
        {
            Type = type;
            Number = number;
            Ref = reference;
        }

        public static readonly MdsValue None = default;
        public static MdsValue Num(double n) => new MdsValue(MdsType.Number, double.IsNaN(n) || double.IsInfinity(n) ? 0 : n, null);
        public static MdsValue Bool(bool b) => new MdsValue(MdsType.Number, b ? 1 : 0, null);
        public static MdsValue Text(string s) => new MdsValue(MdsType.Text, 0, s ?? "");
        public static MdsValue Obj(MapObject o) => o == null ? None : new MdsValue(MdsType.Object, 0, o);
        public static MdsValue Plr(Fighter f) => f == null ? None : new MdsValue(MdsType.Player, 0, f);

        public bool Truthy
        {
            get
            {
                switch (Type)
                {
                    case MdsType.Number: return Number != 0;
                    case MdsType.Text: return ((string)Ref).Length > 0;
                    case MdsType.None: return false;
                    default: return true;
                }
            }
        }

        public string Display()
        {
            switch (Type)
            {
                case MdsType.Number:
                    return Math.Abs(Number - Math.Round(Number)) < 1e-9 ? Math.Round(Number).ToString(CultureInfo.InvariantCulture) : Number.ToString("0.##", CultureInfo.InvariantCulture);
                case MdsType.Text: return (string)Ref;
                case MdsType.Object: return ((MapObject)Ref).Name;
                case MdsType.Player: return ((Fighter)Ref).DisplayName;
                default: return "none";
            }
        }

        public override string ToString() => Type == MdsType.Text ? "\"" + Display() + "\"" : Display();
    }

    // ---------------------------------------------------------------- syntax tree

    internal enum MdsFlow { Next, Stop }

    internal abstract class MdsExpr
    {
        public int Line;
        public abstract MdsValue Eval(MdsRuntime rt);
    }

    internal abstract class MdsStmt
    {
        public int Line;
        public abstract MdsFlow Exec(MdsRuntime rt);

        public static MdsFlow Run(MdsStmt[] block, MdsRuntime rt)
        {
            if (block == null) return MdsFlow.Next;
            for (int i = 0; i < block.Length; i++)
            {
                rt.Step(block[i].Line);
                if (block[i].Exec(rt) == MdsFlow.Stop) return MdsFlow.Stop;
            }
            return MdsFlow.Next;
        }
    }

    internal sealed class ConstExpr : MdsExpr
    {
        public MdsValue Value;
        public override MdsValue Eval(MdsRuntime rt) => Value;
    }

    internal sealed class VarExpr : MdsExpr
    {
        public int Slot;
        public override MdsValue Eval(MdsRuntime rt) => rt.Vars[Slot];
    }

    internal sealed class ObjExpr : MdsExpr
    {
        public int Index;
        public override MdsValue Eval(MdsRuntime rt) => MdsValue.Obj(rt.Object(Index));
    }

    internal sealed class BuiltinExpr : MdsExpr
    {
        public string Which;
        public override MdsValue Eval(MdsRuntime rt)
        {
            switch (Which)
            {
                case "time": return MdsValue.Num(rt.Api.Time);
                case "dt": return MdsValue.Num(rt.Dt);
                case "event_x": return MdsValue.Num(rt.EventPos.x);
                case "event_y": return MdsValue.Num(rt.EventPos.y);
                default: return MdsValue.Plr(rt.Player);
            }
        }
    }

    internal sealed class UnaryExpr : MdsExpr
    {
        public string Op;
        public MdsExpr A;
        public override MdsValue Eval(MdsRuntime rt)
        {
            var a = A.Eval(rt);
            if (Op == "not") return MdsValue.Bool(!a.Truthy);
            return MdsValue.Num(-rt.Number(a, Line, "'-'"));
        }
    }

    internal sealed class LogicExpr : MdsExpr
    {
        public bool Or;
        public MdsExpr A, B;
        public override MdsValue Eval(MdsRuntime rt)
        {
            bool a = A.Eval(rt).Truthy;
            if (Or ? a : !a) return MdsValue.Bool(a);
            return MdsValue.Bool(B.Eval(rt).Truthy);
        }
    }

    internal sealed class BinaryExpr : MdsExpr
    {
        public string Op;
        public MdsExpr A, B;
        public override MdsValue Eval(MdsRuntime rt)
        {
            var a = A.Eval(rt);
            var b = B.Eval(rt);
            switch (Op)
            {
                case "+":
                    if (a.Type == MdsType.Text || b.Type == MdsType.Text) return MdsValue.Text(a.Display() + b.Display());
                    return MdsValue.Num(rt.Number(a, Line, "'+'") + rt.Number(b, Line, "'+'"));
                case "-": return MdsValue.Num(rt.Number(a, Line, "'-'") - rt.Number(b, Line, "'-'"));
                case "*": return MdsValue.Num(rt.Number(a, Line, "'*'") * rt.Number(b, Line, "'*'"));
                case "/":
                {
                    double d = rt.Number(b, Line, "'/'");
                    if (d == 0) throw new MdsError(Line, "division by zero");
                    return MdsValue.Num(rt.Number(a, Line, "'/'") / d);
                }
                case "%":
                {
                    double d = rt.Number(b, Line, "'%'");
                    if (d == 0) throw new MdsError(Line, "division by zero");
                    double r = rt.Number(a, Line, "'%'") % d;
                    return MdsValue.Num(r < 0 ? r + Math.Abs(d) : r);
                }
                case "==": return MdsValue.Bool(Same(a, b));
                case "!=": return MdsValue.Bool(!Same(a, b));
                case "<": return MdsValue.Bool(rt.Number(a, Line, "'<'") < rt.Number(b, Line, "'<'"));
                case "<=": return MdsValue.Bool(rt.Number(a, Line, "'<='") <= rt.Number(b, Line, "'<='"));
                case ">": return MdsValue.Bool(rt.Number(a, Line, "'>'") > rt.Number(b, Line, "'>'"));
                default: return MdsValue.Bool(rt.Number(a, Line, "'>='") >= rt.Number(b, Line, "'>='"));
            }
        }

        private static bool Same(MdsValue a, MdsValue b)
        {
            if (a.Type != b.Type) return false;
            switch (a.Type)
            {
                case MdsType.Number: return Math.Abs(a.Number - b.Number) < 1e-9;
                case MdsType.Text: return (string)a.Ref == (string)b.Ref;
                case MdsType.None: return true;
                default: return ReferenceEquals(a.Ref, b.Ref);
            }
        }
    }

    internal sealed class PropExpr : MdsExpr
    {
        public MdsExpr Target;
        public string Name;
        public override MdsValue Eval(MdsRuntime rt) => MdsLibrary.GetProp(rt, Target.Eval(rt), Name, Line);
    }

    internal sealed class CallExpr : MdsExpr
    {
        public string Name;
        public MdsExpr[] Args;
        public override MdsValue Eval(MdsRuntime rt)
        {
            var values = new MdsValue[Args.Length];
            for (int i = 0; i < Args.Length; i++) values[i] = Args[i].Eval(rt);
            return MdsLibrary.Call(rt, Name, values, Line);
        }
    }

    internal sealed class AssignStmt : MdsStmt
    {
        public int Slot;
        public MdsExpr Value;
        public override MdsFlow Exec(MdsRuntime rt)
        {
            rt.Vars[Slot] = Value.Eval(rt);
            return MdsFlow.Next;
        }
    }

    internal sealed class SetPropStmt : MdsStmt
    {
        public MdsExpr Target;
        public string Name;
        public MdsExpr Value;
        public override MdsFlow Exec(MdsRuntime rt)
        {
            MdsLibrary.SetProp(rt, Target.Eval(rt), Name, Value.Eval(rt), Line);
            return MdsFlow.Next;
        }
    }

    internal sealed class IfStmt : MdsStmt
    {
        public MdsExpr[] Conds;
        public MdsStmt[][] Blocks;
        public MdsStmt[] Else;
        public override MdsFlow Exec(MdsRuntime rt)
        {
            for (int i = 0; i < Conds.Length; i++)
                if (Conds[i].Eval(rt).Truthy) return Run(Blocks[i], rt);
            return Run(Else, rt);
        }
    }

    internal sealed class RepeatStmt : MdsStmt
    {
        public const int MaxCount = 1000;
        public MdsExpr Count;
        public MdsStmt[] Body;
        public override MdsFlow Exec(MdsRuntime rt)
        {
            double n = rt.Number(Count.Eval(rt), Line, "'repeat'");
            if (n > MaxCount) throw new MdsError(Line, "repeat count " + n + " is more than " + MaxCount);
            for (int i = 0; i < (int)n; i++)
            {
                rt.Step(Line);
                if (Run(Body, rt) == MdsFlow.Stop) return MdsFlow.Stop;
            }
            return MdsFlow.Next;
        }
    }

    internal sealed class ForEachStmt : MdsStmt
    {
        public MdsStmt[] Body;
        public override MdsFlow Exec(MdsRuntime rt)
        {
            var saved = rt.Player;
            var list = new List<Fighter>(rt.Api.Players);
            try
            {
                foreach (var f in list)
                {
                    rt.Step(Line);
                    rt.Player = f;
                    if (Run(Body, rt) == MdsFlow.Stop) return MdsFlow.Stop;
                }
            }
            finally { rt.Player = saved; }
            return MdsFlow.Next;
        }
    }

    internal sealed class StopStmt : MdsStmt
    {
        public override MdsFlow Exec(MdsRuntime rt) => MdsFlow.Stop;
    }

    internal sealed class CallStmt : MdsStmt
    {
        public CallExpr Call;
        public override MdsFlow Exec(MdsRuntime rt)
        {
            Call.Eval(rt);
            return MdsFlow.Next;
        }
    }

    // ---------------------------------------------------------------- runtime

    /// <summary>
    /// Executes one MdsProgram for one map instance. Each handler run may take at most HandlerBudget steps and one
    /// physics frame at most FrameBudget; going over (or any runtime error) stops the script and reports the line.
    /// </summary>
    public sealed class MdsRuntime
    {
        public const int HandlerBudget = 5000, FrameBudget = 30000;

        private readonly MdsProgram _program;
        private readonly MapRoot _map;
        private readonly float[] _next;
        private readonly bool[] _done;
        private readonly Dictionary<int, HashSet<Fighter>> _inside = new Dictionary<int, HashSet<Fighter>>();
        private readonly Dictionary<Fighter, bool> _dead = new Dictionary<Fighter, bool>();
        private readonly HashSet<int> _areaObjects = new HashSet<int>();
        private readonly System.Random _rng = new System.Random(Environment.TickCount);
        private int _budget, _frameBudget;
        private bool _started;

        internal readonly MdsValue[] Vars;
        internal Fighter Player;
        internal Vector2 EventPos;
        internal float Dt;

        public MapScriptApi Api { get; }
        public string Error { get; private set; }
        public int Runs { get; private set; }
        public MdsProgram Program => _program;

        internal MdsRuntime(MdsProgram program, MapRoot map, MapScriptApi api)
        {
            _program = program;
            _map = map;
            Api = api;
            Vars = new MdsValue[program.VarNames.Length];
            _next = new float[program.Handlers.Count];
            _done = new bool[program.Handlers.Count];
            foreach (var h in program.Handlers)
                if (h.Trigger == MdsTrigger.Enter || h.Trigger == MdsTrigger.Exit || h.Trigger == MdsTrigger.Inside) _areaObjects.Add(h.Object);
        }

        internal System.Random Rng => _rng;
        internal MapObject Object(int namedIndex) => _map.NamedObject(namedIndex);

        public string VariableSummary(int max = 6)
        {
            var parts = new List<string>();
            for (int i = 0; i < Vars.Length && i < max; i++) parts.Add(_program.VarNames[i] + "=" + Vars[i]);
            if (Vars.Length > max) parts.Add("…");
            return string.Join(" ", parts);
        }

        internal void Step(int line)
        {
            if (--_budget < 0 || --_frameBudget < 0)
                throw new MdsError(line, "too many steps (limit " + HandlerBudget + " per handler, " + FrameBudget + " per frame) — check repeat counts and every-intervals");
        }

        internal double Number(MdsValue v, int line, string what)
        {
            if (v.Type == MdsType.Number) return v.Number;
            throw new MdsError(line, what + " needs a number but got " + (v.Type == MdsType.None ? "none" : v.Type.ToString().ToLowerInvariant() + " " + v));
        }

        /// <summary>Runs one physics step of the script (offline / host / server only).</summary>
        public void Tick(float dt)
        {
            if (Error != null) return;
            _frameBudget = FrameBudget;
            if (!_started)
            {
                _started = true;
                for (int i = 0; i < Vars.Length; i++)
                {
                    if (_program.VarInit[i] == null) continue;
                    _budget = HandlerBudget;
                    try { Vars[i] = _program.VarInit[i].Eval(this); }
                    catch (MdsError e) { Fail(e); return; }
                }
                for (int i = 0; i < _program.Handlers.Count; i++)
                {
                    var h = _program.Handlers[i];
                    _next[i] = h.Seconds;
                    if (h.Trigger == MdsTrigger.Start) Run(h, null, Vector2.zero, 0f);
                }
            }
            float now = Api.Time;
            for (int i = 0; i < _program.Handlers.Count && Error == null; i++)
            {
                var h = _program.Handlers[i];
                switch (h.Trigger)
                {
                    case MdsTrigger.Every:
                        for (int guard = 0; guard < 4 && now >= _next[i] && Error == null; guard++)
                        {
                            _next[i] += h.Seconds;
                            Run(h, null, Vector2.zero, 0f);
                        }
                        if (now >= _next[i]) _next[i] = now + h.Seconds; // skip missed beats after a long frame
                        break;
                    case MdsTrigger.After:
                        if (!_done[i] && now >= _next[i]) { _done[i] = true; Run(h, null, Vector2.zero, 0f); }
                        break;
                    case MdsTrigger.Tick:
                        Run(h, null, Vector2.zero, dt);
                        break;
                }
            }
            if (Error == null && _areaObjects.Count > 0) TickAreas(dt);
            if (Error == null) TickDeaths();
        }

        private void TickAreas(float dt)
        {
            var players = Api.Players;
            foreach (int index in _areaObjects)
            {
                var o = Object(index);
                if (o == null) continue;
                if (!_inside.TryGetValue(index, out var set)) _inside[index] = set = new HashSet<Fighter>();
                var now = new HashSet<Fighter>();
                foreach (var f in players)
                    if ((o.Kind == MapObjectKind.Area || o.Visible) && o.Contains(Api.ToLocal(f.Position), f.Radius * 0.8f)) now.Add(f);
                foreach (var f in now)
                    if (!set.Contains(f)) Fire(MdsTrigger.Enter, index, f, 0f);
                foreach (var f in set)
                    if (!now.Contains(f)) Fire(MdsTrigger.Exit, index, f, 0f);
                foreach (var f in now) Fire(MdsTrigger.Inside, index, f, dt);
                _inside[index] = now;
                if (Error != null) return;
            }
        }

        private void TickDeaths()
        {
            foreach (var f in Fighter.Active)
            {
                if (f == null) continue;
                bool dead = f.IsDead;
                if (_dead.TryGetValue(f, out bool was) && dead && !was) Fire(MdsTrigger.Death, -1, f, 0f);
                _dead[f] = dead;
                if (Error != null) return;
            }
        }

        private void Fire(MdsTrigger trigger, int objectIndex, Fighter f, float dt)
        {
            foreach (var h in _program.Handlers)
            {
                if (h.Trigger != trigger || (objectIndex >= 0 && h.Object != objectIndex)) continue;
                Run(h, f, f != null ? Api.ToLocal(f.Position) : Vector2.zero, dt);
                if (Error != null) return;
            }
        }

        /// <summary>A projectile hit a named object (called by MapRoot).</summary>
        public void OnShot(MapObject o, Projectile p, Vector2 worldPoint)
        {
            if (Error != null || !_started || o == null || o.NamedIndex < 0) return;
            _frameBudget = Mathf.Max(_frameBudget, HandlerBudget);
            foreach (var h in _program.Handlers)
            {
                if (h.Trigger != MdsTrigger.Shot || h.Object != o.NamedIndex) continue;
                Run(h, p != null ? p.Owner : null, Api.ToLocal(worldPoint), 0f);
                if (Error != null) return;
            }
        }

        private void Run(MdsHandler h, Fighter player, Vector2 eventPos, float dt)
        {
            _budget = HandlerBudget;
            Player = player;
            EventPos = eventPos;
            Dt = dt;
            Runs++;
            try { MdsStmt.Run(h.Body, this); }
            catch (MdsError e) { Fail(e); }
            catch (Exception e) { Fail(new MdsError(h.Line, "internal error: " + e.Message)); }
            finally { Player = null; }
        }

        private void Fail(MdsError e)
        {
            Error = e.ToString();
            Debug.LogWarning("[MapScript] " + _map.Definition.Id + " stopped — " + Error);
        }
    }
}

