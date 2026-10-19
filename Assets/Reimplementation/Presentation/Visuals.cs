using System.Collections.Generic;
using UnityEngine;

namespace Metadream.Shoot.Presentation
{
    /// <summary>Procedural sprites so the project ships no third-party art.</summary>
    public static class SpriteFactory
    {
        private static Sprite _circle, _ring, _square;
        private static Material _material;

        public static Material Material
        {
            get
            {
                if (_material == null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                    if (shader == null) shader = Shader.Find("Sprites/Default");
                    _material = new Material(shader) { name = "RuntimeSpriteUnlit" };
                }
                return _material;
            }
            set => _material = value;
        }

        public static Sprite Circle => _circle != null ? _circle : (_circle = MakeDisc(128, 0f));
        public static Sprite Ring => _ring != null ? _ring : (_ring = MakeDisc(128, 0.82f));
        public static Sprite Square => _square != null ? _square : (_square = MakeSquare());

        private static Sprite MakeDisc(int size, float inner)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / r;
                float a = Mathf.Clamp01((1f - d) * r * 0.5f);
                if (inner > 0f) a *= Mathf.Clamp01((d - inner) * r * 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private static Sprite MakeSquare()
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
        }
    }

    public static class PhysicsMaterials
    {
        private static PhysicsMaterial2D _frictionless, _box;
        public static PhysicsMaterial2D Frictionless => _frictionless != null ? _frictionless : (_frictionless = new PhysicsMaterial2D("Frictionless") { friction = 0f, bounciness = 0f });
        public static PhysicsMaterial2D Box => _box != null ? _box : (_box = new PhysicsMaterial2D("Box") { friction = 0.25f, bounciness = 0.05f });
    }

    public static class Palette
    {
        public static readonly Color Background = new Color(0.93f, 0.90f, 0.84f);
        public static readonly Color Solid = new Color(0.16f, 0.15f, 0.19f);
        public static readonly Color Box = new Color(0.35f, 0.33f, 0.38f);
        public static readonly Color Mover = new Color(0.27f, 0.25f, 0.36f);
        public static readonly Color JumpPad = new Color(0.25f, 0.85f, 0.45f);
        public static readonly Color Hazard = new Color(0.95f, 0.25f, 0.2f);
        public static readonly Color PlayerOne = new Color(1f, 0.49f, 0.18f);
        public static readonly Color PlayerTwo = new Color(0.16f, 0.58f, 1f);
        public static readonly Color Common = new Color(0.82f, 0.82f, 0.82f);
        public static readonly Color Uncommon = new Color(0.35f, 0.75f, 1f);
        public static readonly Color Rare = new Color(1f, 0.35f, 0.65f);
        public static readonly Color Ink = new Color(0.1f, 0.1f, 0.12f);
        public static readonly Color PlayerThree = new Color(0.93f, 0.27f, 0.35f);
        public static readonly Color PlayerFour = new Color(0.12f, 0.78f, 0.72f);
        private static readonly Color[] Slots = { PlayerOne, PlayerTwo, PlayerThree, PlayerFour };

        /// <summary>Slot i plays for team i % 2: warm colours for team 0, cool colours for team 1.</summary>
        public static Color SlotColor(int slot) => Slots[((slot % Slots.Length) + Slots.Length) % Slots.Length];
        public static Color TeamColor(int team) => team == 0 ? PlayerOne : PlayerTwo;
        public static string TeamName(int team) => team == 0 ? "주황 팀" : "파랑 팀";
    }

    public enum FxType : byte
    {
        Burst,
        Spark,
        JumpPuff,
        Ring,
        EdgeFlash,
        Shake,
        Muzzle,
        Death,
        /// <summary>Sound event: A = SoundBank.Hash(id), B = volume scale.</summary>
        Sound
    }

    /// <summary>Visual event replicated from the host to clients.</summary>
    public struct FxEvent
    {
        public FxType Type;
        public Vector2 Position;
        public Color32 Color;
        public float A;
        public float B;
    }

