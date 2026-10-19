using UnityEngine;
using Metadream.Shoot.Cards;
using Metadream.Shoot.Core;
using Metadream.Shoot.Network;
using Metadream.Shoot.Player;
using static Metadream.Shoot.Presentation.UiKit;

namespace Metadream.Shoot.Presentation
{
    /// <summary>In-match IMGUI HUD: team score, fighter overlays, owned cards, card pick, banners.</summary>
    public sealed class GameHud : MonoBehaviour
    {
        private bool _resultSound;

        private void OnGUI()
        {
            var flow = GameFlow.Instance;
            if (flow == null || flow.InMenu || !flow.HasMatch) return;
            Begin();
            DrawOverlays(flow);
            if (!flow.IsSandbox) DrawScore(flow); // the tutorial draws its own panel in that area
            DrawCardBars(flow);
            if (flow.IsPicking) DrawPick(flow);
            if (flow.Phase == MatchPhase.GameOver && flow.WinningTeam >= 0 && !string.IsNullOrEmpty(flow.Banner)) DrawResult(flow);
            else { DrawBanner(flow); if (flow.Phase != MatchPhase.GameOver) _resultSound = false; }
            string footer = flow.IsSandbox ? "Esc: 튜토리얼 종료" : flow.Role == NetRole.Client ? "Esc: 방 나가기" : flow.Role == NetRole.Host ? "Esc: 대기실로" : "Esc: 타이틀";
            if (!flow.ReplayMode) Shadowed(new Rect(20, H - 40, 600, 30), footer, Tiny, new Color(1, 1, 1, 0.8f));
        }

        private void DrawScore(GameFlow flow)
        {
            int rounds = flow.RoundsToWinGame;
            int points = flow.PointsToWinRound;
            for (int t = 0; t < 2; t++)
            {
                bool left = t == 0;
                float cx = W / 2 + (left ? -1 : 1) * 190;
                Color c = flow.SideColor(t);
                Shadowed(new Rect(cx - 170, 18, 340, 44), flow.SideName(t), Mid, c);
                for (int r = 0; r < rounds; r++)
                {
                    int idx = left ? rounds - 1 - r : r;
                    float x = cx + (r - (rounds - 1) * 0.5f) * 34;
                    bool won = flow.TeamRounds(t) > idx;
                    Disc(new Vector2(x, 82), 14, new Color(0, 0, 0, 0.35f));
                    Disc(new Vector2(x, 82), 11, won ? c : new Color(1, 1, 1, 0.6f));
                }
                for (int p = 0; p < points; p++)
                {
                    float x = cx + (p - (points - 1) * 0.5f) * 22;
                    bool got = flow.TeamPoints(t) > p;
                    Disc(new Vector2(x, 112), 7, got ? c : new Color(0, 0, 0, 0.25f));
                }
            }
        }

        private void DrawOverlays(GameFlow flow)
        {
            var cam = Camera.main;
            if (cam == null) return;
            bool names = flow.Fighters.Count > 2 || flow.Role != NetRole.Offline;
            foreach (var f in flow.Fighters)
            {
                if (f.IsDead || !f.IsVisible) continue;
                Vector2 top = WorldToGui(cam, f.Position + Vector2.up * (f.Radius + 1.1f));
                float w = 96f;
                if (names)
                {
                    bool me = flow.Role == NetRole.Client ? f.PlayerId == flow.LocalSlot : flow.Role == NetRole.Host && f.PlayerId == 0;
                    Shadowed(new Rect(top.x - 120, top.y - 50, 240, 24), me ? "▼ " + f.DisplayName : f.DisplayName, Small, f.Color);
                }
                var bar = new Rect(top.x - w / 2, top.y - 8, w, 12);
                Fill(new Rect(bar.x - 2, bar.y - 2, bar.width + 4, bar.height + 4), new Color(0, 0, 0, 0.55f));
                float hp = f.MaxHealth > 0f ? Mathf.Clamp01(f.Health / f.MaxHealth) : 0f;
                Fill(new Rect(bar.x, bar.y, bar.width * hp, bar.height), Color.Lerp(new Color(0.95f, 0.25f, 0.2f), new Color(0.3f, 0.9f, 0.4f), hp));

                int max = f.Ammo.maxAmmo;
                float pip = Mathf.Min(14f, 120f / Mathf.Max(1, max));
                for (int i = 0; i < max; i++)
                {
                    float x = top.x + (i - (max - 1) * 0.5f) * pip;
                    bool has = !f.Ammo.isReloading && i < f.Ammo.currentAmmo;
                    Disc(new Vector2(x, top.y + 16), pip * 0.36f, has ? new Color(0.15f, 0.15f, 0.15f, 0.9f) : new Color(0, 0, 0, 0.18f));
                }
                if (f.Ammo.isReloading)
                {
                    Fill(new Rect(top.x - w / 2, top.y + 26, w, 5), new Color(0, 0, 0, 0.35f));
                    Fill(new Rect(top.x - w / 2, top.y + 26, w * f.ReloadProgress, 5), f.Color);
                }
                float cd = f.BlockReadyProgress;
                if (cd < 1f)
                {
                    Fill(new Rect(top.x - w / 2, top.y - 18, w, 4), new Color(0, 0, 0, 0.3f));
                    Fill(new Rect(top.x - w / 2, top.y - 18, w * cd, 4), new Color(1, 1, 1, 0.9f));
                }
            }
        }

