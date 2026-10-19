using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Metadream.Shoot.Content;

namespace Metadream.Shoot.Audio
{
    /// <summary>One synthesized layer (several layers are mixed into one clip).</summary>
    [Serializable]
    public sealed class SynthLayer
    {
        /// <summary>sine | square | triangle | saw | noise</summary>
        public string wave = "square";
        public float freq = 440f;
        /// <summary>End frequency (exponential sweep). 0 = same as freq.</summary>
        public float freqEnd;
        public float duration = 0.1f;
        public float volume = 1f;
        /// <summary>Seconds before this layer starts.</summary>
        public float delay;
        public float attack = 0.004f;
        /// <summary>Decay curve exponent (1 = linear, 3 = punchy).</summary>
        public float curve = 2f;
    }

    /// <summary>A sound event. The game only knows ids; everything else is data.</summary>
    [Serializable]
    public sealed class SoundDef
    {
        public string id = "";
        /// <summary>Clip name in Resources/Sounds or Content/Sounds/*.wav (empty = same as id). Falls back to layers.</summary>
        public string clip = "";
        public float volume = 0.6f;
        public float pitch = 1f;
        /// <summary>Random pitch spread (0.05 = ±5%).</summary>
        public float pitchJitter = 0.05f;
        /// <summary>Minimum seconds between two plays of this sound (spam guard).</summary>
        public float cooldown = 0.03f;
        public SynthLayer[] layers = new SynthLayer[0];
        public string description = "";
    }

    [Serializable]
    public sealed class SoundFile
    {
        public SoundDef[] sounds = new SoundDef[0];
    }

    /// <summary>
    /// Sound definitions: Resources/Content/Sounds/sounds.json (built in), overridden per id by Content/Sounds/sounds.json
    /// next to the executable. Clip lookup per id: Resources/Sounds/&lt;clip&gt; → Content/Sounds/&lt;clip&gt;.wav → synthesized layers.
    /// See Docs/SOUNDS.md.
    /// </summary>
    public static class SoundBank
    {
        public const int SampleRate = 44100;
        private static Dictionary<string, SoundDef> _defs;
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        private static readonly Dictionary<int, string> ByHash = new Dictionary<int, string>();
        public static readonly List<string> Issues = new List<string>();

        public static IReadOnlyCollection<string> Ids { get { Ensure(); return _defs.Keys; } }

        public static SoundDef Find(string id)
        {
            Ensure();
            return id != null && _defs.TryGetValue(id, out var d) ? d : null;
        }

        public static string IdOfHash(int hash)
        {
            Ensure();
            return ByHash.TryGetValue(hash, out var id) ? id : null;
        }

        /// <summary>Stable 24-bit FNV-1a hash (exactly representable in a float, used in network Fx events).</summary>
        public static int Hash(string id)
        {
            uint h = 2166136261;
            foreach (char c in id ?? "") { h ^= c; h *= 16777619; }
            return (int)(h & 0xFFFFFF);
        }

        public static void Reload()
        {
            _defs = null;
            foreach (var c in Clips.Values) if (c != null) UnityEngine.Object.Destroy(c);
            Clips.Clear();
            Ensure();
        }

        private static void Ensure()
        {
            if (_defs != null) return;
            _defs = new Dictionary<string, SoundDef>(StringComparer.Ordinal);
            ByHash.Clear();
            Issues.Clear();
            var builtin = Resources.Load<TextAsset>(ContentDatabase.ResourceRoot + "/Sounds/sounds");
            if (builtin != null) Merge(builtin.text, "builtin sounds.json");
            else Issues.Add("builtin Resources/Content/Sounds/sounds.json missing");
            string ext = ExternalFolder;
            if (ext != null && File.Exists(Path.Combine(ext, "sounds.json")))
            {
                try { Merge(File.ReadAllText(Path.Combine(ext, "sounds.json")), Path.Combine(ext, "sounds.json")); }
                catch (Exception e) { Issues.Add("cannot read external sounds.json: " + e.Message); }
            }
            foreach (var id in _defs.Keys) ByHash[Hash(id)] = id;
            foreach (var i in Issues) Debug.LogWarning("[Sound] " + i);
        }