    public static class Fx
    {
        /// <summary>Non-null while hosting: every effect is also queued here for the next snapshot.</summary>
        public static List<FxEvent> Outbox;
        /// <summary>Dedicated server: record effects for clients but never spawn local particles.</summary>
        public static bool Headless;

        private static void Record(FxType type, Vector2 pos, Color color, float a, float b)
        {
            if (Outbox != null && Outbox.Count < 256)
                Outbox.Add(new FxEvent { Type = type, Position = pos, Color = color, A = a, B = b });
        }

        public static void Play(in FxEvent e)
        {
            switch (e.Type)
            {
                case FxType.Burst: Burst(e.Position, e.Color, Mathf.RoundToInt(e.A), e.B); break;
                case FxType.Spark: Spark(e.Position, e.Color); break;
                case FxType.JumpPuff: JumpPuff(e.Position, e.Color); break;
                case FxType.Ring: Ring(e.Position, e.Color, e.A); break;
                case FxType.EdgeFlash: EdgeFlash(e.Position, Vector2.zero); break;
                case FxType.Shake: Shake(e.A); break;
                case FxType.Muzzle: Muzzle(e.Position, e.Color, e.A); break;
                case FxType.Death: Death(e.Position, e.Color); break;
                case FxType.Sound: Audio.Sfx.PlayHash(Mathf.RoundToInt(e.A), e.Position, e.B); break;
            }
        }

        /// <summary>Gameplay sound: plays here and is replicated to clients / recorded in replays.</summary>
        public static void Sound(string id, Vector2 pos, float volumeScale = 1f)
        {
            Record(FxType.Sound, pos, Color.white, Audio.SoundBank.Hash(id), volumeScale);
            if (Headless) return;
            Audio.Sfx.Play(id, pos, volumeScale);
        }

        private static FxParticle Make(Vector2 pos, Sprite sprite, Color color, float scale, int order)
        {
            var go = new GameObject("Fx");
            go.transform.position = pos;
            var p = go.AddComponent<FxParticle>();
            p.Init(sprite, color, scale, order);
            return p;
        }

        public static void Burst(Vector2 pos, Color color, int count, float speed)
        {
            Record(FxType.Burst, pos, color, count, speed);
            if (Headless) return;
            for (int i = 0; i < count; i++)
            {
                var p = Make(pos, i % 3 == 0 ? SpriteFactory.Circle : SpriteFactory.Square, i % 4 == 0 ? Color.Lerp(color, Color.white, 0.6f) : color, Random.Range(0.18f, 0.45f), 30);
                p.velocity = Random.insideUnitCircle.normalized * Random.Range(speed * 0.3f, speed);
                p.gravity = 18f;
                p.life = Random.Range(0.4f, 0.9f);
                p.spin = Random.Range(-720f, 720f);
            }
        }

        public static void Spark(Vector2 pos, Color color)
        {
            Record(FxType.Spark, pos, color, 0f, 0f);
            if (Headless) return;
            for (int i = 0; i < 7; i++)
            {
                var p = Make(pos, SpriteFactory.Square, i % 2 == 0 ? Color.white : color, Random.Range(0.1f, 0.2f), 30);
                p.velocity = Random.insideUnitCircle.normalized * Random.Range(5f, 11f);
                p.life = Random.Range(0.18f, 0.32f);
                p.spin = Random.Range(-900f, 900f);
            }
            var flash = Make(pos, SpriteFactory.Circle, new Color(1f, 1f, 1f, 0.8f), 0.7f, 31);
            flash.life = 0.09f;
            flash.grow = 0.6f;
        }

