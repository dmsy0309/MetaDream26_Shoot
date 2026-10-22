using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Metadream.Shoot.Cards;
using Metadream.Shoot.Player;
using static Metadream.Shoot.Presentation.UiKit;

namespace Metadream.Shoot.Presentation
{
    /// <summary>
    /// Cheat card search (settings → 치트 대전, or any offline game in debug mode): type a name, click a card, and it is
    /// applied to P1 only. F8 or the on-screen button opens it; the game pauses while it is open.
    /// </summary>
    public sealed class CheatPanel : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        private string _query = "";
        private Vector2 _scroll;
        private bool _focus;
        private float _prevScale = 1f;
        private string _toast;
        private float _toastUntil;
        private List<CardDefinition> _results = new List<CardDefinition>();
        private string _lastQuery;

        /// <summary>Cards whose title, English name, id or description contain every word of the query (case-insensitive).</summary>
        public static List<CardDefinition> Search(string query)
        {
            var words = (query ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<CardDefinition>();
            foreach (var c in CardCatalog.All)
            {
                string hay = (c.Title + " " + c.Subtitle + " " + c.Id + " " + c.Description).ToLowerInvariant();
                bool ok = true;
                foreach (var w in words) ok &= hay.Contains(w);
                if (ok) list.Add(c);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.Title, b.Title));
            return list;
        }

        private static bool Available
        {
            get
            {
                var flow = GameFlow.Instance;
                return flow != null && !flow.InMenu && flow.HasMatch && flow.CanUseDebugCommands && !flow.IsSandbox
                       && (flow.IsCheatMatch || DevSettings.DebugMode);
            }
        }

