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
        private void OnGUI()
        {
            var flow = GameFlow.Instance;
            if (flow == null || flow.InMenu || !flow.HasMatch) return;
            Begin();
            DrawOverlays(flow);
            if (!flow.IsSandbox) DrawScore(flow); // the tutorial draws its own panel in that area
            DrawCardBars(flow);
            if (flow.IsPicking) DrawPick(flow);
            DrawBanner(flow);
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
            float cw = 300, ch = 430, gap = 34;
            float total = n * cw + (n - 1) * gap;
            float x0 = (W - total) / 2;
            for (int i = 0; i < n; i++)
            {
                var card = offer[i];
                bool sel = i == flow.OfferIndex;
                var r = new Rect(x0 + i * (cw + gap), 300 - (sel ? 28 : 0), cw, ch);
                if (sel) Fill(new Rect(r.x - 6, r.y - 6, r.width + 12, r.height + 12), picker.Color);
                Fill(r, new Color(0.97f, 0.96f, 0.93f));
                Color rc = RarityColor(card.Rarity);
                Fill(new Rect(r.x, r.y, r.width, 14), rc);
                Text(new Rect(r.x, r.y + 22, r.width, 26), RarityLabel(card.Rarity), Small, Color.Lerp(rc, Palette.Ink, 0.4f));
                Text(new Rect(r.x + 10, r.y + 58, r.width - 20, 48), card.Title, CardTitle, Palette.Ink);
                Text(new Rect(r.x + 10, r.y + 104, r.width - 20, 26), card.Subtitle, Small, new Color(0, 0, 0, 0.45f));
                Text(new Rect(r.x + 22, r.y + 146, r.width - 44, 90), card.Description, CardBody, new Color(0.2f, 0.2f, 0.22f));
                for (int l = 0; l < card.Lines.Length; l++)
                {
                    var line = card.Lines[l];
                    var lr = new Rect(r.x + 22, r.y + 250 + l * 40, r.width - 44, 34);
                    Fill(lr, line.Positive ? new Color(0.25f, 0.7f, 0.35f, 0.16f) : new Color(0.85f, 0.25f, 0.2f, 0.14f));
                    var col = line.Positive ? new Color(0.12f, 0.5f, 0.22f) : new Color(0.72f, 0.18f, 0.14f);
                    Text(new Rect(lr.x + 10, lr.y, lr.width * 0.6f, lr.height), line.Label, Tiny, col);
                    Text(new Rect(lr.x, lr.y, lr.width - 10, lr.height), line.Amount, TinyRight, col);
                }
            }
            string help;
            if (picker.IsBot) help = "CPU가 고르는 중…";
            else if (flow.Role == NetRole.Client && picker.PlayerId != flow.LocalSlot) help = picker.DisplayName + " 플레이어가 고르는 중…";
            else if (flow.Role == NetRole.Host && picker.Input is NetworkInput) help = picker.DisplayName + " 플레이어가 원격으로 고르는 중…";
            else help = "← / → 로 고르고 사격 키(F · . · 패드 X/RT)로 결정";
            Text(new Rect(0, 800, W, 40), help, Small, new Color(1, 1, 1, 0.8f));
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

