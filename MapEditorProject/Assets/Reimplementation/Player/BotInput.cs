using UnityEngine;
using Metadream.Shoot.Combat;

namespace Metadream.Shoot.Player
{
    /// <summary>Simple CPU opponent used for "vs CPU", the demo mode and the PlayMode match test.</summary>
    public sealed class BotInput : IInputSource
    {
        private readonly System.Random _rng;
        private float _strafeTimer;
        private float _strafeDir = 1f;
        private float _reaction;
        private float _blockCooldown;

        public BotInput(int seed, string label = "CPU")
        {
            _rng = new System.Random(seed);
            Label = label;
        }

        public string Label { get; }
        public Fighter Self { get; set; }
        public float Accuracy = 0.85f;

        private float Rand() => (float)_rng.NextDouble();

        public InputFrame Read(in InputContext context)
        {
            var frame = new InputFrame();
            if (Self == null) return frame;
            float dt = Time.deltaTime;
            Fighter target = null;
            float best = float.MaxValue;
            foreach (var f in Fighter.Active)
            {
                if (f == Self || f.TeamId == Self.TeamId || f.IsDead || !f.IsPlaying) continue;
                float d = (f.Position - Self.Position).sqrMagnitude;
                if (d < best) { best = d; target = f; }
            }
            if (target == null) return frame;

            Vector2 pos = Self.Position;
            Vector2 to = target.Position - pos;
            float dist = to.magnitude;

            // Movement: keep a mid range, strafe, stay away from the screen edges and pits.
            _strafeTimer -= dt;
            if (_strafeTimer <= 0f) { _strafeTimer = 0.6f + Rand() * 1.2f; _strafeDir = Rand() < 0.5f ? -1f : 1f; }
            float moveX = dist > 18f ? Mathf.Sign(to.x) : dist < 7f ? -Mathf.Sign(to.x) : _strafeDir;
            if (pos.x < -Fighter.BoundsX + 5f) moveX = 1f;
            if (pos.x > Fighter.BoundsX - 5f) moveX = -1f;
            bool groundAhead = Physics2D.Raycast(pos + new Vector2(moveX * 2.5f, 0f), Vector2.down, 14f, ~0).collider != null;
            if (!groundAhead && Self.IsGrounded) moveX = -moveX;
            frame.Move = new Vector2(moveX, 0f);

            bool wantJump = (to.y > 3.5f && Rand() < 0.04f) || (Self.TouchingWall && Rand() < 0.2f) || Rand() < 0.006f;
            if (pos.y < -BoundsYSafe) wantJump = true;
            if (!Self.IsGrounded && Self.Velocity.y < -8f && pos.y < -8f) wantJump = true;
            frame.JumpPressed = wantJump;

            // Aim with lead and ballistic drop compensation.
            var gun = Self.Stats.gun;
            float speed = Mathf.Max(1f, gun.ProjectileSpeed);
            float t = dist / speed;
            Vector2 predicted = target.Position + target.Velocity * t * 0.6f;
            predicted.y += 0.5f * 30f * gun.gravity * t * t;
            Vector2 aim = (predicted - pos).normalized;
            float wobble = (1f - Accuracy) * (Rand() - 0.5f) * 0.6f;
            frame.Aim = new Vector2(aim.x - aim.y * wobble, aim.y + aim.x * wobble).normalized;

            _reaction -= dt;
            bool clearShot = gun.ignoreWalls || gun.reflects > 0 || !Physics2D.Linecast(pos + aim * (Self.Radius + 0.4f), target.Position - aim * (target.Radius + 0.2f)).collider;
            if (_reaction <= 0f && clearShot && dist < 45f)
            {
                frame.ShootHeld = true;
                if (Self.Ammo.currentAmmo <= 1) _reaction = 0.25f + Rand() * 0.5f;
            }

            // Block an incoming hostile projectile.
            _blockCooldown -= dt;
            if (_blockCooldown <= 0f && Self.Block.IsReady(Self.Stats.block.EffectiveCooldown))
            {
                foreach (var p in Projectile.Active)
                {
                    Vector2 rel = pos - p.Position;
                    if (rel.magnitude > 3.2f + p.Velocity.magnitude * 0.06f) continue;
                    if (Vector2.Dot(p.Velocity, rel) <= 0f) continue;
                    if (!p.Threatens(Self)) continue;
                    _blockCooldown = 0.25f;
                    if (Rand() < 0.75f) frame.BlockPressed = true;
                    break;
                }
            }
            return frame;
        }

        private const float BoundsYSafe = 15f;
    }
}

