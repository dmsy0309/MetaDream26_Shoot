using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using Metadream.Shoot.Abilities;
using Metadream.Shoot.Cards;
using Metadream.Shoot.Content;
using Metadream.Shoot.Maps;
using static Metadream.Shoot.Presentation.UiKit;

namespace Metadream.Shoot.Presentation
{
    /// <summary>
    /// Debug-mode content browser (settings → 콘텐츠 도감): every card and map at a glance, click for details
    /// (card face, modifiers, effects, source file / map preview with gimmicks, counts, a cheat duel on that map).
    /// Drawn by FrontEnd while the Codex page is open.
    /// </summary>
    public sealed class ContentBrowser
    {
        public enum Tab { Cards, Maps }

        public Tab Current = Tab.Cards;
        public string Query = "";
        private Vector2 _scroll;
        private Vector2 _detailScroll;
        private CardDefinition _card;
        private MapDefinition _map;

        /// <summary>Set by FrontEnd: start a 1v1 cheat duel on the given map id.</summary>
        public Action<string> PlayMap;
        public Action Close;

        public static List<MapDefinition> FilterMaps(string query)
        {
            var words = (query ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<MapDefinition>();
            foreach (var m in MapCatalog.All)
            {
                string hay = (m.Name + " " + m.Id).ToLowerInvariant();
                bool ok = true;
                foreach (var w in words) ok &= hay.Contains(w);
                if (ok) list.Add(m);
            }
            return list;
        }

        public void SelectFirst()
        {
            if (Current == Tab.Cards) { var c = CheatPanel.Search(Query); _card = c.Count > 0 ? c[0] : null; }
            else { var m = FilterMaps(Query); _map = m.Find(x => x.Movers.Length > 0 || x.Zones.Length > 0) ?? (m.Count > 0 ? m[0] : null); }
        }

        public void Draw()
        {
            Shadowed(new Rect(0, 24, W, 70), "콘텐츠 도감", Big, Palette.PlayerOne);
            for (int t = 0; t < 2; t++)
            {
                bool sel = (int)Current == t;
                var r = new Rect(40 + t * 230, 110, 220, 54);
                Fill(r, sel ? new Color(1, 1, 1, 0.95f) : new Color(1, 1, 1, 0.14f));
                Text(r, t == 0 ? "카드 " + CardCatalog.All.Count : "맵 " + MapCatalog.All.Count, Mid, sel ? Palette.Ink : Color.white);
                if (Clicked(r) && (int)Current != t) { Current = (Tab)t; _scroll = Vector2.zero; }
            }
            Text(new Rect(520, 110, 80, 54), "검색", Small, new Color(1, 1, 1, 0.7f));
            Query = GUI.TextField(new Rect(600, 112, 360, 50), Query ?? "", 40, new GUIStyle(Field) { fontSize = 24 });
            var back = new Rect(W - 260, 110, 220, 54);
            Fill(back, new Color(1, 1, 1, 0.18f));
            Text(back, "뒤로 (Esc)", Small, Color.white);
            if (Clicked(back)) Close?.Invoke();

            var list = new Rect(40, 184, 920, 860);
            Fill(list, new Color(0, 0, 0, 0.3f));
            var detail = new Rect(990, 184, 890, 860);
            Fill(detail, new Color(0, 0, 0, 0.3f));
            if (Current == Tab.Cards) { DrawCardList(list); DrawCardDetail(detail); }
            else { DrawMapList(list); DrawMapDetail(detail); }
        }

        // ------------------------------------------------------------ cards

        private void DrawCardList(Rect area)
        {
            var cards = CheatPanel.Search(Query);
            const int cols = 3;
            const float tw = 284, th = 92, gap = 8;
            int rows = (cards.Count + cols - 1) / cols;
            var content = new Rect(0, 0, area.width - 24, Mathf.Max(area.height - 20, rows * (th + gap) + gap));
            _scroll = GUI.BeginScrollView(new Rect(area.x + 6, area.y + 10, area.width - 12, area.height - 20), _scroll, content);
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                var r = new Rect(gap + (i % cols) * (tw + gap), gap + (i / cols) * (th + gap), tw, th);
                if (r.yMax < _scroll.y - 10 || r.y > _scroll.y + area.height) continue;
                bool sel = c == _card;
                Fill(r, sel ? new Color(1f, 0.95f, 0.85f) : new Color(0.97f, 0.96f, 0.93f, 0.92f));
                Fill(new Rect(r.x, r.y, 10, r.height), RarityColor(c.Rarity));
                Text(new Rect(r.x + 20, r.y + 6, r.width - 30, 36), c.Title, new GUIStyle(Small) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip }, Palette.Ink);
                Text(new Rect(r.x + 20, r.y + 42, r.width - 30, 22), c.Subtitle, new GUIStyle(Tiny) { fontSize = 15, clipping = TextClipping.Clip }, new Color(0, 0, 0, 0.5f));
                Text(new Rect(r.x + 20, r.y + 64, r.width - 30, 22), c.Id + (c.Effects != null && c.Effects.Length > 0 ? "  · 효과 " + c.Effects.Length : ""), new GUIStyle(Tiny) { fontSize = 14, clipping = TextClipping.Clip }, new Color(0, 0, 0, 0.4f));
                if (Clicked(r)) { _card = c; _detailScroll = Vector2.zero; }
            }
            GUI.EndScrollView();
            if (cards.Count == 0) Text(area, "검색 결과 없음", Mid, new Color(1, 1, 1, 0.5f));
        }