        private void DrawCardBars(GameFlow flow)
        {
            for (int t = 0; t < 2; t++)
            {
                bool left = t == 0;
                float x = left ? 18 : W - 18 - 200;
                float y = 150;
                bool header = flow.TeamSize > 1;
                foreach (var f in flow.Fighters)
                {
                    if (f.TeamId != t) continue;
                    if (header)
                    {
                        Shadowed(new Rect(x, y, 200, 26), f.DisplayName, Tiny, f.Color);
                        y += 28;
                    }
                    foreach (var card in f.Cards)
                    {
                        var r = new Rect(x, y, 200, 28);
                        Fill(r, new Color(0, 0, 0, 0.45f));
                        Fill(new Rect(left ? r.x : r.xMax - 6, r.y, 6, r.height), RarityColor(card.Rarity));
                        Text(new Rect(r.x + 14, r.y, r.width - 20, r.height), card.Title, Tiny, Color.white);
                        y += 32;
                    }
                    y += 10;
                }
            }
        }

        private void DrawPick(GameFlow flow)
        {
            var picker = flow.Picker;
            var offer = flow.Offer;
            Fill(new Rect(0, 0, W, H), new Color(0.05f, 0.05f, 0.07f, 0.78f));
            string phase = flow.Phase == MatchPhase.InitialCardPick ? "시작 카드" : "라운드 패배 보상";
            Shadowed(new Rect(0, 150, W, 70), picker.DisplayName + " — " + phase, Big, picker.Color);
            int n = offer.Count;
            float cw = 300, ch = 480, gap = 34;
            float total = n * cw + (n - 1) * gap;
            float x0 = (W - total) / 2;
            for (int i = 0; i < n; i++)
            {
                bool sel = i == flow.OfferIndex;
                float bob = sel ? Mathf.Sin(Time.unscaledTime * 3f) * 4f : 0f;
                DrawCard(new Rect(x0 + i * (cw + gap), 270 - (sel ? 30 : 0) + bob, cw, ch), offer[i], sel, picker.Color);
                // Reroll button under every card (only the player who is picking can press it).
                int left = flow.PickerRerolls;
                bool usable = flow.LocalPicker && left > 0;
                var rb = new Rect(x0 + i * (cw + gap) + 30, 270 + ch + 18, cw - 60, 50);
                Fill(rb, usable ? new Color(1f, 1f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.12f));
                Text(rb, "↻ 새로고침", new GUIStyle(Small) { fontSize = 22, fontStyle = FontStyle.Bold }, usable ? Palette.Ink : new Color(1, 1, 1, 0.35f));
                if (usable && Clicked(rb)) { int k = i; flow.RequestReroll(k); }
            }
            string help;
            if (picker.IsBot) help = "CPU가 고르는 중…";
            else if (flow.Role == NetRole.Client && picker.PlayerId != flow.LocalSlot) help = picker.DisplayName + " 플레이어가 고르는 중…";
            else if (flow.Role == NetRole.Host && picker.Input is NetworkInput) help = picker.DisplayName + " 플레이어가 원격으로 고르는 중…";
            else help = "← / → 로 고르고 사격 키(F · . · 패드 X/RT)로 결정";
            Text(new Rect(0, 850, W, 40), help, Small, new Color(1, 1, 1, 0.8f));
            int remaining = flow.PickerRerolls;
            string rerollText = "남은 새로고침: " + remaining + "회" + (flow.LocalPicker ? "  ·  카드 아래 버튼 또는 R / 패드 Y (선택한 카드)" : "");
            Text(new Rect(0, 900, W, 40), rerollText, new GUIStyle(Mid) { fontSize = 28 }, remaining > 0 ? new Color(1f, 0.85f, 0.4f) : new Color(1, 1, 1, 0.5f));
        }