        private static void Merge(string json, string source)
        {
            SoundFile file;
            try { file = JsonUtility.FromJson<SoundFile>(json); }
            catch (Exception e) { Issues.Add(source + ": invalid JSON: " + e.Message); return; }
            if (file?.sounds == null) return;
            foreach (var d in file.sounds)
            {
                if (d == null || string.IsNullOrWhiteSpace(d.id)) { Issues.Add(source + ": sound without id"); continue; }
                if (d.volume < 0f || d.pitch <= 0f) Issues.Add(source + ": " + d.id + " needs volume >= 0 and pitch > 0");
                _defs[d.id] = d;
            }
        }

        public static string ExternalFolder
        {
            get
            {
                var _ = ContentDatabase.Cards; // make sure the external Content folder was resolved
                string root = ContentDatabase.LoadedExternalDirectory;
                return root != null && Directory.Exists(Path.Combine(root, "Sounds")) ? Path.Combine(root, "Sounds") : null;
            }
        }

        public static AudioClip ClipFor(SoundDef d)
        {
            if (d == null) return null;
            if (Clips.TryGetValue(d.id, out var cached)) return cached;
            string name = string.IsNullOrWhiteSpace(d.clip) ? d.id : d.clip;
            AudioClip clip = Resources.Load<AudioClip>("Sounds/" + name);
            if (clip == null && ExternalFolder != null)
            {
                string wav = Path.Combine(ExternalFolder, name + ".wav");
                if (File.Exists(wav))
                {
                    try { clip = WavReader.Load(wav); }
                    catch (Exception e) { Debug.LogWarning("[Sound] " + wav + ": " + e.Message); }
                }
            }
            if (clip == null && d.layers != null && d.layers.Length > 0) clip = Synth(d.id, d.layers);
            Clips[d.id] = clip;
            return clip;
        }

