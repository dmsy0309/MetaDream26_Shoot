using System.Collections.Generic;
using UnityEngine;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Player
{
    /// <summary>
    /// Eyes, expression and decorations of a character (child sprites of a unit-size body, radius 0.5).
    /// Used by Fighter and by the main-menu preview so both look identical. Face parts follow the aim direction.
    /// </summary>
    public sealed class CharacterFace
    {
        private static readonly Color Ink = new Color(0.08f, 0.08f, 0.1f);
        private readonly Transform _root;
        private readonly List<GameObject> _parts = new List<GameObject>();
        private readonly List<KeyValuePair<Transform, Vector3>> _following = new List<KeyValuePair<Transform, Vector3>>();
        private readonly List<SpriteRenderer> _covers = new List<SpriteRenderer>();
        private Transform _eyeL, _eyeR, _pupilL, _pupilR;
        private Color _bodyColor;
        private float _blinkAt = 2f, _blinkUntil;
        private bool _visible = true;
        private Vector2 _partOffset;

        public CharacterLook Look { get; private set; }

        public CharacterFace(Transform root, Color bodyColor)
        {
            _root = root;
            _bodyColor = bodyColor;
            Apply(CharacterLook.Default, true);
        }

        /// <summary>Rebuilds the face for a look. visible = whether the character is currently shown.</summary>
        public void Apply(CharacterLook look, bool visible)
        {
            look = look.Sanitized;
            Look = look;
            _visible = visible;
            foreach (var go in _parts)
            {
                if (go == null) continue;
                if (Application.isPlaying) Object.Destroy(go);
                else Object.DestroyImmediate(go);
            }
            _parts.Clear();
            _following.Clear();
            _covers.Clear();

            _eyeL = _eyeR = _pupilL = _pupilR = null;
            bool originalEyes = AddOriginal(1, look.Expression, look.EyeOffset);
            bool originalMouth = (look.Expression == 0 || look.Expression >= OriginalCharacterArt.EyeStart || look.Mouth >= OriginalCharacterArt.MouthStart) && AddOriginal(3, look.Mouth, look.MouthOffset);
            if (!originalEyes)
            {
            _partOffset = look.EyeOffset;
            _eyeL = Part("EyeL", SpriteFactory.Circle, new Vector2(-0.13f, 0.1f), new Vector2(0.25f, 0.25f), 0f, Color.white, 13, true).transform;
            _eyeR = Part("EyeR", SpriteFactory.Circle, new Vector2(0.13f, 0.1f), new Vector2(0.25f, 0.25f), 0f, Color.white, 13, true).transform;
            _pupilL = Part("PupilL", SpriteFactory.Circle, new Vector2(-0.13f, 0.1f), new Vector2(0.12f, 0.12f), 0f, Ink, 14, false).transform;
            _pupilR = Part("PupilR", SpriteFactory.Circle, new Vector2(0.13f, 0.1f), new Vector2(0.12f, 0.12f), 0f, Ink, 14, false).transform;

            _partOffset = Vector2.zero;
            BuildExpression(look.Expression);
            }
            _partOffset = look.MouthOffset;
            if (!originalMouth && look.Mouth > 0) BuildMouth(look.Mouth);
            _partOffset = look.AccessoryOffset; if (!AddOriginal(2, look.Accessory, look.AccessoryOffset)) BuildAccessory(look.Accessory);
            _partOffset = look.Accessory2Offset; if (!AddOriginal(4, look.Accessory2, look.Accessory2Offset)) BuildAccessory(look.Accessory2);
            _partOffset = Vector2.zero; BuildHat(look.Hat);
        }

        private bool AddOriginal(int category, int value, Vector2 offset)
        {
            var item = OriginalCharacterArt.Resolve(category, value);
            if (item == null) return false;
            var go = OriginalCharacterArt.Create(item, _root, offset, _visible);
            _parts.Add(go);
            return true;
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            foreach (var go in _parts)
                if (go != null) foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true)) sr.enabled = visible;
        }

        public void SetBodyColor(Color c)
        {
            if (c == _bodyColor) return;
            _bodyColor = c;
            Apply(Look, _visible);
        }

        /// <summary>Parts drawn in the body colour (mouth shaping) follow the body's flash/tint every frame.</summary>
        public void TintCovers(Color current)
        {
            foreach (var sr in _covers) if (sr != null) sr.color = current;
        }

        /// <summary>Per frame: eyes look toward aim, blinking, closed eyes when dead.</summary>
        public void Tick(Vector2 aim, bool dead)
        {
            Vector2 look = aim.sqrMagnitude > 0.01f ? aim.normalized : Vector2.right;
            var shift = new Vector3(look.x * 0.08f, look.y * 0.06f, 0f);
            foreach (var kv in _following) if (kv.Key != null) kv.Key.localPosition = kv.Value + shift;

            if (_eyeL == null) return;
            if (Time.time > _blinkAt) { _blinkUntil = Time.time + 0.11f; _blinkAt = Time.time + Random.Range(2f, 5f); }
            bool blink = Time.time < _blinkUntil || dead;
            EyeShape(Look.Expression, out float eyeL, out float eyeR, out float pupil, out float pupilDrop);
            if (blink) { eyeL = Mathf.Min(eyeL, 0.04f); eyeR = Mathf.Min(eyeR, 0.04f); }
            _eyeL.localScale = new Vector3(0.25f, eyeL, 1f);
            _eyeR.localScale = new Vector3(0.25f, eyeR, 1f);
            Vector3 p = new Vector3(look.x, look.y, 0f) * 0.05f + new Vector3(0f, -pupilDrop, 0f);
            _pupilL.localPosition = _eyeL.localPosition + p;
            _pupilR.localPosition = _eyeR.localPosition + p;
            _pupilL.localScale = new Vector3(pupil, Mathf.Min(pupil, eyeL * 0.5f), 1f);
            _pupilR.localScale = new Vector3(pupil, Mathf.Min(pupil, eyeR * 0.5f), 1f);
        }

        /// <summary>Eye heights (left, right), pupil size and how far pupils sit below the eye centre.</summary>
        private static void EyeShape(int expression, out float left, out float right, out float pupil, out float drop)
        {
            left = right = 0.25f; pupil = 0.12f; drop = 0f;
            switch (expression)
            {
                case 1: left = right = 0.2f; break;                         // 화남
                case 2: left = right = 0.11f; pupil = 0.1f; drop = 0.01f; break; // 졸림
                case 3: left = right = 0.31f; pupil = 0.08f; break;         // 놀람
                case 4: left = right = 0.08f; pupil = 0f; break;            // 행복 (웃는 눈)
                case 5: left = 0.22f; right = 0.17f; break;                 // 자신만만
                case 6: left = right = 0.22f; drop = 0.025f; break;         // 슬픔
                case 7: right = 0.04f; break;                               // 윙크
            }
        }

        // ------------------------------------------------------------ parts

        private void BuildExpression(int e)
        {
            switch (e)
            {
                case 1: // 화남: brows slanting down to the centre
                    Brow(-1, -22f, 0.16f);
                    Brow(1, 22f, 0.16f);
                    break;
                case 3: // 놀람: round mouth
                    Part("Mouth", SpriteFactory.Circle, new Vector2(0f, -0.17f), new Vector2(0.09f, 0.11f), 0f, Ink, 14, true);
                    break;
                case 4: // 행복: smile
                    Part("Mouth", SpriteFactory.Circle, new Vector2(0f, -0.14f), new Vector2(0.17f, 0.09f), 0f, Ink, 14, true);
                    Cover(new Vector2(0f, -0.115f), new Vector2(0.19f, 0.05f));
                    break;
                case 5: // 자신만만: one raised brow, smirk
                    Brow(-1, 0f, 0.19f);
                    Brow(1, 14f, 0.15f);
                    Part("Mouth", SpriteFactory.Square, new Vector2(0.05f, -0.16f), new Vector2(0.13f, 0.03f), 12f, Ink, 14, true);
                    break;
                case 6: // 슬픔: brows rising to the centre, flat mouth
                    Brow(-1, 18f, 0.17f);
                    Brow(1, -18f, 0.17f);
                    Part("Mouth", SpriteFactory.Square, new Vector2(0f, -0.17f), new Vector2(0.1f, 0.025f), 0f, Ink, 14, true);
                    break;
                case 7: // 윙크: smile
                    Part("Mouth", SpriteFactory.Circle, new Vector2(0.01f, -0.15f), new Vector2(0.13f, 0.07f), 0f, Ink, 14, true);
                    Cover(new Vector2(0.01f, -0.13f), new Vector2(0.15f, 0.04f));
                    break;
            }
        }

        /// <summary>Body-coloured square that cuts the top off a dark ellipse to make a smile.</summary>
        private void Cover(Vector2 pos, Vector2 size)
        {
            _covers.Add(Part("MouthCover", SpriteFactory.Square, pos, size, 0f, _bodyColor, 15, true).GetComponent<SpriteRenderer>());
        }

        private void Brow(int side, float angle, float height)
        {
            Part(side < 0 ? "BrowL" : "BrowR", SpriteFactory.Square, new Vector2(0.13f * side, 0.1f + height), new Vector2(0.17f, 0.045f), angle, Ink, 15, true);
        }

        private void BuildAccessory(int a)
        {
            switch (a)
            {
                case 1: // 볼터치
                    foreach (int s in new[] { -1, 1 })
                        Part("Blush", SpriteFactory.Circle, new Vector2(0.23f * s, -0.05f), new Vector2(0.13f, 0.075f), 0f, new Color(1f, 0.5f, 0.6f, 0.75f), 12, true);
                    break;
                case 2: // 안경
                    foreach (int s in new[] { -1, 1 })
                        Part("Lens", SpriteFactory.Ring, new Vector2(0.13f * s, 0.1f), new Vector2(0.31f, 0.31f), 0f, new Color(0.12f, 0.12f, 0.16f), 16, true);
                    Part("Bridge", SpriteFactory.Square, new Vector2(0f, 0.12f), new Vector2(0.05f, 0.025f), 0f, new Color(0.12f, 0.12f, 0.16f), 16, true);
                    break;
                case 3: // 선글라스
                    foreach (int s in new[] { -1, 1 })
                    {
                        Part("Shade", SpriteFactory.Circle, new Vector2(0.13f * s, 0.1f), new Vector2(0.28f, 0.22f), 0f, new Color(0.06f, 0.06f, 0.08f, 0.97f), 16, true);
                        Part("ShadeShine", SpriteFactory.Circle, new Vector2(0.13f * s - 0.05f, 0.14f), new Vector2(0.06f, 0.04f), 0f, new Color(1f, 1f, 1f, 0.55f), 17, true);
                    }
                    Part("Bridge", SpriteFactory.Square, new Vector2(0f, 0.12f), new Vector2(0.06f, 0.03f), 0f, new Color(0.06f, 0.06f, 0.08f), 16, true);
                    break;
                case 4: // 콧수염
                    foreach (int s in new[] { -1, 1 })
                        Part("Mustache", SpriteFactory.Circle, new Vector2(0.075f * s, -0.075f), new Vector2(0.16f, 0.07f), -16f * s, new Color(0.3f, 0.18f, 0.1f), 16, true);
                    break;
                case 5: // 반창고
                    Part("Bandage", SpriteFactory.Square, new Vector2(0.22f, -0.1f), new Vector2(0.17f, 0.065f), 30f, new Color(0.96f, 0.84f, 0.68f), 16, true);
                    Part("BandagePad", SpriteFactory.Square, new Vector2(0.22f, -0.1f), new Vector2(0.06f, 0.05f), 30f, new Color(0.88f, 0.7f, 0.55f), 17, true);
                    break;
                case 6: // 외알 안경
                    Part("Monocle", SpriteFactory.Ring, new Vector2(0.13f, 0.1f), new Vector2(0.33f, 0.33f), 0f, new Color(1f, 0.8f, 0.3f), 16, true);
                    Part("Chain", SpriteFactory.Square, new Vector2(0.24f, -0.04f), new Vector2(0.02f, 0.17f), 20f, new Color(1f, 0.8f, 0.3f), 16, true);
                    break;
            }
        }

        private void BuildHat(int h)
        {
            var gold = new Color(1f, 0.8f, 0.25f);
            switch (h)
            {
                case 1: // 왕관
                    Part("CrownBand", SpriteFactory.Square, new Vector2(0f, 0.46f), new Vector2(0.46f, 0.11f), 0f, gold, 17, false);
                    foreach (float x in new[] { -0.17f, 0f, 0.17f })
                        Part("CrownPoint", SpriteFactory.Triangle, new Vector2(x, 0.57f), new Vector2(0.14f, 0.17f), 0f, gold, 17, false);
                    Part("CrownJewel", SpriteFactory.Circle, new Vector2(0f, 0.46f), new Vector2(0.07f, 0.07f), 0f, new Color(0.9f, 0.15f, 0.25f), 18, false);
                    break;
                case 2: // 고깔모자
                    Part("PartyHat", SpriteFactory.Triangle, new Vector2(0.06f, 0.62f), new Vector2(0.32f, 0.4f), -12f, new Color(0.55f, 0.4f, 1f), 17, false);
                    Part("PartyStripe", SpriteFactory.Square, new Vector2(0.045f, 0.55f), new Vector2(0.2f, 0.04f), -12f, new Color(1f, 0.85f, 0.3f), 18, false);
                    Part("PartyPom", SpriteFactory.Circle, new Vector2(0.1f, 0.82f), new Vector2(0.1f, 0.1f), 0f, Color.white, 18, false);
                    break;
                case 3: // 고양이 귀
                    foreach (int s in new[] { -1, 1 })
                    {
                        Part("Ear", SpriteFactory.Triangle, new Vector2(0.24f * s, 0.43f), new Vector2(0.24f, 0.26f), -20f * s, Color.Lerp(_bodyColor, Color.black, 0.18f), 9, false);
                        Part("EarInner", SpriteFactory.Triangle, new Vector2(0.235f * s, 0.43f), new Vector2(0.12f, 0.14f), -20f * s, new Color(1f, 0.62f, 0.7f), 10, false);
                    }
                    break;
                case 4: // 악마 뿔
                    foreach (int s in new[] { -1, 1 })
                        Part("Horn", SpriteFactory.Triangle, new Vector2(0.2f * s, 0.5f), new Vector2(0.12f, 0.21f), -14f * s, new Color(0.75f, 0.12f, 0.15f), 17, false);
                    break;
                case 5: // 새싹
                    Part("Stem", SpriteFactory.Square, new Vector2(0f, 0.55f), new Vector2(0.035f, 0.14f), 0f, new Color(0.3f, 0.7f, 0.3f), 17, false);
                    foreach (int s in new[] { -1, 1 })
                        Part("Leaf", SpriteFactory.Circle, new Vector2(0.08f * s, 0.63f), new Vector2(0.16f, 0.08f), 25f * s, new Color(0.45f, 0.9f, 0.4f), 17, false);
                    break;
                case 6: // 리본
                    foreach (int s in new[] { -1, 1 })
                        Part("Bow", SpriteFactory.Triangle, new Vector2(0.2f + 0.08f * s, 0.41f), new Vector2(0.15f, 0.15f), 90f * s, new Color(1f, 0.4f, 0.62f), 17, false);
                    Part("BowKnot", SpriteFactory.Circle, new Vector2(0.2f, 0.41f), new Vector2(0.07f, 0.07f), 0f, new Color(0.9f, 0.25f, 0.5f), 18, false);
                    break;
                case 7: // 천사 고리
                    Part("Halo", SpriteFactory.Ring, new Vector2(0f, 0.68f), new Vector2(0.5f, 0.14f), 0f, new Color(1f, 0.9f, 0.45f), 17, false);
                    break;
                case 8: // 신사 모자
                    var black = new Color(0.1f, 0.1f, 0.12f);
                    Part("HatBrim", SpriteFactory.Square, new Vector2(0f, 0.46f), new Vector2(0.62f, 0.06f), 0f, black, 17, false);
                    Part("HatCrown", SpriteFactory.Square, new Vector2(0f, 0.63f), new Vector2(0.38f, 0.32f), 0f, black, 17, false);
                    Part("HatBand", SpriteFactory.Square, new Vector2(0f, 0.51f), new Vector2(0.38f, 0.06f), 0f, new Color(0.85f, 0.2f, 0.25f), 18, false);
                    break;
                case 9: // 더듬이
                    foreach (int s in new[] { -1, 1 })
                    {
                        Part("Antenna", SpriteFactory.Square, new Vector2(0.1f * s, 0.6f), new Vector2(0.03f, 0.26f), -15f * s, Color.Lerp(_bodyColor, Color.black, 0.3f), 17, false);
                        Part("AntennaTip", SpriteFactory.Circle, new Vector2(0.135f * s, 0.73f), new Vector2(0.1f, 0.1f), 0f, new Color(1f, 0.9f, 0.35f), 18, false);
                    }
                    break;
            }
        }

        private void BuildMouth(int mouth)
        {
            if (mouth == 1) return;
            Vector2 size = mouth == 3 ? new Vector2(0.23f,0.16f) : mouth == 4 ? new Vector2(0.11f,0.14f) : mouth == 7 ? new Vector2(0.07f,0.05f) : new Vector2(0.2f,0.04f);
            var sprite = mouth == 5 || mouth == 6 ? SpriteFactory.Square : SpriteFactory.Circle;
            Part("CustomMouth", sprite, new Vector2(0,-0.17f), size, mouth == 6 ? -15 : 0, Ink, 14, true);
            if (mouth == 2) Part("CustomMouthCover", SpriteFactory.Circle, new Vector2(0,-0.145f), new Vector2(0.21f,0.035f), 0, _bodyColor, 15, true);
        }

        private GameObject Part(string name, Sprite sprite, Vector2 pos, Vector2 size, float angle, Color color, int order, bool followsAim)
        {
            if (name.StartsWith("Mouth")) { if (Look.Mouth > 0) size = Vector2.zero; pos += Look.MouthOffset; }
            else if (name.StartsWith("Brow")) pos += Look.EyeOffset;
            pos += _partOffset;
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = SpriteFactory.Material;
            sr.color = color;
            sr.sortingOrder = order;
            sr.enabled = _visible;
            _parts.Add(go);
            if (followsAim) _following.Add(new KeyValuePair<Transform, Vector3>(go.transform, pos));
            return go;
        }
    }
}

