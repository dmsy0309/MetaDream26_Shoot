using System;
using System.IO;
using UnityEngine;

namespace Metadream.Shoot.Player
{
    /// <summary>
    /// Cosmetic character look chosen in the main menu (캐릭터 꾸미기): head decoration, expression and face accessory.
    /// Body colour stays the team/slot colour. Sent to the host in Hello and to clients/replays in snapshots.
    /// </summary>
    [Serializable]
    public struct CharacterLook : IEquatable<CharacterLook>
    {
        public byte Hat, Expression, Accessory;
        public byte Mouth, Accessory2;
        public Vector2 EyeOffset, MouthOffset, AccessoryOffset, Accessory2Offset;
        // Original IDs stay stable for saved profiles, network snapshots and replays.
        // The removed procedural choices are no longer selectable or rendered.
        public static string[] MouthNames => OriginalCharacterArt.Names.mouths;
        public static string[] ExpressionNames => OriginalCharacterArt.Names.eyes;
        public static string[] AccessoryNames => OriginalCharacterArt.Names.accessories;
        public static Vector2 ClampOffset(Vector2 p) => new Vector2(Safe(p.x), Safe(p.y));
        private static float Safe(float n) => float.IsNaN(n) || float.IsInfinity(n) ? 0 : Mathf.Clamp(n, -0.25f, 0.25f);
        public void WriteDetails(BinaryWriter w)
        {
            var l = Sanitized; w.Write(l.Mouth); w.Write(l.Accessory2);
            foreach (var p in new[] { l.EyeOffset, l.MouthOffset, l.AccessoryOffset, l.Accessory2Offset }) { w.Write(p.x); w.Write(p.y); }
        }
        public CharacterLook ReadDetails(BinaryReader r)
        {
            var l = this; l.Mouth = r.ReadByte(); l.Accessory2 = r.ReadByte();
            l.EyeOffset = new Vector2(r.ReadSingle(), r.ReadSingle()); l.MouthOffset = new Vector2(r.ReadSingle(), r.ReadSingle());
            l.AccessoryOffset = new Vector2(r.ReadSingle(), r.ReadSingle()); l.Accessory2Offset = new Vector2(r.ReadSingle(), r.ReadSingle());
            return l.Sanitized;
        }

        public CharacterLook(int hat, int expression, int accessory)
        {
            this = default;
            Hat = 0; // Reserved byte retained in the existing wire/storage format.
            Expression = Canonical(expression, OriginalCharacterArt.EyeStart, ExpressionNames.Length);
            Accessory = Canonical(accessory, OriginalCharacterArt.AccessoryStart, AccessoryNames.Length);
            Mouth = OriginalCharacterArt.MouthStart;
            Accessory2 = OriginalCharacterArt.AccessoryStart;
        }

        public static CharacterLook Default => new CharacterLook(0, 0, 0);

        /// <summary>Removed legacy or unknown options fall back to the first extracted item.</summary>
        public CharacterLook Sanitized
        {
            get
            {
                var l = this;
                l.Hat = 0;
                l.Expression = Canonical(Expression, OriginalCharacterArt.EyeStart, ExpressionNames.Length);
                l.Mouth = Canonical(Mouth, OriginalCharacterArt.MouthStart, MouthNames.Length);
                l.Accessory = Canonical(Accessory, OriginalCharacterArt.AccessoryStart, AccessoryNames.Length);
                l.Accessory2 = Canonical(Accessory2, OriginalCharacterArt.AccessoryStart, AccessoryNames.Length);
                l.EyeOffset = ClampOffset(EyeOffset); l.MouthOffset = ClampOffset(MouthOffset);
                l.AccessoryOffset = ClampOffset(AccessoryOffset); l.Accessory2Offset = ClampOffset(Accessory2Offset);
                return l;
            }
        }

        public static CharacterLook Random(System.Random rng) => new CharacterLook(0,
            OriginalCharacterArt.EyeStart + rng.Next(ExpressionNames.Length),
            OriginalCharacterArt.AccessoryStart + rng.Next(AccessoryNames.Length))
        {
            Mouth = (byte)(OriginalCharacterArt.MouthStart + rng.Next(MouthNames.Length)),
            Accessory2 = (byte)(OriginalCharacterArt.AccessoryStart + rng.Next(AccessoryNames.Length))
        };

        public int Pack() => Hat | (Expression << 8) | (Accessory << 16);
        public static CharacterLook Unpack(int v) => new CharacterLook(v & 0xff, (v >> 8) & 0xff, (v >> 16) & 0xff);

        public string Describe()
        {
            var l = Sanitized;
            return ExpressionNames[l.Expression - OriginalCharacterArt.EyeStart] + " / "
                + MouthNames[l.Mouth - OriginalCharacterArt.MouthStart] + " / "
                + AccessoryNames[l.Accessory - OriginalCharacterArt.AccessoryStart];
        }

        private static byte Canonical(int value, int first, int count) => (byte)(value >= first && value < first + count ? value : first);

        public bool Equals(CharacterLook o) => Hat == o.Hat && Expression == o.Expression && Accessory == o.Accessory && Mouth == o.Mouth && Accessory2 == o.Accessory2 && EyeOffset == o.EyeOffset && MouthOffset == o.MouthOffset && AccessoryOffset == o.AccessoryOffset && Accessory2Offset == o.Accessory2Offset;
        public override bool Equals(object obj) => obj is CharacterLook o && Equals(o);
        public override int GetHashCode() => Pack();
        public static bool operator ==(CharacterLook a, CharacterLook b) => a.Equals(b);
        public static bool operator !=(CharacterLook a, CharacterLook b) => !a.Equals(b);
        public override string ToString() => Describe();
    }
}

