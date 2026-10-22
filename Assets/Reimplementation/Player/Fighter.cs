using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Metadream.Shoot.Abilities;
using Metadream.Shoot.Cards;
using Metadream.Shoot.Combat;
using Metadream.Shoot.Network;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Player
{
    /// <summary>
    /// A player: input, motion (MotionModel), gun + ammo, block, health, death/revive.
    /// Collision resolution uses Unity's 2D solver; forces/drag/gravity follow the recovered formulas.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
    public sealed class Fighter : MonoBehaviour
    {
        public static readonly List<Fighter> Active = new List<Fighter>();

        // OutOfBoundsHandler.LateUpdate / GetPoint constants.
        public const float BoundsX = 35.56f;
        public const float BoundsY = 20f;

        private readonly ContactPoint2D[] _contacts = new ContactPoint2D[16];
        private Rigidbody2D _rb;
        private CircleCollider2D _collider;
        private SpriteRenderer _body;
        private CharacterModel _model;
        private CharacterFace _face;
        private SpriteRenderer _ring;
        private SpriteRenderer _cooldownRing;

        private float _sinceGrounded;
        private float _sinceWallGrab = 10f;
        private float _sinceJump = 10f;
        private float _sinceAttack = 10f;
        private int _currentJumps;
        private bool _groundAccum;
        private Vector2 _wallNormal;
        private float _edgeCounter;
        private float _flash;
        private InputFrame _frame;
        private Coroutine _burst;
        private bool _pendingRevive;
        // Network mirror (client puppet) state.
        private Vector2 _netPos;
        private Vector2 _netVel;
        private float _netTime;
        private float _puppetReload = 1f;
        private float _puppetBlockReady = 1f;
        private bool _hasNetState;

        public int PlayerId { get; private set; }
        public int TeamId { get; private set; }
        public Color Color { get; private set; } = Color.white;
        public string DisplayName { get; private set; } = "P";
        public IInputSource Input { get; set; }
        public bool IsBot => Input is BotInput;
        public FighterStats Stats { get; private set; } = FighterStats.CreateDefault();
        public readonly List<CardDefinition> Cards = new List<CardDefinition>();
        public MotionTuning Tuning { get; set; } = new MotionTuning();

        public float Health { get; private set; } = 100f;
        public float MaxHealth => Stats.character.maxHealth;
        public bool IsDead { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool CountsAsAlive => !Absent && (!IsDead || _pendingRevive);
        /// <summary>The player left the match (online); the fighter stays hidden until the same device rejoins.</summary>
        public bool Absent { get; set; }
        public AmmoState Ammo { get; } = new AmmoState();
        public BlockState Block { get; } = new BlockState();
        public Vector2 AimDirection { get; private set; } = Vector2.right;
        /// <summary>True on a network client: driven by host snapshots, never simulated locally.</summary>
        public bool Puppet { get; private set; }
        public Vector2 Velocity => Puppet ? _netVel : (_rb != null ? _rb.linearVelocity : Vector2.zero);
        public bool IsVisible => _body != null && _body.enabled;
        public float ReloadProgress => Puppet ? _puppetReload : Ammo.ReloadProgress(Stats.ammo.EffectiveReloadTime);
        public float BlockReadyProgress => Puppet ? _puppetBlockReady : Block.CooldownProgress(Stats.block.EffectiveCooldown);
        public Vector2 Position => transform.position;
        public bool IsGrounded { get; private set; }
        public bool TouchingWall => _sinceWallGrab < 0.1f;
        public float Radius => 0.5f * transform.localScale.x;
        public int RespawnsLeft { get; private set; }
        public int ShotsFired { get; private set; }
        /// <summary>Active card abilities (one instance per owned card copy). See Abilities/CardEffect.cs.</summary>
        public readonly List<CardEffect> Effects = new List<CardEffect>();
        /// <summary>Starting stats applied by ResetForMatch (null = defaults). Set from MatchSettings.stats.</summary>
        public Core.BaseStats BaseStats { get; set; }
        /// <summary>Developer debug: ignore all damage.</summary>
        public bool Invulnerable { get; set; }
        public event Action<Fighter> Jumped;

        private struct DamageOverTime
        {
            public float Dps;
            public float Remaining;
            public Fighter Source;
        }
        private readonly List<DamageOverTime> _dots = new List<DamageOverTime>();
        public bool IsPoisoned => _dots.Count > 0;

        /// <summary>True while a text field (cheat card search) has focus: local keyboard/pad input is ignored.</summary>
        public static bool InputSuspended;

        private struct SpeedModifier
        {
            public float Factor;
            public float Remaining;
        }

        private readonly List<SpeedModifier> _speedMods = new List<SpeedModifier>();

        /// <summary>Product of active temporary movement modifiers (slows &lt; 1, boosts &gt; 1).</summary>
        public float SpeedFactor
        {
            get
            {
                float k = 1f;
                foreach (var m in _speedMods) k *= m.Factor;
                return Mathf.Clamp(k, 0.05f, 4f);
            }
        }

        public bool IsSlowed => SpeedFactor < 0.99f;

        /// <summary>Temporary movement multiplier (0.5 = half speed). Same-direction modifiers refresh instead of stacking.</summary>
        public void AddSpeedModifier(float factor, float duration)
        {
            if (IsDead || duration <= 0f || factor <= 0f || Mathf.Approximately(factor, 1f)) return;
            for (int i = 0; i < _speedMods.Count; i++)
            {
                var m = _speedMods[i];
                if ((m.Factor < 1f) != (factor < 1f)) continue;
                m.Factor = factor < 1f ? Mathf.Min(m.Factor, factor) : Mathf.Max(m.Factor, factor);
                m.Remaining = Mathf.Max(m.Remaining, duration);
                _speedMods[i] = m;
                return;
            }
            _speedMods.Add(new SpeedModifier { Factor = factor, Remaining = duration });
        }

        private void TickSpeedModifiers(float dt)
        {
            for (int i = _speedMods.Count - 1; i >= 0; i--)
            {
                var m = _speedMods[i];
                m.Remaining -= dt;
                if (m.Remaining <= 0f) _speedMods.RemoveAt(i);
                else _speedMods[i] = m;
            }
        }

        /// <summary>Block becomes ready immediately (Refresh, etc.).</summary>
        public void RefreshBlock() => Block.counter = 1000f;

        /// <summary>Moves the fighter (teleport), keeping velocity.</summary>
        public void Teleport(Vector2 position)
        {
            if (_rb != null) _rb.position = position;
            transform.position = position;
        }
        public int BlocksSucceeded { get; private set; }

        public event Action<Fighter, Fighter> Died;
        public event Action<Fighter, float> Damaged;

        public static Fighter Create(int playerId, int teamId, Color color, string name, IInputSource input)
        {
            var go = new GameObject(name);
            var f = go.AddComponent<Fighter>();
            f.PlayerId = playerId;
            f.TeamId = teamId;
            f.Color = color;
            f.DisplayName = name;
            f.Input = input;
            f.BuildVisuals();
            f.ResetForMatch();
            f.SetPresent(false);
            return f;
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _collider = GetComponent<CircleCollider2D>();
            _rb.bodyType = RigidbodyType2D.Dynamic;
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            _collider.radius = 0.5f;
            _collider.sharedMaterial = PhysicsMaterials.Frictionless;
        }

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        private void BuildVisuals()
        {
            _body = gameObject.AddComponent<SpriteRenderer>();
            _model = new CharacterModel(transform, Color, _body);
            _face = _model.Face;

            _ring = MakeChildSprite("BlockRing", SpriteFactory.Ring, 1.9f, 15);
            _ring.color = new Color(1f, 1f, 1f, 0f);
            _cooldownRing = MakeChildSprite("ReadyRing", SpriteFactory.Ring, 1.35f, 9);
            _cooldownRing.color = new Color(1f, 1f, 1f, 0f);
        }

        private SpriteRenderer MakeChildSprite(string name, Sprite sprite, float scale, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = SpriteFactory.Material;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>Cosmetic look (head decoration, expression, face accessory). No gameplay effect.</summary>
        public CharacterLook Look => _face != null ? _face.Look : CharacterLook.Default;

        public void SetLook(CharacterLook look)
        {
            if (_face == null || _face.Look == look.Sanitized) return;
            _face.Apply(look, IsVisible);
        }

        // ---------------------------------------------------------------- lifecycle

        public void ResetForMatch()
        {
            foreach (var e in Effects) e.OnDetach();
            Effects.Clear();
            _dots.Clear();
            Cards.Clear();
            Stats = FighterStats.CreateDefault(BaseStats);
            ApplyStatsToBody();
            ShotsFired = 0;
            BlocksSucceeded = 0;
        }

        public void ApplyCard(CardDefinition card)
        {
            Cards.Add(card);
            StatComposer.Apply(Stats, card.Modifiers);
            ApplyStatsToBody();
            if (card.Effects != null)
            {
                foreach (var spec in card.Effects)
                {
                    var effect = EffectRegistry.Create(spec, this, card);
                    if (effect == null) continue;
                    Effects.Add(effect);
                    effect.OnAttach();
                }
            }
            // ApplyStats finishes with a stat refresh and Revive.
            Health = MaxHealth;
            Ammo.maxAmmo = Stats.ammo.maxAmmo;
            Ammo.Refill();
        }

        private void ApplyStatsToBody()
        {
            transform.localScale = Vector3.one * Stats.character.scale;
            if (_rb != null) _rb.mass = Stats.character.mass / MotionModel.BaseMass;
        }

        /// <summary>HealthHandler.Revive resets health, velocity, block, ammo, status and death flags.</summary>
        public void Revive(Vector2 position, bool resetRespawns, bool stopRoutines = true)
        {
            if (stopRoutines) StopAllCoroutines();
            _burst = null;
            _pendingRevive = false;
            transform.position = position;
            if (_rb != null)
            {
                _rb.linearVelocity = Vector2.zero;
                _rb.angularVelocity = 0f;
            }
            Health = MaxHealth;
            IsDead = false;
            Ammo.maxAmmo = Stats.ammo.maxAmmo;
            Ammo.Refill();
            Block.ResetCooldown(Stats.block.EffectiveCooldown);
            Block.sinceBlock = 10f;
            _sinceGrounded = 0f;
            _sinceWallGrab = 10f;
            _sinceJump = 10f;
            _sinceAttack = 10f;
            _currentJumps = Stats.character.jumps;
            _edgeCounter = 0f;
            _dots.Clear();
            if (resetRespawns) RespawnsLeft = Stats.character.respawns;
            SetPresent(true);
            foreach (var e in Effects) e.OnRevive();
        }

        /// <summary>Visible + simulated or fully removed from the world.</summary>
        public void SetPresent(bool present)
        {
            if (_body != null)
            {
                foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true)) sr.enabled = present;
                _model.SetVisible(present);
            }
            _face?.SetVisible(present);
            if (_rb != null) _rb.simulated = present && !Puppet;
            if (!present) IsPlaying = false;
        }

        public void SetPlaying(bool playing)
        {
            IsPlaying = playing && !IsDead;
            if (!IsPlaying && _rb != null && _rb.simulated) _rb.linearVelocity = Vector2.zero;
        }

        // ---------------------------------------------------------------- update

        private void Update()
        {
            if (Puppet)
            {
                UpdatePuppet();
                UpdateVisuals();
                return;
            }
            UpdateVisuals();
            if (!IsPlaying || IsDead) return;

            float dt = Time.deltaTime;
            TickSpeedModifiers(dt);
            _frame = Input != null ? Input.Read(new InputContext { Position = transform.position, Camera = Camera.main }) : default;
            if (InputSuspended && Input is DeviceInput) _frame = default;

            _sinceGrounded += dt;
            _sinceWallGrab += dt;
            _sinceJump += dt;
            _sinceAttack += dt * Stats.gun.attackSpeedMultiplier;
            Block.Tick(dt);
            Ammo.Tick(dt, Stats.ammo.EffectiveReloadTime, Stats.character.automaticReload);
            if (Stats.character.regeneration > 0f) Heal(Stats.character.regeneration * dt);
            _flash = Mathf.Max(0f, _flash - dt * 4f);

            if (_frame.Aim.sqrMagnitude > 0.001f) AimDirection = _frame.Aim.normalized;
            if (_frame.JumpPressed) TryJump();
            _rb.linearVelocity += MotionModel.HeldJumpDelta(_frame.JumpHeld, _sinceJump, dt, Stats.character.jump * SpeedFactor, Tuning.upForce);
            if (_frame.ShootHeld || _frame.ShootPressed) TryAttack();
            if (_frame.BlockPressed) TryBlock();

            for (int i = 0; i < Effects.Count; i++) Effects[i].OnTick(dt);
            TickDamageOverTime(dt);
            if (IsDead) return;
            CheckBounds(dt);
        }

        private void FixedUpdate()
        {
            IsGrounded = _groundAccum;
            _groundAccum = false;
            if (Puppet || !IsPlaying || IsDead || _rb == null || !_rb.simulated) return;

            var t = Tuning;
            float mult = MotionModel.Multiplier(IsGrounded);
            Vector2 v = _rb.linearVelocity;
            v += MotionModel.BaseGravityDelta(Time.fixedDeltaTime);
            v += MotionModel.AirGravityDelta(_sinceGrounded, _sinceWallGrab, t.gravityForce, t.gravityExponent);
            v += MotionModel.MoveDelta(_frame.Move, t.force, Stats.character.movementSpeed * SpeedFactor, 0f, mult);
            v -= MotionModel.DragDelta(v, t.extraDrag, mult);
            _rb.linearVelocity = v;
        }

        private void OnCollisionEnter2D(Collision2D collision) => ReadContacts(collision);
        private void OnCollisionStay2D(Collision2D collision)
        {
            ReadContacts(collision);
            PushBody(collision);
        }

        /// <summary>Walking into a loose box pushes it along (contact physics alone barely moves heavier boxes).</summary>
        private void PushBody(Collision2D collision)
        {
            var body = collision.rigidbody;
            if (Puppet || body == null || body.bodyType != RigidbodyType2D.Dynamic || !IsPlaying || IsDead) return;
            float move = _frame.Move.x;
            if (Mathf.Abs(move) < 0.1f) return;
            Vector2 toBody = body.worldCenterOfMass - (Vector2)transform.position;
            if (Mathf.Sign(toBody.x) != Mathf.Sign(move) || Mathf.Abs(toBody.x) < Mathf.Abs(toBody.y) * 0.5f) return;
            body.AddForce(new Vector2(move * 40f * Stats.character.movementSpeed, 0f));
        }

        private void ReadContacts(Collision2D collision)
        {
            if (collision.collider.isTrigger) return;
            int n = collision.GetContacts(_contacts);
            for (int i = 0; i < n; i++)
            {
                Vector2 normal = _contacts[i].normal;
                if (normal.y > 0.65f)
                {
                    _groundAccum = true;
                    _sinceGrounded = 0f;
                    // TouchGround restores jumps (not during the 0.1 s jump lockout).
                    if (_sinceJump > MotionModel.JumpCooldown) _currentJumps = Stats.character.jumps;
                }
                else if (Mathf.Abs(normal.x) > 0.65f && _sinceJump >= 0.15f)
                {
                    _wallNormal = new Vector2(Mathf.Sign(normal.x), 0f);
                    _sinceWallGrab = 0f;
                }
            }
        }

        // ---------------------------------------------------------------- jump

        /// <summary>PlayerJump.Jump (0x060003fc).</summary>
        public bool TryJump(bool forceJump = false)
        {
            if (!forceJump)
            {
                if (_sinceJump < MotionModel.JumpCooldown) return false;
                if (_currentJumps <= 0 && _sinceWallGrab > MotionModel.WallJumpWindow) return false;
            }

            Vector2 dir = Vector2.up;
            bool wallJump = false;
            if (_sinceWallGrab < MotionModel.WallJumpWindow && !IsGrounded)
            {
                dir = MotionModel.WallJumpDirection(_wallNormal);
                _currentJumps = Stats.character.jumps;
                wallJump = true;
            }
            else if (_sinceGrounded <= MotionModel.GroundedCoyoteTime)
            {
                // Development decision: the literal CIL resets currentJumps on every jump (UNKNOWN-02);
                // here a grounded jump restores the pool and air jumps consume it.
                _currentJumps = Stats.character.jumps;
            }

            Vector2 v = _rb.linearVelocity;
            if (v.y < 0f) v.y = 0f;
            _sinceGrounded = 0f;
            _sinceJump = 0f;
            IsGrounded = false;
            _currentJumps--;
            v += MotionModel.JumpImpulse(dir, Stats.character.jump, 0f, Tuning.upForce);
            if (!wallJump) v.x += MotionModel.JumpSideImpulse(v.x, Stats.character.jump, 0f);
            _rb.linearVelocity = v;
            Fx.JumpPuff(transform.position - Vector3.up * Radius, Color);
            Jumped?.Invoke(this);
            return true;
        }

        // ---------------------------------------------------------------- gun

        /// <summary>Gun.Attack (0x06000298) -> DoAttack -> FireBurst.</summary>
        public bool TryAttack()
        {
            var g = Stats.gun;
            if (!CombatRules.CanAttack(_sinceAttack, g.attackSpeed, Ammo.isReloading, false)) return false;
            if (_burst != null) return false;
            _sinceAttack = 0f;
            _burst = StartCoroutine(FireBurst());
            return true;
        }

        private IEnumerator FireBurst()
        {
            var g = Stats.gun;
            int count = Mathf.Max(1, g.numberOfProjectiles);
            for (int burst = 0; burst < g.BurstCount; burst++)
            {
                if (!IsPlaying || IsDead) break;
                for (int i = 0; i < count; i++)
                {
                    float angle = SpreadAngle(i, count, g.spread);
                    Vector2 dir = Rotate(AimDirection, angle);
                    SpawnProjectile(dir);
                    // Gun.BulletInit: one GunAmmo.Shoot per spawned projectile when useAmmo.
                    Ammo.Shoot(Stats.ammo.EffectiveReloadTime);
                    if (Ammo.isReloading && Ammo.currentAmmo <= 0) Fx.Sound("reload", transform.position);
                }
                Fx.Shake(0.12f + 0.04f * count);
                if (g.timeBetweenBullets > 0f && burst < g.BurstCount - 1) yield return new WaitForSeconds(g.timeBetweenBullets);
            }
            _burst = null;
        }

        /// <summary>Development decision: getShootRotation is not reproduced; projectiles fan evenly.</summary>
        public static float SpreadAngle(int index, int count, float spread)
        {
            if (count <= 1) return 0f;
            float total = Mathf.Max(spread * 180f, 6f * (count - 1));
            return Mathf.Lerp(-total * 0.5f, total * 0.5f, index / (float)(count - 1));
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private void SpawnProjectile(Vector2 dir)
        {
            ShotsFired++;
            Vector2 origin = (Vector2)transform.position + dir * (Radius + 0.35f);
            var projectile = Projectile.Spawn(this, origin, dir, Stats.gun);
            Fx.Muzzle(origin, Color, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            for (int i = 0; i < Effects.Count; i++) Effects[i].OnShoot(projectile);
        }

        // ---------------------------------------------------------------- block

        /// <summary>Block.TryBlock (0x06000069): requires counter &gt;= cooldown, then opens the 0.3 s window.</summary>
        public bool TryBlock()
        {
            if (!Block.TryBlock(Stats.block.EffectiveCooldown)) return false;
            Fx.Ring(transform.position, Color.white, 1.9f * transform.localScale.x);
            Fx.Sound("block", transform.position);
            if (Stats.block.healing > 0f) Heal(Stats.block.healing);
            if (Stats.block.additionalBlocks > 0) StartCoroutine(ExtraBlocks(Stats.block.additionalBlocks));
            for (int i = 0; i < Effects.Count; i++) Effects[i].OnBlock();
            return true;
        }

        private IEnumerator ExtraBlocks(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new WaitForSeconds(CombatRules.TimeBetweenBlocks);
                if (!IsPlaying || IsDead) yield break;
                Block.DoExtraBlock();
                Fx.Ring(transform.position, Color.white, 1.9f * transform.localScale.x);
            }
        }

        /// <summary>Block.TryBlockMe: called by a projectile that reaches this fighter.</summary>
        public bool TryBlockProjectile(Projectile projectile = null)
        {
            if (!Block.TryBlockMe()) return false;
            BlocksSucceeded++;
            Fx.Ring(transform.position, Color.white, 2.2f * transform.localScale.x);
            Fx.Sound("reflect", transform.position);
            Fx.Shake(0.15f);
            if (projectile != null)
                for (int i = 0; i < Effects.Count; i++) Effects[i].OnBlockedProjectile(projectile);
            return true;
        }

        // ---------------------------------------------------------------- health

        public bool TakeDamage(float amount, Fighter source, Vector2 direction, bool unblockable, float knockback, bool quiet = false)
        {
            if (!IsPlaying || IsDead) return false;
            if (Invulnerable) return false;
            if (!unblockable && Block.IsBlocking) return false;
            var outcome = CombatRules.ApplyDamage(Health, amount);
            Health = outcome.Health;
            if (!quiet) _flash = 1f;
            Damaged?.Invoke(this, amount);
            if (knockback > 0f && direction.sqrMagnitude > 0f)
                AddImpulse(direction.normalized * knockback * (MotionModel.BaseMass / Stats.character.mass));
            if (source != null && source != this && source.Stats.character.lifeSteal > 0f)
                source.Heal(amount * source.Stats.character.lifeSteal);
            if (!quiet) Fx.Burst(transform.position, Color, 6, 6f);
            if (!quiet && amount > 0.5f) Fx.Sound("hit", transform.position, Mathf.Clamp(amount / 55f, 0.5f, 1.3f));
            for (int i = 0; i < Effects.Count; i++) Effects[i].OnDamaged(amount, source);
            if (source != null && source != this)
                for (int i = 0; i < source.Effects.Count; i++) source.Effects[i].OnDealtDamage(this, amount);
            if (outcome.Died) HandleDeath(source);
            return true;
        }

        /// <summary>Unblockable damage per second for a duration (poison etc.). Re-applying from the same source refreshes it.</summary>
        public void AddDamageOverTime(float damagePerSecond, float duration, Fighter source)
        {
            if (IsDead || damagePerSecond <= 0f || duration <= 0f) return;
            for (int i = 0; i < _dots.Count; i++)
            {
                if (_dots[i].Source != source) continue;
                _dots[i] = new DamageOverTime { Dps = Mathf.Max(_dots[i].Dps, damagePerSecond), Remaining = Mathf.Max(_dots[i].Remaining, duration), Source = source };
                return;
            }
            _dots.Add(new DamageOverTime { Dps = damagePerSecond, Remaining = duration, Source = source });
        }

        private void TickDamageOverTime(float dt)
        {
            for (int i = _dots.Count - 1; i >= 0 && !IsDead; i--)
            {
                var d = _dots[i];
                float step = Mathf.Min(dt, d.Remaining);
                d.Remaining -= dt;
                if (d.Remaining <= 0f) _dots.RemoveAt(i);
                else _dots[i] = d;
                TakeDamage(d.Dps * step, d.Source, Vector2.zero, true, 0f, quiet: true);
            }
        }

        public void Heal(float amount)
        {
            if (amount <= 0f || IsDead || Health >= MaxHealth) return;
            Health = Mathf.Min(MaxHealth, Health + amount);
        }

        public void AddImpulse(Vector2 velocityChange)
        {
            if (_rb == null) return;
            _rb.linearVelocity += velocityChange;
            _sinceGrounded = MotionModel.SinceGroundedAfterForce(_sinceGrounded, velocityChange.y);
        }

        private void HandleDeath(Fighter killer)
        {
            IsDead = true;
            IsPlaying = false;
            if (_burst != null) { StopCoroutine(_burst); _burst = null; }
            Fx.Death(transform.position, Color);
            if (RespawnsLeft > 0)
            {
                RespawnsLeft--;
                _pendingRevive = true;
                SetPresent(false);
                StartCoroutine(PhoenixRevive());
                return;
            }
            SetPresent(false);
            Died?.Invoke(this, killer);
        }

        private IEnumerator PhoenixRevive()
        {
            Vector2 pos = transform.position;
            yield return new WaitForSeconds(0.8f);
            pos.x = Mathf.Clamp(pos.x, -BoundsX + 3f, BoundsX - 3f);
            pos.y = Mathf.Clamp(pos.y, -BoundsY + 3f, BoundsY - 3f);
            Revive(pos, false, false);
            SetPlaying(true);
            Fx.Ring(pos, Color, 3f);
        }

        /// <summary>OutOfBoundsHandler.LateUpdate: every 0.1 s outside, push inward; damage 51 unless blocking.</summary>
        private void CheckBounds(float dt)
        {
            Vector2 p = transform.position;
            float nx = Mathf.InverseLerp(-BoundsX, BoundsX, p.x);
            float ny = Mathf.InverseLerp(-BoundsY, BoundsY, p.y);
            bool outside = nx <= 0f || nx >= 1f || ny <= 0f || ny >= 1f;
            if (!outside) { _edgeCounter = 0f; return; }

            _sinceGrounded = 0f;
            _edgeCounter += dt;
            if (_edgeCounter <= 0.1f) return;
            _edgeCounter = 0f;

            Vector2 inward = Vector2.zero;
            if (nx <= 0f) inward.x += 1f;
            if (nx >= 1f) inward.x -= 1f;
            if (ny <= 0f) inward.y += 1f;
            if (ny >= 1f) inward.y -= 1f;
            inward.Normalize();

            if (Block.IsBlocking)
            {
                _rb.linearVelocity = Vector2.zero;
                AddImpulse(inward * Tuning.edgeImpulseBlocking);
                Fx.Ring(p, Color.white, 2.5f);
            }
            else
            {
                AddImpulse(inward * Tuning.edgeImpulse);
                Fx.EdgeFlash(p, inward);
                TakeDamage(Tuning.edgeDamage, null, Vector2.zero, true, 0f);
            }
        }

        // ---------------------------------------------------------------- visuals

        private void UpdateVisuals()
        {
            if (_body == null || !_body.enabled) return;
            float hp = MaxHealth > 0f ? Mathf.Clamp01(Health / MaxHealth) : 1f;
            float danger = hp < 0.3f ? (0.5f + 0.5f * Mathf.Sin(Time.time * 10f)) * (1f - hp / 0.3f) * 0.45f : 0f;
            Color baseColor = Color.Lerp(Color, new Color(1f, 0.15f, 0.1f), danger);
            if (IsSlowed) baseColor = Color.Lerp(baseColor, new Color(0.65f, 0.85f, 1f), 0.35f);
            if (IsPoisoned) baseColor = Color.Lerp(baseColor, new Color(0.45f, 0.85f, 0.25f), 0.3f);
            _body.color = Color.Lerp(baseColor, Color.white, _flash * 0.8f);
            _model.Tick(AimDirection, Velocity, IsGrounded, IsDead, Ammo.currentAmmo, Ammo.maxAmmo,
                Ammo.isReloading, ReloadProgress, BlockReadyProgress, Block.IsBlocking, _body.color);
            float blockAlpha = Block.IsBlocking ? Mathf.Clamp01(1f - Block.sinceBlock / CombatRules.BlockWindow) : 0f;
            _ring.color = new Color(1f, 1f, 1f, blockAlpha);
            bool ready = BlockReadyProgress >= 1f;
            _cooldownRing.color = new Color(1f, 1f, 1f, ready && IsPlaying ? 0.35f : 0f);
        }

        // ---------------------------------------------------------------- network

        public void SetDisplayName(string name)
        {
            DisplayName = name;
            gameObject.name = name;
        }

        public void MakePuppet()
        {
            Puppet = true;
            if (_rb != null)
            {
                _rb.simulated = false;
                _rb.linearVelocity = Vector2.zero;
            }
        }

        /// <summary>Host side: everything a client needs to draw this fighter.</summary>
        public FighterState CaptureState()
        {
            var cards = new byte[Mathf.Min(Cards.Count, 255)];
            for (int i = 0; i < cards.Length; i++) cards[i] = (byte)Mathf.Clamp(Cards[i].Index, 0, 255);
            return new FighterState
            {
                Present = IsVisible,
                Dead = IsDead,
                Playing = IsPlaying,
                Grounded = IsGrounded,
                TouchingWall = TouchingWall,
                Reloading = Ammo.isReloading,
                Position = transform.position,
                Velocity = Velocity,
                Aim = AimDirection,
                Health = Health,
                MaxHealth = MaxHealth,
                Scale = transform.localScale.x,
                Ammo = (byte)Mathf.Clamp(Ammo.currentAmmo, 0, 255),
                MaxAmmo = (byte)Mathf.Clamp(Ammo.maxAmmo, 0, 255),
                ReloadProgress = ReloadProgress,
                SinceBlock = Mathf.Min(Block.sinceBlock, 10f),
                BlockReady = BlockReadyProgress,
                Cards = cards
            };
        }

        /// <summary>Client side: adopt the host state; position is smoothed in UpdatePuppet.</summary>
        public void ApplySnapshot(in FighterState s)
        {
            bool wasVisible = IsVisible;
            if (s.Present != wasVisible) SetPresent(s.Present);
            IsDead = s.Dead;
            IsPlaying = s.Present && !s.Dead && s.Playing;
            _netPos = s.Position;
            _netVel = s.Velocity;
            _netTime = Time.unscaledTime;
            if (!_hasNetState || (!wasVisible && s.Present)) transform.position = s.Position;
            _hasNetState = true;
            if (s.Aim.sqrMagnitude > 0.001f) AimDirection = s.Aim.normalized;
            if (s.Health < Health - 0.01f) _flash = 1f;
            Health = s.Health;
            Stats.character.maxHealth = s.MaxHealth;
            transform.localScale = Vector3.one * s.Scale;
            Ammo.maxAmmo = s.MaxAmmo;
            Ammo.currentAmmo = s.Ammo;
            Ammo.isReloading = s.Reloading;
            _puppetReload = s.ReloadProgress;
            Block.sinceBlock = s.SinceBlock;
            _puppetBlockReady = s.BlockReady;
            IsGrounded = s.Grounded;
            _sinceWallGrab = s.TouchingWall ? 0f : 10f;

            bool same = s.Cards != null && s.Cards.Length == Cards.Count;
            if (same)
                for (int i = 0; i < Cards.Count; i++)
                    if (Cards[i].Index != s.Cards[i]) { same = false; break; }
            if (!same && s.Cards != null)
            {
                Cards.Clear();
                foreach (var idx in s.Cards)
                    if (idx < CardCatalog.All.Count) Cards.Add(CardCatalog.All[idx]);
            }
        }

        private void UpdatePuppet()
        {
            _flash = Mathf.Max(0f, _flash - Time.deltaTime * 4f);
            if (!_hasNetState || !IsVisible) return;
            float age = Mathf.Min(Time.unscaledTime - _netTime, 0.1f) * Time.timeScale;
            Vector2 target = _netPos + _netVel * age;
            Vector2 current = transform.position;
            transform.position = (target - current).sqrMagnitude > 16f
                ? target
                : Vector2.Lerp(current, target, 1f - Mathf.Exp(-30f * Time.unscaledDeltaTime));
        }
    }
}