        /// <summary>Card face: frame, rarity band, procedural emblem, title, description and stat lines.</summary>
        internal static void DrawCard(Rect r, CardDefinition card, bool selected, Color accent)
        {
            Color rc = RarityColor(card.Rarity);
            Fill(new Rect(r.x + 8, r.y + 10, r.width, r.height), new Color(0, 0, 0, 0.35f));
            if (selected)
            {
                float glow = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
                Fill(new Rect(r.x - 14, r.y - 14, r.width + 28, r.height + 28), new Color(accent.r, accent.g, accent.b, 0.18f + 0.12f * glow));
                Fill(new Rect(r.x - 6, r.y - 6, r.width + 12, r.height + 12), accent);
            }
            Fill(r, new Color(0.11f, 0.11f, 0.14f));
            var inner = new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 12);
            Fill(inner, new Color(0.97f, 0.96f, 0.93f));
            // rarity band
            var band = new Rect(inner.x, inner.y, inner.width, 44);
            Fill(band, rc);
            Fill(new Rect(band.x, band.yMax - 6, band.width, 6), Color.Lerp(rc, Color.black, 0.25f));
            Text(new Rect(band.x + 14, band.y, 120, band.height), RarityLabel(card.Rarity), new GUIStyle(Tiny) { fontStyle = FontStyle.Bold, fontSize = 20 }, Palette.Ink);
            bool original = System.Array.IndexOf(card.Categories ?? new string[0], "rounds") >= 0;
            if (original)
            {
                var badge = new Rect(band.xMax - 104, band.y + 9, 92, 26);
                Fill(badge, new Color(0.1f, 0.1f, 0.12f, 0.85f));
                Text(badge, "ROUNDS", new GUIStyle(Tiny) { alignment = TextAnchor.MiddleCenter, fontSize = 15, fontStyle = FontStyle.Bold }, Color.white);
            }
            // emblem art
            var art = new Rect(inner.x + 14, band.yMax + 10, inner.width - 28, 112);
            Fill(art, Color.Lerp(rc, Palette.Ink, 0.62f));
            for (int s = 0; s < 6; s++)
                Fill(new Rect(art.x, art.y + s * 20 + 6, art.width, 2), new Color(1, 1, 1, 0.05f));
            Vector2 c = art.center;
            Disc(c, 48, Color.Lerp(rc, Color.white, 0.25f));
            Disc(c, 40, Color.Lerp(rc, Palette.Ink, 0.35f));
            string glyph = Emblem(card);
            Text(new Rect(c.x - 50, c.y - 32, 100, 64), glyph, new GUIStyle(Mid) { fontSize = glyph.Length > 2 ? 26 : 36 }, Color.white);
            int fx = card.Effects != null ? card.Effects.Length : 0;
            for (int e = 0; e < fx; e++) Disc(new Vector2(art.xMax - 16 - e * 18, art.yMax - 14), 6, new Color(1f, 0.85f, 0.3f));
            // text
            Text(new Rect(inner.x + 8, art.yMax + 6, inner.width - 16, 44), card.Title, CardTitle, Palette.Ink);
            Text(new Rect(inner.x + 8, art.yMax + 46, inner.width - 16, 22), card.Subtitle, new GUIStyle(Tiny) { alignment = TextAnchor.MiddleCenter, fontSize = 16 }, new Color(0, 0, 0, 0.45f));
            Text(new Rect(inner.x + 16, art.yMax + 70, inner.width - 32, 70), card.Description, CardBody, new Color(0.2f, 0.2f, 0.22f));
            float ly = art.yMax + 146;
            int max = Mathf.Min(card.Lines.Length, Mathf.FloorToInt((inner.yMax - ly - 6) / 32f));
            for (int l = 0; l < max; l++)
            {
                var line = card.Lines[l];
                var lr = new Rect(inner.x + 14, ly + l * 32, inner.width - 28, 28);
                Fill(lr, line.Positive ? new Color(0.25f, 0.7f, 0.35f, 0.16f) : new Color(0.85f, 0.25f, 0.2f, 0.14f));
                Fill(new Rect(lr.x, lr.y, 4, lr.height), line.Positive ? new Color(0.2f, 0.65f, 0.3f) : new Color(0.8f, 0.25f, 0.2f));
                var col = line.Positive ? new Color(0.12f, 0.5f, 0.22f) : new Color(0.72f, 0.18f, 0.14f);
                Text(new Rect(lr.x + 12, lr.y, lr.width * 0.62f, lr.height), line.Label, Tiny, col);
                Text(new Rect(lr.x, lr.y, lr.width - 10, lr.height), line.Amount, TinyRight, col);
            }
        }