        /// <summary>Mixes the layers into a mono clip (deterministic, no assets needed).</summary>
        public static AudioClip Synth(string name, SynthLayer[] layers)
        {
            float length = 0f;
            foreach (var l in layers) length = Mathf.Max(length, l.delay + l.duration);
            int n = Mathf.Max(1, Mathf.CeilToInt(length * SampleRate));
            var data = new float[n];
            uint noise = 22222;
            foreach (var l in layers)
            {
                int start = Mathf.RoundToInt(l.delay * SampleRate);
                int len = Mathf.RoundToInt(l.duration * SampleRate);
                float f0 = Mathf.Max(1f, l.freq), f1 = l.freqEnd > 0f ? l.freqEnd : f0;
                double phase = 0;
                for (int i = 0; i < len && start + i < n; i++)
                {
                    float t = i / (float)Mathf.Max(1, len);
                    float freq = f0 * Mathf.Pow(f1 / f0, t);
                    phase += freq / SampleRate;
                    float p = (float)(phase - Math.Floor(phase));
                    float s;
                    switch (l.wave)
                    {
                        case "sine": s = Mathf.Sin(p * 2f * Mathf.PI); break;
                        case "triangle": s = 1f - 4f * Mathf.Abs(p - 0.5f); break;
                        case "saw": s = 2f * p - 1f; break;
                        case "noise": noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5; s = (noise / (float)uint.MaxValue) * 2f - 1f; break;
                        default: s = p < 0.5f ? 1f : -1f; break;
                    }
                    float att = l.attack > 0f ? Mathf.Clamp01(i / (l.attack * SampleRate)) : 1f;
                    float env = att * Mathf.Pow(1f - t, Mathf.Max(0.1f, l.curve));
                    data[start + i] += s * env * l.volume;
                }
            }
            for (int i = 0; i < n; i++) data[i] = (float)Math.Tanh(data[i] * 0.9f);
            var clip = AudioClip.Create("sfx_" + name, n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }

    /// <summary>Minimal RIFF/WAVE reader (PCM 8/16/24-bit and 32-bit float) for Content/Sounds/*.wav.</summary>
    public static class WavReader
    {
        public static AudioClip Load(string path) => FromBytes(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));

        public static AudioClip FromBytes(byte[] b, string name)
        {
            if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F') throw new InvalidDataException("not a WAV file");
            int pos = 12, channels = 0, rate = 0, bits = 0, format = 0, dataPos = -1, dataLen = 0;
            while (pos + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                int size = BitConverter.ToInt32(b, pos + 4);
                if (id == "fmt ")
                {
                    format = BitConverter.ToInt16(b, pos + 8);
                    channels = BitConverter.ToInt16(b, pos + 10);
                    rate = BitConverter.ToInt32(b, pos + 12);
                    bits = BitConverter.ToInt16(b, pos + 22);
                }
                else if (id == "data") { dataPos = pos + 8; dataLen = Math.Min(size, b.Length - dataPos); break; }
                pos += 8 + size + (size & 1);
            }
            if (dataPos < 0 || channels <= 0 || rate <= 0) throw new InvalidDataException("missing fmt/data chunk");
            if (!(format == 1 && (bits == 8 || bits == 16 || bits == 24)) && !(format == 3 && bits == 32))
                throw new InvalidDataException("unsupported WAV (format " + format + ", " + bits + " bit); use 16-bit PCM");
            int bytes = bits / 8, count = dataLen / bytes;
            var data = new float[count];
            for (int i = 0; i < count; i++)
            {
                int o = dataPos + i * bytes;
                switch (bits)
                {
                    case 8: data[i] = (b[o] - 128) / 128f; break;
                    case 16: data[i] = BitConverter.ToInt16(b, o) / 32768f; break;
                    case 24: data[i] = ((b[o] | (b[o + 1] << 8) | ((sbyte)b[o + 2] << 16))) / 8388608f; break;
                    default: data[i] = BitConverter.ToSingle(b, o); break;
                }
            }
            var clip = AudioClip.Create(name, count / channels, channels, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }

    /// <summary>
    /// Plays sound events. Gameplay code calls Presentation.Fx.Sound(id, pos) (replicated to clients and replays);
    /// UI code calls Sfx.Play(id) (local only). Volume = definition volume × Settings 효과음 볼륨.
    /// </summary>
    public static class Sfx
    {
        private static GameObject _root;
        private static readonly List<AudioSource> Pool = new List<AudioSource>();
        private static readonly Dictionary<string, float> LastPlayed = new Dictionary<string, float>();
        private static int _next;
        public static bool Enabled = true;
        public static int Played { get; private set; }

        public static void Play(string id, Vector2? position = null, float volumeScale = 1f)
        {
            if (!Enabled || Presentation.Fx.Headless || GameBootstrap.IsServer || Application.isBatchMode) return;
            var def = SoundBank.Find(id);
            if (def == null) { Debug.LogWarning("[Sound] unknown sound id '" + id + "'"); return; }
            float now = Time.unscaledTime;
            if (LastPlayed.TryGetValue(id, out float last) && now - last < def.cooldown) return;
            LastPlayed[id] = now;
            var clip = SoundBank.ClipFor(def);
            if (clip == null) return;
            var src = NextSource();
            src.clip = clip;
            src.volume = Mathf.Clamp01(def.volume * volumeScale * DevSettings.SfxVolume);
            float speed = Time.timeScale > 0.01f ? Mathf.Clamp(Time.timeScale, 0.5f, 1.5f) : 1f;
            src.pitch = def.pitch * (1f + UnityEngine.Random.Range(-def.pitchJitter, def.pitchJitter)) * speed;
            src.panStereo = position.HasValue ? Mathf.Clamp(position.Value.x / 45f, -0.7f, 0.7f) : 0f;
            src.Play();
            Played++;
        }

        public static void PlayHash(int hash, Vector2 position, float volumeScale)
        {
            string id = SoundBank.IdOfHash(hash);
            if (id != null) Play(id, position, volumeScale <= 0f ? 1f : volumeScale);
        }

        private static AudioSource NextSource()
        {
            if (_root == null)
            {
                _root = new GameObject("Sfx");
                UnityEngine.Object.DontDestroyOnLoad(_root);
                if (UnityEngine.Object.FindAnyObjectByType<AudioListener>() == null) _root.AddComponent<AudioListener>();
                Pool.Clear();
                for (int i = 0; i < 24; i++)
                {
                    var s = _root.AddComponent<AudioSource>();
                    s.playOnAwake = false;
                    s.spatialBlend = 0f;
                    Pool.Add(s);
                }
            }
            for (int i = 0; i < Pool.Count; i++)
            {
                var s = Pool[(_next + i) % Pool.Count];
                if (!s.isPlaying) { _next = (_next + i + 1) % Pool.Count; return s; }
            }
            var steal = Pool[_next];
            _next = (_next + 1) % Pool.Count;
            return steal;
        }
    }
}
