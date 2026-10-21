using System;

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

        public static readonly string[] HatNames = { "없음", "왕관", "고깔모자", "고양이 귀", "악마 뿔", "새싹", "리본", "천사 고리", "신사 모자", "더듬이" };
        public static readonly string[] ExpressionNames = { "기본", "화남", "졸림", "놀람", "행복", "자신만만", "슬픔", "윙크" };
        public static readonly string[] AccessoryNames = { "없음", "볼터치", "안경", "선글라스", "콧수염", "반창고", "외알 안경" };

        public CharacterLook(int hat, int expression, int accessory)
        {
            Hat = (byte)Wrap(hat, HatNames.Length);
            Expression = (byte)Wrap(expression, ExpressionNames.Length);
            Accessory = (byte)Wrap(accessory, AccessoryNames.Length);
        }

        public static CharacterLook Default => new CharacterLook(0, 0, 0);

        /// <summary>Values outside the known options (newer game, corrupt data) fall back to option 0.</summary>
        public CharacterLook Sanitized => new CharacterLook(Hat < HatNames.Length ? Hat : 0, Expression < ExpressionNames.Length ? Expression : 0, Accessory < AccessoryNames.Length ? Accessory : 0);

        public static CharacterLook Random(System.Random rng) => new CharacterLook(rng.Next(HatNames.Length), rng.Next(ExpressionNames.Length), rng.Next(AccessoryNames.Length));

        public int Pack() => Hat | (Expression << 8) | (Accessory << 16);
        public static CharacterLook Unpack(int v) => new CharacterLook(v & 0xff, (v >> 8) & 0xff, (v >> 16) & 0xff);

        public string Describe() => HatNames[Sanitized.Hat] + " · " + ExpressionNames[Sanitized.Expression] + " · " + AccessoryNames[Sanitized.Accessory];

        private static int Wrap(int v, int n) => ((v % n) + n) % n;

        public bool Equals(CharacterLook o) => Hat == o.Hat && Expression == o.Expression && Accessory == o.Accessory;
        public override bool Equals(object obj) => obj is CharacterLook o && Equals(o);
        public override int GetHashCode() => Pack();
        public static bool operator ==(CharacterLook a, CharacterLook b) => a.Equals(b);
        public static bool operator !=(CharacterLook a, CharacterLook b) => !a.Equals(b);
        public override string ToString() => Describe();
    }
}

