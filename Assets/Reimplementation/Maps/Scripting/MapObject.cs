using UnityEngine;

namespace Metadream.Shoot.Maps.Scripting
{
    public enum MapObjectKind : byte { Solid, Box, Mover, JumpPad, Hazard, Wind, Area }

    /// <summary>
    /// Runtime state of one map object (one per map instance; the shared MapDefinition is never modified).
    /// Named objects can be read and changed by map scripts; their state is sent to network clients and replays.
    /// Coordinates are map-local (the same space as the map JSON).
    /// </summary>
    public sealed class MapObject
    {
        public string Name = "";
        public MapObjectKind Kind;
        /// <summary>Index in MapDefinition.ObjectNames (-1 = unnamed, not scriptable).</summary>
        public int NamedIndex = -1;
        public Transform Transform;
        public Collider2D Collider;
        public Rigidbody2D Body;
        public SpriteRenderer Renderer;
        public SpriteRenderer Edge;
        /// <summary>Movers: the path definition.</summary>
        public MapMover Path;

        /// <summary>Solids/zones/areas: current centre. Movers: base point of the path. Boxes: read from the body.</summary>
        public Vector2 Center;
        public float Rotation;
        public Vector2 Size;
        public bool Visible = true;
        public bool HasTint;
        public Color Tint = Color.white;
        public Color BaseColor = Color.white;

        // zone parameters (copied from the definition, scripts may change them)
        public float Power, Dps;
        public Vector2 Force;

        internal Tween2 MoveTween, SizeTween;
        internal Tween1 TurnTween;

        public bool IsZone => Kind == MapObjectKind.JumpPad || Kind == MapObjectKind.Hazard || Kind == MapObjectKind.Wind;

        /// <summary>Actual current position (boxes and movers move on their own).</summary>
        public Vector2 Position => (Kind == MapObjectKind.Box || Kind == MapObjectKind.Mover) && Transform != null ? (Vector2)Transform.localPosition : Center;

        public float CurrentRotation => Kind == MapObjectKind.Box && Transform != null ? Transform.localEulerAngles.z : Rotation;

        public MapBlock Block => new MapBlock(Position.x, Position.y, Size.x, Size.y, CurrentRotation);

        public bool Contains(Vector2 localPoint, float margin = 0f) => Block.Contains(localPoint, margin);
    }

    internal struct Tween2
    {
        public bool Active;
        public Vector2 From, To;
        public float Elapsed, Duration;
    }

    internal struct Tween1
    {
        public bool Active;
        public float From, To;
        public float Elapsed, Duration;
    }
}