        private void DrawCardDetail(Rect area)
        {
            if (_card == null) { Text(area, "왼쪽에서 카드를 누르면 자세한 정보가 나옵니다", Small, new Color(1, 1, 1, 0.5f)); return; }
            GameHud.DrawCard(new Rect(area.x + 24, area.y + 24, 300, 480), _card, false, Palette.PlayerOne);
            var lines = new List<string>
            {
                "id: " + _card.Id,
                "희귀도: " + _card.Rarity + " · 중복 획득 " + (_card.AllowMultiple ? "가능" : "불가"),
                "분류: " + Join(_card.Categories) + " · 제외 분류: " + Join(_card.BlacklistedCategories),
                "",
                "능력치 변경 (modifiers):"
            };
            var defaults = new CardModifiers();
            int changed = 0;
            foreach (var f in typeof(CardModifiers).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v = f.GetValue(_card.Modifiers), d = f.GetValue(defaults);
                if (Equals(v, d)) continue;
                lines.Add("  " + f.Name + " = " + (v is float fl ? fl.ToString("0.###") : v));
                changed++;
            }
            if (changed == 0) lines.Add("  (없음)");
            lines.Add("");
            lines.Add("효과 (effects):");
            if (_card.Effects == null || _card.Effects.Length == 0) lines.Add("  (없음)");
            else
                foreach (var e in _card.Effects)
                {
                    var p = new StringBuilder("  " + e.type);
                    if (e.radius != 0) p.Append(" radius=" + e.radius);
                    if (e.damage != 0) p.Append(" damage=" + e.damage);
                    if (e.amount != 0) p.Append(" amount=" + e.amount);
                    if (e.duration != 0) p.Append(" duration=" + e.duration);
                    if (e.chance != 1f) p.Append(" chance=" + e.chance);
                    lines.Add(p.ToString());
                    string summary = EffectRegistry.SummaryOf(e.type);
                    if (!string.IsNullOrEmpty(summary)) lines.Add("     ↳ " + summary);
                }
            lines.Add("");
            lines.Add("파일: " + _card.Source);
            DrawLines(new Rect(area.x + 350, area.y + 24, area.width - 370, area.height - 48), lines);
        }

        // ------------------------------------------------------------ maps

        private void DrawMapList(Rect area)
        {
            var maps = FilterMaps(Query);
            const int cols = 3;
            const float tw = 284, th = 204, gap = 8;
            int rows = (maps.Count + cols - 1) / cols;
            var content = new Rect(0, 0, area.width - 24, Mathf.Max(area.height - 20, rows * (th + gap) + gap));
            _scroll = GUI.BeginScrollView(new Rect(area.x + 6, area.y + 10, area.width - 12, area.height - 20), _scroll, content);
            for (int i = 0; i < maps.Count; i++)
            {
                var m = maps[i];
                var r = new Rect(gap + (i % cols) * (tw + gap), gap + (i / cols) * (th + gap), tw, th);
                if (r.yMax < _scroll.y - 10 || r.y > _scroll.y + area.height) continue;
                bool sel = m == _map;
                Fill(r, sel ? Palette.PlayerOne : new Color(1, 1, 1, 0.12f));
                DrawMapPreview(new Rect(r.x + 6, r.y + 6, r.width - 12, (r.width - 12) * 40f / 71.12f), m, false);
                Text(new Rect(r.x + 8, r.yMax - 46, r.width - 16, 24), m.Name + (m.TutorialOnly ? " (튜토리얼)" : ""), new GUIStyle(Small) { fontSize = 20, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip }, sel ? Palette.Ink : Color.white);
                Text(new Rect(r.x + 8, r.yMax - 24, r.width - 16, 20), m.Id, new GUIStyle(Tiny) { fontSize = 14 }, sel ? new Color(0, 0, 0, 0.6f) : new Color(1, 1, 1, 0.5f));
                if (Clicked(r)) { _map = m; _detailScroll = Vector2.zero; }
            }
            GUI.EndScrollView();
            if (maps.Count == 0) Text(area, "검색 결과 없음", Mid, new Color(1, 1, 1, 0.5f));
        }

