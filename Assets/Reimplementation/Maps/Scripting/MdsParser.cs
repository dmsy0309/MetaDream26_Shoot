using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Metadream.Shoot.Maps.Scripting
{
    /// <summary>Syntax or runtime error in a map script, with its 1-based line inside the "script" array.</summary>
    public sealed class MdsError : Exception
    {
        public readonly int Line;
        public MdsError(int line, string message) : base(message) { Line = line; }
        public override string ToString() => "script line " + Line + ": " + Message;
    }

    public enum MdsTrigger { Start, Tick, Every, After, Enter, Exit, Inside, Death, Shot }

    public sealed class MdsHandler
    {
        public MdsTrigger Trigger;
        public float Seconds;
        /// <summary>Named object index for enter/exit/inside/shot.</summary>
        public int Object = -1;
        internal MdsStmt[] Body;
        public int Line;
    }

    /// <summary>
    /// A parsed map script ("MDS"). Immutable and shared by every instance of the map; MdsRuntime holds the state.
    /// The language has no access to anything except the commands in MdsLibrary (game actions only).
    /// </summary>
    public sealed class MdsProgram
    {
        public string[] VarNames = new string[0];
        internal MdsExpr[] VarInit = new MdsExpr[0];
        public readonly List<MdsHandler> Handlers = new List<MdsHandler>();
        public int LineCount;

        /// <summary>Parses the script. Returns null and fills errors when anything is wrong.</summary>
        public static MdsProgram Parse(string[] lines, IList<string> objectNames, List<MdsError> errors)
        {
            var p = new MdsParser(lines ?? new string[0], objectNames ?? new string[0], errors);
            var program = p.Run();
            return errors.Count == 0 ? program : null;
        }
    }

    internal enum TokKind { Number, Text, Ident, Op }

    internal struct Token
    {
        public TokKind Kind;
        public string S;
        public double N;
    }

    internal sealed class MdsParser
    {
        private static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "on", "every", "after", "var", "if", "elif", "else", "end", "repeat", "foreach", "stop", "and", "or", "not",
            "true", "false", "none", "player", "time", "dt", "event_x", "event_y", "start", "tick", "enter", "exit", "inside", "death", "shot"
        };

        private readonly string[] _src;
        private readonly List<MdsError> _errors;
        private readonly Dictionary<string, int> _objects = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _vars = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<KeyValuePair<int, List<Token>>> _lines = new List<KeyValuePair<int, List<Token>>>();
        private int _i;
        private List<Token> _t;
        private int _p;
        private int _line;
        private bool _playerContext;
        private int _foreachDepth;

        public MdsParser(string[] src, IList<string> objects, List<MdsError> errors)
        {
            _src = src;
            _errors = errors;
            for (int i = 0; i < objects.Count; i++) if (!string.IsNullOrEmpty(objects[i]) && !_objects.ContainsKey(objects[i])) _objects[objects[i]] = i;
        }

        public MdsProgram Run()
        {
            var program = new MdsProgram { LineCount = _src.Length };
            for (int n = 0; n < _src.Length; n++)
            {
                try
                {
                    var toks = Lex(_src[n] ?? "", n + 1);
                    if (toks.Count > 0) _lines.Add(new KeyValuePair<int, List<Token>>(n + 1, toks));
                }
                catch (MdsError e) { _errors.Add(e); }
            }
            // pass 1: variables (so handlers may use variables declared below them)
            var names = new List<string>();
            foreach (var l in _lines)
            {
                var t = l.Value;
                if (!IsWord(t[0], "var")) continue;
                if (t.Count < 2 || t[1].Kind != TokKind.Ident) { _errors.Add(new MdsError(l.Key, "'var' needs a name: var level = 0")); continue; }
                string name = t[1].S;
                if (Keywords.Contains(name) || MdsLibrary.Signatures.ContainsKey(name)) { _errors.Add(new MdsError(l.Key, "'" + name + "' is a reserved word, choose another variable name")); continue; }
                if (_objects.ContainsKey(name)) { _errors.Add(new MdsError(l.Key, "variable '" + name + "' has the same name as a map object")); continue; }
                if (_vars.ContainsKey(name)) { _errors.Add(new MdsError(l.Key, "variable '" + name + "' is declared twice")); continue; }
                _vars[name] = names.Count;
                names.Add(name);
            }
            program.VarNames = names.ToArray();
            program.VarInit = new MdsExpr[names.Count];

            // pass 2: top level
            _i = 0;
            while (_i < _lines.Count)
            {
                int start = _i;
                try
                {
                    Load(_i);
                    var head = _t[0];
                    if (IsWord(head, "var"))
                    {
                        if (_t.Count < 2 || _t[1].Kind != TokKind.Ident || !_vars.TryGetValue(_t[1].S, out int slot)) { _i++; continue; }
                        _p = 2;
                        Expect("=");
                        _playerContext = false;
                        program.VarInit[slot] = Expr();
                        EndOfLine();
                        _i++;
                    }
                    else if (IsWord(head, "on") || IsWord(head, "every") || IsWord(head, "after"))
                    {
                        var h = Header();
                        _i++;
                        h.Body = Block(new[] { "end" }, h.Line);
                        ExpectEnd(h.Line);
                        _playerContext = false;
                        program.Handlers.Add(h);
                    }
                    else throw Err("expected 'on …', 'every …', 'after …' or 'var …' at the top level (statements must be inside a handler)");
                }
                catch (MdsError e)
                {
                    _errors.Add(e);
                    if (_i == start) _i++;
                }
            }
            return program;
        }

        // ------------------------------------------------------------ lexer

        private static List<Token> Lex(string s, int line)
        {
            var list = new List<Token>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '#' || (c == '/' && i + 1 < s.Length && s[i + 1] == '/')) break;
                if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
                {
                    int st = i;
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                    if (!double.TryParse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        throw new MdsError(line, "bad number '" + s.Substring(st, i - st) + "'");
                    list.Add(new Token { Kind = TokKind.Number, N = v, S = s.Substring(st, i - st) });
                    continue;
                }
                if (c == '"')
                {
                    var sb = new StringBuilder();
                    i++;
                    bool closed = false;
                    while (i < s.Length)
                    {
                        char d = s[i++];
                        if (d == '"') { closed = true; break; }
                        if (d == '\\' && i < s.Length)
                        {
                            char e = s[i++];
                            sb.Append(e == 'n' ? '\n' : e);
                            continue;
                        }
                        sb.Append(d);
                    }
                    if (!closed) throw new MdsError(line, "text is missing its closing \"");
                    list.Add(new Token { Kind = TokKind.Text, S = sb.ToString() });
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int st = i;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                    list.Add(new Token { Kind = TokKind.Ident, S = s.Substring(st, i - st) });
                    continue;
                }
                if (i + 1 < s.Length)
                {
                    string two = s.Substring(i, 2);
                    if (two == "==" || two == "!=" || two == "<=" || two == ">=")
                    {
                        list.Add(new Token { Kind = TokKind.Op, S = two });
                        i += 2;
                        continue;
                    }
                }
                if ("+-*/%(),.=<>".IndexOf(c) >= 0)
                {
                    list.Add(new Token { Kind = TokKind.Op, S = c.ToString() });
                    i++;
                    continue;
                }
                throw new MdsError(line, "unexpected character '" + c + "'");
            }
            return list;
        }

        // ------------------------------------------------------------ structure

        private void Load(int index)
        {
            _t = _lines[index].Value;
            _line = _lines[index].Key;
            _p = 0;
        }

        private MdsError Err(string message) => new MdsError(_line, message);

        private static bool IsWord(Token t, string w) => t.Kind == TokKind.Ident && t.S == w;
        private bool AtEnd => _p >= _t.Count;
        private bool PeekOp(string op) => !AtEnd && _t[_p].Kind == TokKind.Op && _t[_p].S == op;
        private bool PeekWord(string w) => !AtEnd && IsWord(_t[_p], w);

        private void Expect(string op)
        {
            if (!PeekOp(op)) throw Err("expected '" + op + "'" + (AtEnd ? " at the end of the line" : " before '" + Describe(_t[_p]) + "'"));
            _p++;
        }

        private void EndOfLine()
        {
            if (!AtEnd) throw Err("unexpected '" + Describe(_t[_p]) + "' (one statement per line)");
        }

        private static string Describe(Token t) => t.Kind == TokKind.Text ? "\"" + t.S + "\"" : t.S;

        private string Name(string what)
        {
            if (AtEnd || _t[_p].Kind != TokKind.Ident) throw Err("expected " + what);
            return _t[_p++].S;
        }

        private MdsHandler Header()
        {
            _playerContext = false;
            var h = new MdsHandler { Line = _line };
            string w = _t[0].S;
            _p = 1;
            if (w == "every" || w == "after")
            {
                if (AtEnd || _t[_p].Kind != TokKind.Number) throw Err("'" + w + "' needs a number of seconds: " + w + " 5");
                h.Trigger = w == "every" ? MdsTrigger.Every : MdsTrigger.After;
                h.Seconds = (float)_t[_p++].N;
                if (h.Trigger == MdsTrigger.Every && h.Seconds < 0.05f) throw Err("'every' needs at least 0.05 seconds");
                EndOfLine();
                return h;
            }
            string ev = Name("an event after 'on' (start, tick, enter, exit, inside, death, shot)");
            switch (ev)
            {
                case "start": h.Trigger = MdsTrigger.Start; break;
                case "tick": h.Trigger = MdsTrigger.Tick; break;
                case "death": h.Trigger = MdsTrigger.Death; _playerContext = true; break;
                case "enter": h.Trigger = MdsTrigger.Enter; break;
                case "exit": h.Trigger = MdsTrigger.Exit; break;
                case "inside": h.Trigger = MdsTrigger.Inside; break;
                case "shot": h.Trigger = MdsTrigger.Shot; break;
                default: throw Err("unknown event 'on " + ev + "' (start, tick, enter, exit, inside, death, shot)");
            }
            if (h.Trigger == MdsTrigger.Enter || h.Trigger == MdsTrigger.Exit || h.Trigger == MdsTrigger.Inside || h.Trigger == MdsTrigger.Shot)
            {
                string obj = Name("an object name: on " + ev + " <name>");
                if (!_objects.TryGetValue(obj, out h.Object)) throw Err("'" + obj + "' is not a named object in this map" + KnownObjects());
                _playerContext = true;
            }
            EndOfLine();
            return h;
        }

        private MdsStmt[] Block(string[] stop, int openLine)
        {
            var list = new List<MdsStmt>();
            while (_i < _lines.Count)
            {
                Load(_i);
                if (_t[0].Kind == TokKind.Ident && Array.IndexOf(stop, _t[0].S) >= 0) return list.ToArray();
                if (IsWord(_t[0], "on") || IsWord(_t[0], "every") || IsWord(_t[0], "after") || IsWord(_t[0], "var"))
                    throw new MdsError(openLine, "block is missing its 'end' (a new handler starts at line " + _line + ")");
                int start = _i;
                try { list.Add(Statement()); }
                catch (MdsError e)
                {
                    _errors.Add(e);
                    if (_i == start) _i++;
                }
            }
            throw new MdsError(openLine, "block is missing its 'end'");
        }

        private void ExpectEnd(int openLine)
        {
            if (_i >= _lines.Count) throw new MdsError(openLine, "block is missing its 'end'");
            Load(_i);
            if (!IsWord(_t[0], "end")) throw Err("expected 'end'");
            _p = 1;
            EndOfLine();
            _i++;
        }

        private MdsStmt Statement()
        {
            int line = _line;
            var head = _t[0];
            if (IsWord(head, "if"))
            {
                var conds = new List<MdsExpr>();
                var blocks = new List<MdsStmt[]>();
                MdsStmt[] otherwise = null;
                _p = 1;
                conds.Add(Expr());
                EndOfLine();
                _i++;
                blocks.Add(Block(new[] { "elif", "else", "end" }, line));
                while (true)
                {
                    Load(_i);
                    if (IsWord(_t[0], "elif"))
                    {
                        _p = 1;
                        conds.Add(Expr());
                        EndOfLine();
                        _i++;
                        blocks.Add(Block(new[] { "elif", "else", "end" }, line));
                        continue;
                    }
                    if (IsWord(_t[0], "else"))
                    {
                        _p = 1;
                        EndOfLine();
                        _i++;
                        otherwise = Block(new[] { "end" }, line);
                    }
                    break;
                }
                ExpectEnd(line);
                return new IfStmt { Line = line, Conds = conds.ToArray(), Blocks = blocks.ToArray(), Else = otherwise };
            }
            if (IsWord(head, "repeat"))
            {
                _p = 1;
                var count = Expr();
                EndOfLine();
                _i++;
                var body = Block(new[] { "end" }, line);
                ExpectEnd(line);
                return new RepeatStmt { Line = line, Count = count, Body = body };
            }
            if (IsWord(head, "foreach"))
            {
                _p = 1;
                if (!PeekWord("player")) throw Err("write 'foreach player' (loops over every living player)");
                _p++;
                EndOfLine();
                _i++;
                bool before = _playerContext;
                _playerContext = true;
                _foreachDepth++;
                var body = Block(new[] { "end" }, line);
                _foreachDepth--;
                _playerContext = before;
                ExpectEnd(line);
                return new ForEachStmt { Line = line, Body = body };
            }
            if (IsWord(head, "stop"))
            {
                _p = 1;
                EndOfLine();
                _i++;
                return new StopStmt { Line = line };
            }
            if (IsWord(head, "elif") || IsWord(head, "else") || IsWord(head, "end"))
                throw Err("'" + head.S + "' without a matching block");

            _p = 0;
            var lhs = Expr();
            if (PeekOp("="))
            {
                _p++;
                var value = Expr();
                EndOfLine();
                _i++;
                if (lhs is VarExpr v) return new AssignStmt { Line = line, Slot = v.Slot, Value = value };
                if (lhs is PropExpr pe)
                {
                    if (!MdsLibrary.SettableObjectProps.Contains(pe.Name))
                        throw new MdsError(line, "'." + pe.Name + "' cannot be set (settable: " + string.Join(", ", MdsLibrary.SettableObjectProps) + "; use commands for players)");
                    return new SetPropStmt { Line = line, Target = pe.Target, Name = pe.Name, Value = value };
                }
                throw new MdsError(line, "only variables (declared with var) and object properties can be assigned");
            }
            EndOfLine();
            _i++;
            if (lhs is CallExpr call)
            {
                if (!MdsLibrary.Signatures[call.Name].Command)
                    throw new MdsError(line, "'" + call.Name + "(…)' returns a value; use it inside an expression, e.g. x = " + call.Name + "(…)");
                return new CallStmt { Line = line, Call = call };
            }
            throw new MdsError(line, "expected a command such as move(…), damage(…) or 'variable = value'");
        }

        // ------------------------------------------------------------ expressions

        private MdsExpr Expr() => Or();

        private MdsExpr Or()
        {
            var a = And();
            while (PeekWord("or")) { _p++; a = new LogicExpr { Line = _line, Or = true, A = a, B = And() }; }
            return a;
        }

        private MdsExpr And()
        {
            var a = Not();
            while (PeekWord("and")) { _p++; a = new LogicExpr { Line = _line, Or = false, A = a, B = Not() }; }
            return a;
        }

        private MdsExpr Not()
        {
            if (PeekWord("not")) { _p++; return new UnaryExpr { Line = _line, Op = "not", A = Not() }; }
            return Compare();
        }

        private MdsExpr Compare()
        {
            var a = Add();
            while (!AtEnd && _t[_p].Kind == TokKind.Op && (_t[_p].S == "==" || _t[_p].S == "!=" || _t[_p].S == "<" || _t[_p].S == "<=" || _t[_p].S == ">" || _t[_p].S == ">="))
            {
                string op = _t[_p++].S;
                a = new BinaryExpr { Line = _line, Op = op, A = a, B = Add() };
            }
            return a;
        }

        private MdsExpr Add()
        {
            var a = Mul();
            while (PeekOp("+") || PeekOp("-"))
            {
                string op = _t[_p++].S;
                a = new BinaryExpr { Line = _line, Op = op, A = a, B = Mul() };
            }
            return a;
        }

        private MdsExpr Mul()
        {
            var a = Unary();
            while (PeekOp("*") || PeekOp("/") || PeekOp("%"))
            {
                string op = _t[_p++].S;
                a = new BinaryExpr { Line = _line, Op = op, A = a, B = Unary() };
            }
            return a;
        }

        private MdsExpr Unary()
        {
            if (PeekOp("-")) { _p++; return new UnaryExpr { Line = _line, Op = "-", A = Unary() }; }
            return Postfix();
        }

        private MdsExpr Postfix()
        {
            var e = Primary();
            while (PeekOp("."))
            {
                _p++;
                string prop = Name("a property name after '.'");
                bool obj = MdsLibrary.ObjectProps.Contains(prop), plr = MdsLibrary.PlayerProps.Contains(prop);
                if (e is ObjExpr && !obj) throw Err("objects have no '." + prop + "' (" + string.Join(", ", MdsLibrary.ObjectProps) + ")");
                if (e is BuiltinExpr b && b.Which == "player" && !plr) throw Err("player has no '." + prop + "' (" + string.Join(", ", MdsLibrary.PlayerProps) + ")");
                if (!obj && !plr) throw Err("unknown property '." + prop + "'");
                e = new PropExpr { Line = _line, Target = e, Name = prop };
            }
            return e;
        }

        private MdsExpr Primary()
        {
            if (AtEnd) throw Err("expression expected at the end of the line");
            var t = _t[_p++];
            switch (t.Kind)
            {
                case TokKind.Number: return new ConstExpr { Line = _line, Value = MdsValue.Num(t.N) };
                case TokKind.Text: return new ConstExpr { Line = _line, Value = MdsValue.Text(t.S) };
                case TokKind.Op:
                    if (t.S == "(")
                    {
                        var inner = Expr();
                        Expect(")");
                        return inner;
                    }
                    throw Err("unexpected '" + t.S + "'");
            }
            string name = t.S;
            if (PeekOp("("))
            {
                if (!MdsLibrary.Signatures.TryGetValue(name, out var sig)) throw Err("unknown command or function '" + name + "' — see Docs/MAP_SCRIPTING.md");
                _p++;
                var args = new List<MdsExpr>();
                if (!PeekOp(")"))
                {
                    args.Add(Expr());
                    while (PeekOp(",")) { _p++; args.Add(Expr()); }
                }
                Expect(")");
                if (args.Count < sig.Min || args.Count > sig.Max)
                    throw Err("'" + name + "' takes " + (sig.Min == sig.Max ? sig.Min.ToString() : sig.Min + "–" + sig.Max) + " arguments: " + sig.Usage);
                return new CallExpr { Line = _line, Name = name, Args = args.ToArray() };
            }
            switch (name)
            {
                case "true": return new ConstExpr { Line = _line, Value = MdsValue.Num(1) };
                case "false": return new ConstExpr { Line = _line, Value = MdsValue.Num(0) };
                case "none": return new ConstExpr { Line = _line, Value = MdsValue.None };
                case "time":
                case "dt":
                case "event_x":
                case "event_y":
                    return new BuiltinExpr { Line = _line, Which = name };
                case "player":
                    if (!_playerContext) throw Err("'player' only exists in 'on enter/exit/inside/death/shot' handlers and inside 'foreach player'");
                    return new BuiltinExpr { Line = _line, Which = name };
            }
            if (_vars.TryGetValue(name, out int slot)) return new VarExpr { Line = _line, Slot = slot };
            if (_objects.TryGetValue(name, out int index)) return new ObjExpr { Line = _line, Index = index };
            if (Keywords.Contains(name)) throw Err("'" + name + "' cannot be used here");
            throw Err("unknown name '" + name + "' (declare variables with 'var " + name + " = 0' at the top; map objects need a \"name\" in the JSON)" + KnownObjects());
        }

        private string KnownObjects() => _objects.Count == 0 ? " — this map has no named objects" : " — named objects: " + string.Join(", ", _objects.Keys);
    }
}

