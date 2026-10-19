using UnityEngine;

namespace Metadream.Shoot.Presentation
{
    /// <summary>Shared IMGUI helpers in a 1920x1080 reference space.</summary>
    public static class UiKit
    {
        public const float W = 1920f, H = 1080f;
        private static Font _font;
        private static Font _bold;
        private static Texture2D _disc;
        public static GUIStyle Title, Big, Mid, Small, Tiny, CardTitle, CardBody, Field, TinyRight;

        public static void Begin()
        {
            Ensure();
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Screen.width / W, Screen.height / H, 1f));
        }

        private static void Ensure()
        {
            if (Title != null) return;
            // Bundled Noto Sans KR (SIL OFL 1.1) so Hangul renders identically on macOS and Windows,
            // independent of which system fonts are installed.
            _font = Resources.Load<Font>("Fonts/NotoSansKR-Regular");
            _bold = Resources.Load<Font>("Fonts/NotoSansKR-Bold");
            if (_font == null)
            {
                _font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Noto Sans KR", "Gulim", "Arial Unicode MS", "Arial" }, 32);
                Debug.LogWarning("[UI] bundled font missing, using OS font");
            }
            if (_bold == null) _bold = _font;
            Debug.Log("[UI] font=" + _font.name + " bold=" + _bold.name);
            _disc = SpriteFactory.Circle.texture;
            Title = Make(92, FontStyle.Bold, TextAnchor.MiddleCenter);
            Big = Make(64, FontStyle.Bold, TextAnchor.MiddleCenter);
            Mid = Make(34, FontStyle.Bold, TextAnchor.MiddleCenter);
            Small = Make(24, FontStyle.Normal, TextAnchor.MiddleCenter);
            Tiny = Make(19, FontStyle.Normal, TextAnchor.MiddleLeft);
            TinyRight = Make(19, FontStyle.Bold, TextAnchor.MiddleRight);
            CardTitle = Make(36, FontStyle.Bold, TextAnchor.MiddleCenter);
            CardBody = Make(22, FontStyle.Normal, TextAnchor.UpperCenter);
            CardBody.wordWrap = true;
            Field = new GUIStyle(GUI.skin.textField) { font = _font, fontSize = 40, alignment = TextAnchor.MiddleCenter };
            Field.normal.textColor = Color.white;
            Field.focused.textColor = Color.white;
        }

        private static GUIStyle Make(int size, FontStyle style, TextAnchor anchor)
        {
            // A real bold face is used instead of synthesized bold when available.
            bool realBold = style == FontStyle.Bold && _bold != _font;
            return new GUIStyle
            {
                font = realBold ? _bold : _font,
                fontSize = size,
                fontStyle = realBold ? FontStyle.Normal : style,
                alignment = anchor,
                normal = { textColor = Color.white },
                richText = false
            };
        }

        public static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        public static void Disc(Vector2 center, float radius, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2, radius * 2), _disc);
            GUI.color = old;
        }

        public static void Text(Rect r, string s, GUIStyle style, Color c)
        {
            var old = style.normal.textColor;
            style.normal.textColor = c;
            GUI.Label(r, s, style);
            style.normal.textColor = old;
        }

        public static void Shadowed(Rect r, string s, GUIStyle style, Color c)
        {
            Text(new Rect(r.x + 3, r.y + 3, r.width, r.height), s, style, new Color(0, 0, 0, 0.55f));
            Text(r, s, style, c);
        }

        /// <summary>Invisible click target over an already drawn rect.</summary>
        public static bool Clicked(Rect r)
        {
            bool hit = GUI.Button(r, GUIContent.none, GUIStyle.none);
            if (hit) Audio.Sfx.Play("ui_click");
            return hit;
        }

        public static Vector2 WorldToGui(Camera cam, Vector3 world)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            return new Vector2(sp.x * W / Screen.width, (Screen.height - sp.y) * H / Screen.height);
        }
    }
}

