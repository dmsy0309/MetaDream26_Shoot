using UnityEngine;
using Metadream.Shoot.Player;

namespace Metadream.Shoot.Maps.Scripting
{
    // Built-in C# map behaviors. Each one is an example of the pattern in Docs/MAP_SCRIPTING.md §3.

    [MapBehavior("spin", "target을 초당 speed도(기본 45)로 계속 회전")]
    public sealed class SpinBehavior : MapBehavior
    {
        public override bool NeedsTarget => true;
        public override void OnTick(float dt) => Api.RotateBy(Target, (Spec.speed != 0f ? Spec.speed : 45f) * dt, 0f);
    }

    [MapBehavior("blink", "target을 interval초마다 보였다/숨겼다 (delay초 후 시작)")]
    public sealed class BlinkBehavior : MapBehavior
    {
        private float _next;
        public override bool NeedsTarget => true;
        public override string Validate() => Spec.interval > 0.1f ? null : "blink needs interval > 0.1";
        public override void OnStart() => _next = Spec.delay + Spec.interval;
        public override void OnTick(float dt)
        {
            if (Api.Time < _next) return;
            _next += Spec.interval;
            Api.SetVisible(Target, !Target.Visible);
            if (!string.IsNullOrEmpty(Spec.sound)) Api.Sound(Spec.sound, Target.Position);
        }
    }

    [MapBehavior("rise", "delay초 후 target을 초당 speed만큼 위로 이동, 최대 amount만큼 (음수 speed = 아래로)")]
    public sealed class RiseBehavior : MapBehavior
    {
        private float _moved;
        public override bool NeedsTarget => true;
        public override string Validate() => Spec.speed != 0f && Spec.amount > 0f ? null : "rise needs speed != 0 and amount > 0";
        public override void OnTick(float dt)
        {
            if (Api.Time < Spec.delay || _moved >= Spec.amount) return;
            float step = Mathf.Min(Mathf.Abs(Spec.speed) * dt, Spec.amount - _moved);
            _moved += step;
            Api.MoveBy(Target, new Vector2(0f, Mathf.Sign(Spec.speed) * step), 0f);
        }
    }

    [MapBehavior("turret", "target 오브젝트(없으면 x, y)에서 interval초마다 가장 가까운 플레이어에게 탄 발사 (speed, damage). target을 숨기면 멈춤")]
    public sealed class TurretBehavior : MapBehavior
    {
        private float _next;
        public override string Validate() => Spec.interval >= 0.2f ? null : "turret needs interval >= 0.2";
        public override void OnStart() => _next = Spec.delay + Spec.interval;
        public override void OnTick(float dt)
        {
            if (Api.Time < _next) return;
            _next += Spec.interval;
            if (Target != null && !Target.Visible) return;
            var origin = Target != null ? Target.Position : new Vector2(Spec.x, Spec.y);
            var target = Api.Nearest(origin);
            if (target == null) return;
            Vector2 dir = Api.ToLocal(target.Position) - origin;
            // Start outside the turret block so the bullet does not hit its own turret.
            if (Target != null) origin += dir.normalized * (Mathf.Max(Target.Size.x, Target.Size.y) * 0.75f + 0.5f);
            Api.Shoot(origin, dir, Spec.speed > 0f ? Spec.speed : 40f, Spec.damage > 0f ? Spec.damage : 20f);
            Api.Sound(string.IsNullOrEmpty(Spec.sound) ? "shoot" : Spec.sound, origin);
        }
    }

    [MapBehavior("bounce_area", "area에 들어온 플레이어를 위로 speed(기본 40)만큼 튕김")]
    public sealed class BounceAreaBehavior : MapBehavior
    {
        public override bool NeedsArea => true;
        public override void OnEnter(MapObject area, Fighter f)
        {
            if (area != Area) return;
            Api.Push(f, new Vector2(0f, Spec.speed > 0f ? Spec.speed : 40f));
            Api.Effect("ring", Api.ToLocal(f.Position), 2f, Color.white);
            Api.Sound("pad", Api.ToLocal(f.Position));
        }
    }
}

