using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Scripting;
using Metadream.Shoot.Combat;
using Metadream.Shoot.Player;

namespace Metadream.Shoot.Maps.Scripting
{
    /// <summary>Marks a MapBehavior subclass and gives it the id used in map JSON ("behaviors": [{"type": id, ...}]).</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class MapBehaviorAttribute : Attribute
    {
        public MapBehaviorAttribute(string id, string summary = "")
        {
            Id = id;
            Summary = summary;
        }

        public string Id { get; }
        public string Summary { get; }
    }

    /// <summary>Parameters of one "behaviors" entry in map JSON. Unused fields are ignored by a behavior.</summary>
    [Serializable]
    public sealed class MapBehaviorSpec
    {
        public string type = "";
        /// <summary>Name of the map object the behavior acts on (solids/boxes/movers/pads/hazards/winds/areas "name").</summary>
        public string target = "";
        /// <summary>Name of a trigger area (optional).</summary>
        public string area = "";
        public float speed;
        public float amount;
        public float interval = 1f;
        public float delay;
        public float duration;
        public float damage;
        public float radius;
        public float x, y;
        public string text = "";
        public string sound = "";
    }

    /// <summary>
    /// C# map gimmick (one instance per map instance). Hooks run only on the simulated map (offline, host, server)
    /// and may only act through Api (MapScriptApi). Add one = a file with [MapBehavior("id")] (see Docs/MAP_SCRIPTING.md).
    /// </summary>
    [Preserve]
    public abstract class MapBehavior
    {
        public MapBehaviorSpec Spec { get; internal set; }
        public MapScriptApi Api { get; internal set; }
        /// <summary>Spec.target resolved (null when not given).</summary>
        public MapObject Target { get; internal set; }
        /// <summary>Spec.area resolved (null when not given).</summary>
        public MapObject Area { get; internal set; }

        /// <summary>Return an error for bad parameters or null (checked at content load). requireTarget etc. are checked for you.</summary>
        public virtual string Validate() => null;
        /// <summary>Must Spec.target name an object? (checked at content load)</summary>
        public virtual bool NeedsTarget => false;
        public virtual bool NeedsArea => false;

        public virtual void OnStart() { }
        /// <summary>Every physics step while the map is simulated.</summary>
        public virtual void OnTick(float deltaTime) { }
        public virtual void OnEnter(MapObject area, Fighter fighter) { }
        public virtual void OnExit(MapObject area, Fighter fighter) { }
        public virtual void OnDeath(Fighter fighter) { }
        /// <summary>A projectile hit a named map object.</summary>
        public virtual void OnShot(MapObject target, Projectile projectile, Vector2 point) { }
    }

    /// <summary>Finds every [MapBehavior] class in this assembly and creates instances by id.</summary>
    public static class MapBehaviorRegistry
    {
        private static Dictionary<string, Type> _types;
        private static Dictionary<string, string> _summaries;

        private static void Ensure()
        {
            if (_types != null) return;
            _types = new Dictionary<string, Type>(StringComparer.Ordinal);
            _summaries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var t in typeof(MapBehavior).Assembly.GetTypes())
            {
                if (t.IsAbstract || !typeof(MapBehavior).IsAssignableFrom(t)) continue;
                var attr = t.GetCustomAttribute<MapBehaviorAttribute>();
                if (attr == null) continue;
                if (_types.ContainsKey(attr.Id)) { Debug.LogError("[MapBehavior] duplicate id '" + attr.Id + "'"); continue; }
                _types[attr.Id] = t;
                _summaries[attr.Id] = attr.Summary;
            }
        }

        public static IEnumerable<string> Ids { get { Ensure(); return _types.Keys; } }
        public static string SummaryOf(string id) { Ensure(); return _summaries.TryGetValue(id, out var s) ? s : ""; }
        public static bool IsKnown(string id) { Ensure(); return id != null && _types.ContainsKey(id); }

        /// <summary>Error message or null. names = the map's object names.</summary>
        public static string Validate(MapBehaviorSpec spec, ICollection<string> names)
        {
            if (spec == null) return "behavior is null";
            if (string.IsNullOrWhiteSpace(spec.type)) return "missing \"type\"";
            if (!IsKnown(spec.type)) return "unknown behavior type '" + spec.type + "' (known: " + string.Join(", ", Ids) + ")";
            var probe = (MapBehavior)Activator.CreateInstance(_types[spec.type]);
            probe.Spec = spec;
            if (!string.IsNullOrEmpty(spec.target) && !names.Contains(spec.target)) return "target '" + spec.target + "' is not a named object in this map";
            if (!string.IsNullOrEmpty(spec.area) && !names.Contains(spec.area)) return "area '" + spec.area + "' is not a named object in this map";
            if (probe.NeedsTarget && string.IsNullOrEmpty(spec.target)) return "'" + spec.type + "' needs \"target\" (an object \"name\")";
            if (probe.NeedsArea && string.IsNullOrEmpty(spec.area)) return "'" + spec.type + "' needs \"area\" (an object \"name\")";
            return probe.Validate();
        }

        public static MapBehavior Create(MapBehaviorSpec spec, MapScriptApi api)
        {
            if (!IsKnown(spec?.type)) return null;
            var b = (MapBehavior)Activator.CreateInstance(_types[spec.type]);
            b.Spec = spec;
            b.Api = api;
            b.Target = string.IsNullOrEmpty(spec.target) ? null : api.Find(spec.target);
            b.Area = string.IsNullOrEmpty(spec.area) ? null : api.Find(spec.area);
            return b;
        }
    }
}