        private void DrawMapDetail(Rect area)
        {
            if (_map == null) { Text(area, "왼쪽에서 맵을 누르면 자세한 정보가 나옵니다", Small, new Color(1, 1, 1, 0.5f)); return; }
            var m = _map;
            Shadowed(new Rect(area.x + 24, area.y + 14, area.width - 48, 50), m.Name, new GUIStyle(Mid) { alignment = TextAnchor.MiddleLeft }, Palette.PlayerOne);
            var preview = new Rect(area.x + 24, area.y + 70, area.width - 48, (area.width - 48) * 40f / 71.12f);
            DrawMapPreview(preview, m, true);
            int pads = 0, hazards = 0, winds = 0;
            foreach (var z in m.Zones) { if (z.Kind == ZoneKind.JumpPad) pads++; else if (z.Kind == ZoneKind.Hazard) hazards++; else winds++; }
            var lines = new List<string>
            {
                "id: " + m.Id + (m.TutorialOnly ? " · 튜토리얼 전용 (일반 순환 제외)" : ""),
                "지형 " + m.Solids.Length + " · 상자 " + m.Boxes.Length + " · 움직이는 발판 " + m.Movers.Length + " · 점프대 " + pads + " · 피해 구역 " + hazards + " · 바람 " + winds + " · 스폰 " + m.Spawns.Length,
                "범례: 검정 지형 · 회색 상자 · 보라 발판(점선 = 경로) · 초록 점프대 · 빨강 피해 구역 · 하늘색 바람 · 원 = 스폰(팀 색)",
                "파일: " + m.Source
            };
            DrawLines(new Rect(area.x + 24, preview.yMax + 14, area.width - 48, 200), lines);
            if (!m.TutorialOnly && PlayMap != null)
            {
                var play = new Rect(area.x + 24, area.yMax - 80, 420, 60);
                Fill(play, new Color(1f, 0.8f, 0.25f));
                Text(play, "이 맵에서 치트 대전 1대1", Mid, Palette.Ink);
                if (Clicked(play)) PlayMap(m.Id);
            }
        }

        /// <summary>Scaled top-down view of a map inside r (arena ±35.56 × ±20).</summary>
        public static void DrawMapPreview(Rect r, MapDefinition m, bool detailed)
        {
            Fill(r, Palette.Background);
            float s = r.width / 71.12f;
            Vector2 c = r.center;
            Vector2 ToGui(Vector2 w) => new Vector2(c.x + w.x * s, c.y - w.y * s);
            void Block(MapBlock b, Color col)
            {
                Vector2 p = ToGui(b.Center);
                var rect = new Rect(p.x - b.Size.x * s / 2f, p.y - b.Size.y * s / 2f, Mathf.Max(1f, b.Size.x * s), Mathf.Max(1f, b.Size.y * s));
                if (Mathf.Abs(b.Rotation) < 0.5f) { Fill(rect, col); return; }
                var saved = GUI.matrix;
                GUIUtility.RotateAroundPivot(-b.Rotation, p);
                Fill(rect, col);
                GUI.matrix = saved;
            }
            GUI.BeginClip(r);
            c = new Vector2(r.width / 2f, r.height / 2f);
            foreach (var z in m.Zones)
            {
                Color zc = z.Kind == ZoneKind.JumpPad ? Palette.JumpPad : z.Kind == ZoneKind.Hazard ? new Color(Palette.Hazard.r, Palette.Hazard.g, Palette.Hazard.b, 0.8f) : new Color(0.55f, 0.8f, 1f, 0.35f);
                Block(z.Block, zc);
            }
            foreach (var b in m.Solids) Block(b, Palette.Solid);
            foreach (var b in m.Boxes) Block(b, Palette.Box);
            foreach (var mv in m.Movers)
            {
                if (detailed)
                    foreach (var p in mv.Path)
                    {
                        Vector2 g = ToGui(mv.Block.Center + p);
                        Fill(new Rect(g.x - 2, g.y - 2, 4, 4), new Color(0.45f, 0.3f, 0.8f, 0.8f));
                    }
                Block(mv.Block, new Color(0.45f, 0.3f, 0.8f));
            }
            for (int i = 0; i < m.Spawns.Length; i++)
            {
                Vector2 g = ToGui(m.Spawns[i]);
                Disc(g, detailed ? 9f : 5f, Palette.TeamColor(i % 2));
            }
            GUI.EndClip();
        }

        // ------------------------------------------------------------ helpers

        private void DrawLines(Rect area, List<string> lines)
        {
            var style = new GUIStyle(Tiny) { fontSize = 19, wordWrap = true, alignment = TextAnchor.UpperLeft };
            float width = area.width - 24, total = 0f;
            var heights = new float[lines.Count];
            for (int i = 0; i < lines.Count; i++) { heights[i] = Mathf.Max(26f, style.CalcHeight(new GUIContent(lines[i]), width)); total += heights[i]; }
            var content = new Rect(0, 0, width, Mathf.Max(area.height, total));
            _detailScroll = GUI.BeginScrollView(area, _detailScroll, content);
            float y = 0f;
            for (int i = 0; i < lines.Count; i++)
            {
                Text(new Rect(0, y, width, heights[i]), lines[i], style, Color.white);
                y += heights[i];
            }
            GUI.EndScrollView();
        }

        private static string Join(string[] a) => a == null || a.Length == 0 ? "-" : string.Join(", ", a);

        private static Color RarityColor(CardRarity r) => r == CardRarity.Rare ? Palette.Rare : r == CardRarity.Uncommon ? Palette.Uncommon : Palette.Common;
    }
}