        /// <summary>Two-letter emblem from the English name (EXPLOSIVE BULLET → EB).</summary>
        private static string Emblem(CardDefinition card)
        {
            string s = string.IsNullOrWhiteSpace(card.Subtitle) ? card.Title : card.Subtitle;
            var words = s.Split(new[] { ' ', '-', '_' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return "?";
            if (words.Length == 1) return words[0].Substring(0, Mathf.Min(2, words[0].Length)).ToUpperInvariant();
            return (words[0].Substring(0, 1) + words[1].Substring(0, 1)).ToUpperInvariant();
        }

        /// <summary>Win/lose screen. Leaving it (Enter/Space or the button) returns to the main menu — no rematch.</summary>
        private void DrawResult(GameFlow flow)
        {
            int win = flow.WinningTeam;
            int me = flow.LocalTeam;
            if (!_resultSound && Event.current.type == EventType.Repaint)
            {
                _resultSound = true;
                Audio.Sfx.Play(me < 0 || me == win ? "victory" : "defeat");
            }
            string head = me < 0 ? flow.SideName(win) + " 승리" : me == win ? "승리!" : "패배";
            Color hc = me < 0 ? flow.SideColor(win) : me == win ? new Color(1f, 0.82f, 0.25f) : new Color(0.7f, 0.74f, 0.82f);
            Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.55f));
            var panel = new Rect(W / 2 - 540, 190, 1080, 640);
            Fill(new Rect(panel.x + 10, panel.y + 12, panel.width, panel.height), new Color(0, 0, 0, 0.4f));
            Fill(panel, new Color(0.08f, 0.08f, 0.11f, 0.96f));
            Fill(new Rect(panel.x, panel.y, panel.width, 10), hc);
            float pulse = 1f + 0.03f * Mathf.Sin(Time.unscaledTime * 4f);
            var titleStyle = new GUIStyle(Title) { fontSize = Mathf.RoundToInt(Title.fontSize * pulse) };
            Shadowed(new Rect(panel.x, panel.y + 30, panel.width, 120), head, titleStyle, hc);
            Text(new Rect(panel.x, panel.y + 150, panel.width, 40), flow.SideName(win) + " 승리 · 라운드 " + flow.TeamRounds(0) + " : " + flow.TeamRounds(1), Mid, Color.white);
            for (int t = 0; t < 2; t++)
            {
                float x = panel.x + 60 + t * 500;
                var head2 = new Rect(x, panel.y + 220, 460, 46);
                Fill(head2, new Color(flow.SideColor(t).r, flow.SideColor(t).g, flow.SideColor(t).b, t == win ? 0.85f : 0.35f));
                Text(head2, (t == win ? "★ " : "") + flow.SideName(t) + "  " + flow.TeamRounds(t) + "R", Mid, Color.white);
                int row = 0;
                foreach (var f in flow.Fighters)
                {
                    if (f.TeamId != t) continue;
                    var rr = new Rect(x, panel.y + 276 + row * 52, 460, 46);
                    Fill(rr, new Color(1, 1, 1, 0.07f));
                    Disc(new Vector2(rr.x + 26, rr.center.y), 13, f.Color);
                    Text(new Rect(rr.x + 50, rr.y, 260, rr.height), f.DisplayName + (f.Absent ? " (나감)" : ""), new GUIStyle(Small) { alignment = TextAnchor.MiddleLeft }, f.Absent ? new Color(1, 1, 1, 0.45f) : Color.white);
                    Text(new Rect(rr.x, rr.y, rr.width - 14, rr.height), "카드 " + f.Cards.Count + "장", new GUIStyle(Tiny) { alignment = TextAnchor.MiddleRight }, new Color(1, 1, 1, 0.65f));
                    row++;
                }
            }
            if (!string.IsNullOrEmpty(flow.ForfeitText))
                Text(new Rect(panel.x, panel.yMax - 150, panel.width, 34), flow.ForfeitText.Split('·')[0].Trim(), Small, new Color(1f, 0.85f, 0.5f));
            if (flow.ReplayMode || (flow.Role == NetRole.Host && flow.DedicatedServer)) return;
            var btn = new Rect(W / 2 - 220, panel.yMax - 100, 440, 70);
            Fill(btn, hc);
            Text(btn, "메인 화면으로 (Enter)", Mid, Palette.Ink);
            if (Clicked(btn)) flow.ExitResults();
        }

        private void DrawBanner(GameFlow flow)
        {
            if (string.IsNullOrEmpty(flow.Banner) || flow.IsPicking) return;
            Fill(new Rect(0, H / 2 - 80, W, 160), new Color(0, 0, 0, 0.35f));
            Shadowed(new Rect(0, H / 2 - 70, W, 100), flow.Banner, Big, flow.BannerColor);
            if (!string.IsNullOrEmpty(flow.SubBanner))
                Text(new Rect(0, H / 2 + 22, W, 40), flow.SubBanner, Small, Color.white);
        }

        private static Color RarityColor(CardRarity r) => r == CardRarity.Rare ? Palette.Rare : r == CardRarity.Uncommon ? Palette.Uncommon : Palette.Common;
        private static string RarityLabel(CardRarity r) => r == CardRarity.Rare ? "희귀" : r == CardRarity.Uncommon ? "고급" : "일반";
    }
}

