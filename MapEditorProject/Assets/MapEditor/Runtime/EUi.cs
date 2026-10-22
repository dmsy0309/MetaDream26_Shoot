using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Metadream.MapEditor
{
    /// <summary>IMGUI kit for the editor (dark theme, the game's bundled Noto Sans KR font, ~900 px reference height).</summary>
    public static class EUi
    {
        public static float Scale = 1f;
        public static float W = 1600f, H = 900f;
        public static GUIStyle Label, Small, Tiny, Header, Title, Button, ButtonOn, ButtonBad, Field, Area, ListItem, ListItemOn, Warn, Error, Ok, Center, Code, Gutter, Chip, ChipOn;
        private static Font _font, _bold;
        private static readonly Dictionary<string, string> Buffers = new Dictionary<string, string>();

        public static readonly Color PanelColor = new Color(0.105f, 0.115f, 0.15f, 0.98f);
        public static readonly Color PanelDark = new Color(0.075f, 0.082f, 0.108f, 1f);
        public static readonly Color Accent = new Color(0.36f, 0.58f, 1f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color TextDim = new Color(0.68f, 0.71f, 0.78f);
        public static readonly Color WarnColor = new Color(1f, 0.78f, 0.3f);
        public static readonly Color ErrorColor = new Color(1f, 0.42f, 0.38f);
        public static readonly Color OkColor = new Color(0.45f, 0.9f, 0.55f);

        public static void Begin()
        {
            Ensure();
            Scale = Mathf.Clamp(Screen.height / 900f, 0.7f, 3f);
            if (Screen.width / Scale < 1180f) Scale = Mathf.Max(0.55f, Screen.width / 1180f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
            W = Screen.width / Scale;
            H = Screen.height / Scale;
        }

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixels(new[] { c, c, c, c });
            t.Apply();
            return t;
        }

        private static void Ensure()
        {
            if (Label != null) return;
            _font = Resources.Load<Font>("Fonts/NotoSansKR-Regular");
            _bold = Resources.Load<Font>("Fonts/NotoSansKR-Bold");
            if (_font == null) _font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans KR", "Arial" }, 16);
            if (_bold == null) _bold = _font;
            Label = Make(_font, 14, TextAnchor.MiddleLeft, Color.white);
            Small = Make(_font, 12, TextAnchor.MiddleLeft, TextDim);
            Tiny = Make(_font, 11, TextAnchor.MiddleLeft, TextDim);
            Header = Make(_bold, 14, TextAnchor.MiddleLeft, new Color(0.85f, 0.9f, 1f));
            Title = Make(_bold, 20, TextAnchor.MiddleLeft, Color.white);
            Center = Make(_font, 14, TextAnchor.MiddleCenter, Color.white);
            Warn = Make(_font, 12, TextAnchor.UpperLeft, WarnColor);
            Error = Make(_font, 12, TextAnchor.UpperLeft, ErrorColor);
            Ok = Make(_font, 12, TextAnchor.UpperLeft, OkColor);
            Warn.wordWrap = Error.wordWrap = Ok.wordWrap = true;
            Area = Make(_font, 12, TextAnchor.UpperLeft, TextDim);
            Area.wordWrap = true;

            Button = Make(_font, 13, TextAnchor.MiddleCenter, Color.white);
            Button.normal.background = Tex(new Color(0.2f, 0.22f, 0.29f));
            Button.hover.background = Tex(new Color(0.27f, 0.3f, 0.4f));
            Button.active.background = Tex(new Color(0.33f, 0.38f, 0.52f));
            Button.hover.textColor = Button.active.textColor = Color.white;
            Button.padding = new RectOffset(6, 6, 2, 2);
            ButtonOn = new GUIStyle(Button);
            ButtonOn.normal.background = ButtonOn.hover.background = Tex(new Color(0.24f, 0.4f, 0.78f));
            ButtonBad = new GUIStyle(Button);
            ButtonBad.normal.background = Tex(new Color(0.45f, 0.18f, 0.18f));
            ButtonBad.hover.background = Tex(new Color(0.58f, 0.24f, 0.24f));

            ListItem = Make(_font, 12, TextAnchor.MiddleLeft, new Color(0.85f, 0.87f, 0.92f));
            ListItem.hover.background = Tex(new Color(1f, 1f, 1f, 0.06f));
            ListItem.hover.textColor = Color.white;
            ListItem.padding = new RectOffset(6, 4, 0, 0);
            ListItemOn = new GUIStyle(ListItem);
            ListItemOn.normal.background = ListItemOn.hover.background = Tex(new Color(0.24f, 0.4f, 0.78f, 0.8f));
            ListItemOn.normal.textColor = Color.white;

            Field = new GUIStyle(GUI.skin.textField) { font = _font, fontSize = 13, alignment = TextAnchor.MiddleLeft };
            Field.normal.background = Tex(new Color(0.06f, 0.065f, 0.085f));
            Field.focused.background = Tex(new Color(0.04f, 0.05f, 0.09f));
            Field.hover.background = Field.normal.background;
            Field.normal.textColor = Field.focused.textColor = Field.hover.textColor = Color.white;
            Field.padding = new RectOffset(5, 5, 2, 2);
            Field.border = new RectOffset(1, 1, 1, 1);

            Code = new GUIStyle(GUI.skin.textArea) { font = _font, fontSize = 14, wordWrap = false, richText = false, alignment = TextAnchor.UpperLeft, clipping = TextClipping.Overflow };
            Code.normal.background = Code.hover.background = Code.focused.background = Code.active.background = Tex(new Color(0f, 0f, 0f, 0f));
            Code.normal.textColor = Code.hover.textColor = Code.focused.textColor = Code.active.textColor = new Color(0.9f, 0.93f, 1f);
            Code.padding = new RectOffset(8, 8, 6, 6);
            Code.margin = new RectOffset(0, 0, 0, 0);
            Code.border = new RectOffset(0, 0, 0, 0);
            Gutter = Make(_font, 14, TextAnchor.UpperRight, new Color(0.45f, 0.5f, 0.6f));
            Chip = Make(_font, 12, TextAnchor.MiddleCenter, new Color(0.85f, 0.88f, 0.95f));
            Chip.normal.background = Tex(new Color(0.17f, 0.19f, 0.25f));
            Chip.hover.background = Tex(new Color(0.25f, 0.28f, 0.37f));
            Chip.hover.textColor = Color.white;
            Chip.padding = new RectOffset(6, 6, 1, 1);
            ChipOn = new GUIStyle(Chip);
            ChipOn.normal.background = ChipOn.hover.background = Tex(new Color(0.24f, 0.4f, 0.78f));
            ChipOn.normal.textColor = Color.white;
        }

        private static GUIStyle Make(Font font, int size, TextAnchor anchor, Color color) => new GUIStyle
        {
            font = font,
            fontSize = size,
            alignment = anchor,
            normal = { textColor = color },
            richText = false,
            clipping = TextClipping.Clip
        };

        public static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        public static void Frame(Rect r, Color c)
        {
            Fill(new Rect(r.x, r.y, r.width, 1), c);
            Fill(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            Fill(new Rect(r.x, r.y, 1, r.height), c);
            Fill(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }

        public static bool Btn(Rect r, string text, bool on = false, bool enabled = true, GUIStyle style = null)
        {
            bool old = GUI.enabled;
            GUI.enabled = old && enabled;
            bool clicked = GUI.Button(r, text, style ?? (on ? ButtonOn : Button));
            GUI.enabled = old;
            return clicked;
        }

        public static bool Toggle(Rect r, bool value, string text)
        {
            var box = new Rect(r.x, r.y + (r.height - 14) / 2, 14, 14);
            Fill(box, new Color(0.06f, 0.065f, 0.085f));
            Frame(box, new Color(1, 1, 1, 0.25f));
            if (value) Fill(new Rect(box.x + 3, box.y + 3, 8, 8), Accent);
            GUI.Label(new Rect(r.x + 20, r.y, r.width - 20, r.height), text, Label);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { GUIUtility.keyboardControl = 0; return !value; }
            return value;
        }

        public static string Text(string key, Rect r, string value)
        {
            GUI.SetNextControlName(key);
            return GUI.TextField(r, value ?? "", Field);
        }

        /// <summary>Float field that keeps the typed text while focused ("-", "1." ...) and returns the parsed value.</summary>
        public static float Float(string key, Rect r, float value)
        {
            GUI.SetNextControlName(key);
            bool focused = GUI.GetNameOfFocusedControl() == key;
            string formatted = MapDocument.Num(value);
            string shown = focused && Buffers.TryGetValue(key, out var b) ? b : formatted;
            string edited = GUI.TextField(r, shown, Field);
            if (!focused && edited == shown) { Buffers.Remove(key); return value; }
            Buffers[key] = edited;
            return TryParse(edited, out float v) ? v : value;
        }

        public static bool TryParse(string s, out float v) =>
            float.TryParse((s ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>Label + float field in one row.</summary>
        public static float Row(string key, Rect r, string label, float value, float labelWidth = 70f)
        {
            GUI.Label(new Rect(r.x, r.y, labelWidth, r.height), label, Small);
            return Float(key, new Rect(r.x + labelWidth, r.y, r.width - labelWidth, r.height), value);
        }

        public static string TextRow(string key, Rect r, string label, string value, float labelWidth = 70f)
        {
            GUI.Label(new Rect(r.x, r.y, labelWidth, r.height), label, Small);
            return Text(key, new Rect(r.x + labelWidth, r.y, r.width - labelWidth, r.height), value);
        }

        /// <summary>Colour as hex text + swatch + three channel sliders. Returns the edited colour.</summary>
        public static Color ColorRows(string key, ref float y, float x, float width, string label, Color c)
        {
            GUI.Label(new Rect(x, y, 70, 22), label, Small);
            Fill(new Rect(x + 70, y + 2, 26, 18), c);
            Frame(new Rect(x + 70, y + 2, 26, 18), new Color(1, 1, 1, 0.3f));
            string hexKey = key + "_hex";
            string hex = MapBackground.ToHex(c);
            GUI.SetNextControlName(hexKey);
            bool focused = GUI.GetNameOfFocusedControl() == hexKey;
            string shown = focused && Buffers.TryGetValue(hexKey, out var buffered) ? buffered : hex;
            string edited = GUI.TextField(new Rect(x + 102, y, width - 102, 22), shown, Field);
            if (focused || edited != shown)
            {
                Buffers[hexKey] = edited;
                string h = edited.Trim().TrimStart('#');
                if (h.Length == 6) c = MapBackground.ParseHex(h, c);
            }
            else Buffers.Remove(hexKey);
            y += 26;
            string[] names = { "R", "G", "B" };
            for (int i = 0; i < 3; i++)
            {
                GUI.Label(new Rect(x + 70, y, 16, 16), names[i], Tiny);
                float v = GUI.HorizontalSlider(new Rect(x + 88, y + 3, width - 88, 12), c[i], 0f, 1f);
                if (!Mathf.Approximately(v, c[i])) { c[i] = v; GUIUtility.keyboardControl = 0; }
                y += 17;
            }
            y += 4;
            return c;
        }

        public static void Divider(float x, float y, float w) => Fill(new Rect(x, y, w, 1), Line);
    }
}
