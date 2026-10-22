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
        public static readonly string[] MouthNames = OriginalCharacterArt.AppendNames(new[] { "표정에 맞춤", "없음", "미소", "활짝", "놀람", "일자", "찡그림", "작은 입" }, 3);
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

        public static readonly string[] HatNames = { "없음", "왕관", "고깔모자", "고양이 귀", "악마 뿔", "새싹", "리본", "천사 고리", "신사 모자", "더듬이" };
        public static readonly string[] ExpressionNames = OriginalCharacterArt.AppendNames(new[] { "기본", "화남", "졸림", "놀람", "행복", "자신만만", "슬픔", "윙크" }, 1);
        public static readonly string[] AccessoryNames = OriginalCharacterArt.AppendNames(new[] { "없음", "볼터치", "안경", "선글라스", "콧수염", "반창고", "외알 안경" }, 2);

        public CharacterLook(int hat, int expression, int accessory)
        {
            this = default;
            Hat = (byte)Wrap(hat, HatNames.Length);
            Expression = (byte)Wrap(expression, ExpressionNames.Length);
            Accessory = (byte)Wrap(accessory, AccessoryNames.Length);
        }

        public static CharacterLook Default => new CharacterLook(0, 0, 0);

        /// <summary>Values outside the known options (newer game, corrupt data) fall back to option 0.</summary>
        public CharacterLook Sanitized
        {
            get
            {
                var l = this;
                l.Hat = Hat < HatNames.Length ? Hat : (byte)0; l.Expression = Expression < ExpressionNames.Length ? Expression : (byte)0;
                l.Accessory = Accessory < AccessoryNames.Length ? Accessory : (byte)0;
                l.Mouth = Mouth < MouthNames.Length ? Mouth : (byte)0; l.Accessory2 = Accessory2 < AccessoryNames.Length ? Accessory2 : (byte)0;
                l.EyeOffset = ClampOffset(EyeOffset); l.MouthOffset = ClampOffset(MouthOffset);
                l.AccessoryOffset = ClampOffset(AccessoryOffset); l.Accessory2Offset = ClampOffset(Accessory2Offset);
                return l;
            }
        }

        public static CharacterLook Random(System.Random rng) => new CharacterLook(rng.Next(HatNames.Length), rng.Next(ExpressionNames.Length), rng.Next(AccessoryNames.Length)) { Mouth = (byte)rng.Next(MouthNames.Length), Accessory2 = (byte)rng.Next(AccessoryNames.Length) };

        public int Pack() => Hat | (Expression << 8) | (Accessory << 16);
        public static CharacterLook Unpack(int v) => new CharacterLook(v & 0xff, (v >> 8) & 0xff, (v >> 16) & 0xff);

        public string Describe() => HatNames[Sanitized.Hat] + " · " + ExpressionNames[Sanitized.Expression] + " · " + AccessoryNames[Sanitized.Accessory];

        private static int Wrap(int v, int n) => ((v % n) + n) % n;

        public bool Equals(CharacterLook o) => Hat == o.Hat && Expression == o.Expression && Accessory == o.Accessory && Mouth == o.Mouth && Accessory2 == o.Accessory2 && EyeOffset == o.EyeOffset && MouthOffset == o.MouthOffset && AccessoryOffset == o.AccessoryOffset && Accessory2Offset == o.Accessory2Offset;
        public override bool Equals(object obj) => obj is CharacterLook o && Equals(o);
        public override int GetHashCode() => Pack();
        public static bool operator ==(CharacterLook a, CharacterLook b) => a.Equals(b);
        public static bool operator !=(CharacterLook a, CharacterLook b) => !a.Equals(b);
        public override string ToString() => Describe();
    }
}