        private void Update()
        {
            if (!Available) { if (IsOpen) Close(); return; }
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f8Key.wasPressedThisFrame) { if (IsOpen) Close(); else Open(); }
            else if (IsOpen && kb.escapeKey.wasPressedThisFrame) Close();
        }

        public void Open()
        {
            if (IsOpen || !Available) return;
            IsOpen = true;
            _prevScale = Time.timeScale;
            Time.timeScale = 0f;
            Fighter.InputSuspended = true;
            _focus = true;
            _lastQuery = null;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Time.timeScale = _prevScale > 0.01f ? _prevScale : 1f;
            Fighter.InputSuspended = false;
            GUI.FocusControl(null);
        }

        public void SetQuery(string query) => _query = query ?? "";

        private void OnDisable()
        {
            if (IsOpen) Close();
        }

        private void Give(CardDefinition card)
        {
            var flow = GameFlow.Instance;
            if (flow == null || flow.Fighters.Count == 0) return;
            flow.DebugGiveCard(card);
            Audio.Sfx.Play("card_pick");
            _toast = "적용: " + card.Title + " → " + flow.Fighters[0].DisplayName;
            _toastUntil = Time.unscaledTime + 2.5f;
        }

        private void OnGUI()
        {
            if (!Available) return;
            var flow = GameFlow.Instance;
            UiKit.Begin();
            GUI.depth = -80;
            if (!IsOpen)
            {
                if (flow.IsCheatMatch && !flow.IsPicking)
                {
                    var b = new Rect(W - 300, H - 116, 280, 56);
                    Fill(b, new Color(1f, 0.8f, 0.25f, 0.95f));
                    Text(b, "카드 검색 (F8)", new GUIStyle(Small) { fontStyle = FontStyle.Bold }, Palette.Ink);
                    if (Clicked(b)) Open();
                    Text(new Rect(W - 520, H - 56, 500, 30), "치트 대전 · F2 맵 · F4 적 처치 · F6 무적 · F7 슬로모", new GUIStyle(Tiny) { alignment = TextAnchor.MiddleRight }, new Color(1, 1, 1, 0.7f));
                }
                DrawToast();
                return;
            }

            Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.6f));
            var panel = new Rect(W / 2 - 520, 90, 1040, 880);
            Fill(panel, new Color(0.08f, 0.08f, 0.11f, 0.97f));
            Fill(new Rect(panel.x, panel.y, panel.width, 8), new Color(1f, 0.8f, 0.25f));
            Shadowed(new Rect(panel.x, panel.y + 18, panel.width, 56), "치트 · 카드 검색", Big, new Color(1f, 0.8f, 0.25f));
            string who = flow.Fighters.Count > 0 ? flow.Fighters[0].DisplayName : "P1";
            Text(new Rect(panel.x, panel.y + 80, panel.width, 30), "고른 카드는 " + who + "(나)에게만 적용됩니다 · 게임 일시정지 중 · Enter: 첫 결과 적용 · Esc/F8: 닫기", Small, new Color(1, 1, 1, 0.7f));

            GUI.SetNextControlName("cheatsearch");
            _query = GUI.TextField(new Rect(panel.x + 30, panel.y + 124, panel.width - 60, 64), _query ?? "", 40, Field);
            if (_focus && Event.current.type == EventType.Repaint) { GUI.FocusControl("cheatsearch"); _focus = false; }
            if (_query != _lastQuery) { _results = Search(_query); _lastQuery = _query; _scroll = Vector2.zero; }
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && _results.Count > 0) { Give(_results[0]); e.Use(); }

            Text(new Rect(panel.x + 30, panel.y + 196, 600, 28), "결과 " + _results.Count + "장 (이름 · 영문명 · id · 설명 검색)", new GUIStyle(Tiny) { fontSize = 18 }, new Color(1, 1, 1, 0.6f));
            var view = new Rect(panel.x + 30, panel.y + 230, panel.width - 60, 520);
            const float row = 62f;
            var content = new Rect(0, 0, view.width - 24, Mathf.Max(view.height, _results.Count * row));
            _scroll = GUI.BeginScrollView(view, _scroll, content);
            int first = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / row) - 1);
            int last = Mathf.Min(_results.Count, first + Mathf.CeilToInt(view.height / row) + 2);
            for (int i = first; i < last; i++)
            {
                var c = _results[i];
                var r = new Rect(0, i * row, content.width, row - 6);
                Fill(r, new Color(1, 1, 1, i % 2 == 0 ? 0.08f : 0.05f));
                Color rc = c.Rarity == CardRarity.Rare ? Palette.Rare : c.Rarity == CardRarity.Uncommon ? Palette.Uncommon : Palette.Common;
                Fill(new Rect(r.x, r.y, 8, r.height), rc);
                Text(new Rect(r.x + 22, r.y, 300, r.height), c.Title, new GUIStyle(Small) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold }, Color.white);
                Text(new Rect(r.x + 330, r.y, 330, r.height), c.Subtitle + "  ·  " + c.Id, new GUIStyle(Tiny) { fontSize = 17, clipping = TextClipping.Clip }, new Color(1, 1, 1, 0.55f));
                string fx = c.Effects != null && c.Effects.Length > 0 ? "효과 " + c.Effects.Length : "";
                Text(new Rect(r.xMax - 260, r.y, 120, r.height), fx, new GUIStyle(Tiny) { alignment = TextAnchor.MiddleRight }, new Color(1f, 0.85f, 0.4f));
                var add = new Rect(r.xMax - 128, r.y + 8, 116, r.height - 16);
                Fill(add, new Color(1f, 0.8f, 0.25f, 0.9f));
                Text(add, "적용", new GUIStyle(Small) { fontSize = 22, fontStyle = FontStyle.Bold }, Palette.Ink);
                if (Clicked(r)) { var card = c; Give(card); }
            }
            GUI.EndScrollView();

            var me = flow.Fighters.Count > 0 ? flow.Fighters[0] : null;
            if (me != null)
            {
                var names = new List<string>();
                foreach (var c in me.Cards) names.Add(c.Title);
                Text(new Rect(panel.x + 30, panel.y + 760, panel.width - 60, 54), "내 카드 (" + names.Count + "): " + (names.Count > 0 ? string.Join(", ", names) : "없음"),
                    new GUIStyle(Small) { alignment = TextAnchor.UpperLeft, wordWrap = true, fontSize = 20 }, new Color(1, 1, 1, 0.8f));
            }
            var close = new Rect(panel.xMax - 230, panel.yMax - 66, 200, 50);
            Fill(close, new Color(1, 1, 1, 0.18f));
            Text(close, "닫기 (Esc)", Small, Color.white);
            if (Clicked(close)) Close();
            DrawToast();
        }

        private void DrawToast()
        {
            if (_toast == null || Time.unscaledTime > _toastUntil) return;
            var r = new Rect(W / 2 - 420, 30, 840, 50);
            Fill(r, new Color(0.1f, 0.45f, 0.2f, 0.9f));
            Text(r, _toast, Small, Color.white);
        }
    }
}