        /// <summary>Muzzle flash at the barrel; angle in degrees.</summary>
        public static void Muzzle(Vector2 pos, Color color, float angle)
        {
            Record(FxType.Muzzle, pos, color, angle, 0f);
            if (Headless) return;
            Audio.Sfx.Play("shoot", pos);
            var core = Make(pos, SpriteFactory.Circle, new Color(1f, 0.97f, 0.8f, 0.95f), 0.55f, 32);
            core.life = 0.07f;
            core.grow = 0.5f;
            var glow = Make(pos, SpriteFactory.Circle, new Color(color.r, color.g, color.b, 0.45f), 0.95f, 31);
            glow.life = 0.1f;
            Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            for (int i = 0; i < 3; i++)
            {
                var p = Make(pos, SpriteFactory.Square, Color.Lerp(color, Color.white, 0.5f), 0.1f, 31);
                p.velocity = (dir + Random.insideUnitCircle * 0.5f) * Random.Range(6f, 12f);
                p.life = 0.15f;
            }
        }

        /// <summary>Fighter death: big burst, shock ring, white flash and camera shake.</summary>
        public static void Death(Vector2 pos, Color color)
        {
            Record(FxType.Death, pos, color, 0f, 0f);
            if (Headless) return;
            Audio.Sfx.Play("death", pos);
            var flash = Make(pos, SpriteFactory.Circle, new Color(1f, 1f, 1f, 0.9f), 1.6f, 33);
            flash.life = 0.14f;
            flash.grow = 1.2f;
            var ring = Make(pos, SpriteFactory.Ring, color, 2.2f, 26);
            ring.life = 0.45f;
            ring.grow = 2.2f;
            for (int i = 0; i < 22; i++)
            {
                var p = Make(pos, i % 2 == 0 ? SpriteFactory.Circle : SpriteFactory.Square, i % 5 == 0 ? Color.white : color, Random.Range(0.2f, 0.55f), 30);
                p.velocity = Random.insideUnitCircle.normalized * Random.Range(4f, 16f);
                p.gravity = 14f;
                p.life = Random.Range(0.5f, 1.1f);
                p.spin = Random.Range(-720f, 720f);
            }
            if (CameraRig.Instance != null) CameraRig.Instance.AddShake(0.5f);
        }

        public static void JumpPuff(Vector2 pos, Color color)
        {
            Record(FxType.JumpPuff, pos, color, 0f, 0f);
            if (Headless) return;
            Audio.Sfx.Play("jump", pos);
            for (int i = 0; i < 3; i++)
            {
                var p = Make(pos, SpriteFactory.Circle, Color.Lerp(color, Color.white, 0.6f), 0.35f, 5);
                p.velocity = new Vector2(Random.Range(-3f, 3f), Random.Range(-0.5f, 0.5f));
                p.life = 0.3f;
            }
        }

        public static void Ring(Vector2 pos, Color color, float scale)
        {
            Record(FxType.Ring, pos, color, scale, 0f);
            if (Headless) return;
            var p = Make(pos, SpriteFactory.Ring, color, scale, 25);
            p.life = 0.3f;
            p.grow = 0.35f;
        }

        public static void EdgeFlash(Vector2 pos, Vector2 inward)
        {
            Record(FxType.EdgeFlash, pos, Color.white, 0f, 0f);
            if (Headless) return;
            Audio.Sfx.Play("edge", pos);
            var p = Make(pos, SpriteFactory.Ring, new Color(1f, 0.25f, 0.2f), 3f, 25);
            p.life = 0.35f;
            p.grow = 0.8f;
            if (CameraRig.Instance != null) CameraRig.Instance.AddShake(0.35f);
        }

        public static void Shake(float amount)
        {
            Record(FxType.Shake, Vector2.zero, Color.white, amount, 0f);
            if (CameraRig.Instance != null) CameraRig.Instance.AddShake(amount);
        }

        public static void ClearAll()
        {
            for (int i = FxParticle.Active.Count - 1; i >= 0; i--)
                if (FxParticle.Active[i] != null) Object.Destroy(FxParticle.Active[i].gameObject);
            FxParticle.Active.Clear();
        }
    }
}
