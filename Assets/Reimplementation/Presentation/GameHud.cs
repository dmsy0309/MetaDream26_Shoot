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
            if (flow == null || flow.InMenu || !flow.HasMatch) { ResetPickAnimation(); return; }
            Begin();
            DrawOverlays(flow);
            if (!flow.IsSandbox) DrawScore(flow); // the tutorial draws its own panel in that area
            DrawMapHud(flow);
            DrawCardBars(flow);
            UpdatePickAnimation(flow);
            if (flow.IsPicking) DrawPick(flow);
            DrawPickExit();
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
                    Disc(new Vector2(x, 82), 14, new Color(1, 1, 1, 0.35f));
                    Disc(new Vector2(x, 82), 11, won ? c : new Color(0.12f, 0.13f, 0.17f, 0.95f));
                }
                for (int p = 0; p < points; p++)
                {
                    float x = cx + (p - (points - 1) * 0.5f) * 22;
                    bool got = flow.TeamPoints(t) > p;
                    Disc(new Vector2(x, 112), 7, got ? c : new Color(1, 1, 1, 0.22f));
                }
            }
        }

        /// <summary>Map script "hud(…)" line, centred under the score.</summary>
        private static void DrawMapHud(GameFlow flow)
        {
            string hud = flow.MapHud;
            if (string.IsNullOrEmpty(hud) || flow.IsPicking) return;
            var r = new Rect(W / 2 - 420, flow.IsSandbox ? 18 : 128, 840, 38);
            Fill(r, new Color(0f, 0f, 0f, 0.45f));
            Shadowed(r, hud, new GUIStyle(Small) { alignment = TextAnchor.MiddleCenter }, new Color(1f, 0.88f, 0.45f));
        }

        private void DrawOverlays(GameFlow flow)
        {
            var cam = Camera.main;
            if (cam == null) return;
            bool names = flow.Fighters.Count > 2 || flow.Role != NetRole.Offline;
            foreach (var f in flow.Fighters)
            {
                if (f.IsDead || !f.IsVisible) continue;
                // 1.6 units above the body edge: leaves room for tall head decorations (CharacterLook hats).
                Vector2 top = WorldToGui(cam, f.Position + Vector2.up * (f.Radius + 1.6f));
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
                    Disc(new Vector2(x, top.y + 16), pip * 0.36f, has ? new Color(0.95f, 0.95f, 0.98f, 0.95f) : new Color(1, 1, 1, 0.18f));
                }
                if (f.Ammo.isReloading)
                {
                    Fill(new Rect(top.x - w / 2, top.y + 26, w, 5), new Color(1, 1, 1, 0.18f));
                    Fill(new Rect(top.x - w / 2, top.y + 26, w * f.ReloadProgress, 5), f.Color);
                }
                float cd = f.BlockReadyProgress;
                if (cd < 1f)
                {
                    Fill(new Rect(top.x - w / 2, top.y - 18, w, 4), new Color(1, 1, 1, 0.18f));
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

        // ── Card pick presentation, modelled on ROUNDS CardChoice / CardChoiceVisuals (REVERSE_DEVELOPMENT_REPORT §10.13):
        // cards are dealt one by one from the picker's ball, the selected card grows (1.15) while the others shrink (0.9)
        // and dim, two "hands" follow the selection on a spring with Perlin sway, and after the pick the chosen card flies
        // to the player while the rest are knocked away.
        private const float CardW = 300f, CardH = 480f, CardGap = 34f, CardCenterY = 520f;
        private const float DealDelay = 0.15f, DealInterval = 0.1f, DealTime = 0.28f;
        private const float SelScale = 1.15f, IdleScale = 0.9f, IdleDim = 0.55f;
        private const float HandSpring = 120f, HandDrag = 11f, FlipTime = 0.3f, ExitTime = 0.9f, ExitGravity = 2400f;
        private static readonly Vector2 PickerBall = new Vector2(960f, 1010f);

        private struct ExitCard { public CardDefinition Card; public Vector2 Pos, Vel; public float Angle, Spin, Scale; public bool Chosen; }

        private Fighter _pickFighter;
        private MatchPhase _pickPhase;
        private bool _wasPicking;
        private float _sessionStart, _lastAnimTime;
        private int _lastSel = -1, _dealtSounds;
        private CardDefinition[] _shown = new CardDefinition[0];
        private CardDefinition[] _flipFrom = new CardDefinition[0];
        private float[] _flipAt = new float[0], _scale = new float[0], _dim = new float[0], _spin0 = new float[0];
        private Vector2 _hand, _handVel, _rest, _restVel;
        private Color _pickColor = Color.white;
        private readonly System.Collections.Generic.List<ExitCard> _exit = new System.Collections.Generic.List<ExitCard>();
        private float _exitStart = -10f;
        private Vector2 _exitTarget;

        private static float Fit(int n) => Mathf.Min(1f, (W - 80f) / Mathf.Max(1f, n * CardW + (n - 1) * CardGap));

        private static Vector2 Slot(int i, int n)
        {
            float fit = Fit(n);
            float total = (n * CardW + (n - 1) * CardGap) * fit;
            return new Vector2((W - total) / 2f + i * (CardW + CardGap) * fit + CardW * fit / 2f, CardCenterY);
        }

        private float DealStart(int i) => _sessionStart + DealDelay + i * DealInterval;

        private static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float a = x - 1f;
            return 1f + c3 * a * a * a + c1 * a * a;
        }

        private static void Spring(ref Vector2 p, ref Vector2 v, Vector2 target, float k, float drag, float dt)
        {
            v += (target - p) * k * dt;
            v -= v * Mathf.Min(1f, drag * dt);
            p += v * dt;
        }

        private void ResetPickAnimation()
        {
            _wasPicking = false;
            _pickFighter = null;
            _exit.Clear();
        }

        /// <summary>Advances the pick animation once per frame (Repaint only).</summary>
        private void UpdatePickAnimation(GameFlow flow)
        {
            if (Event.current.type != EventType.Repaint) return;
            float now = Time.unscaledTime;
            float dt = Mathf.Clamp(now - _lastAnimTime, 0f, 0.1f); // ROUNDS skips frames with unscaledDeltaTime > 0.1
            _lastAnimTime = now;
            bool picking = flow.IsPicking;
            bool newSession = picking && (!_wasPicking || flow.Picker != _pickFighter || flow.Phase != _pickPhase);
            if (_wasPicking && (!picking || newSession)) StartExit();
            if (newSession || (picking && _shown.Length != flow.Offer.Count)) StartSession(flow, now);
            _wasPicking = picking;
            if (!picking) return;

            var offer = flow.Offer;
            int n = offer.Count;
            for (int i = 0; i < n; i++)
            {
                if (_shown[i] == offer[i]) continue; // reroll: flip the card over to the new face
                _flipFrom[i] = _shown[i];
                _flipAt[i] = now;
                _shown[i] = offer[i];
            }
            while (_dealtSounds < n && now >= DealStart(_dealtSounds)) { Audio.Sfx.Play("card_deal"); _dealtSounds++; }
            int sel = Mathf.Clamp(flow.OfferIndex, 0, n - 1);
            if (sel != _lastSel) { Audio.Sfx.Play("card_switch"); _lastSel = sel; }

            float k = 1f - Mathf.Exp(-14f * dt);
            for (int i = 0; i < n; i++)
            {
                _scale[i] = Mathf.Lerp(_scale[i], i == sel ? SelScale : IdleScale, k);
                _dim[i] = Mathf.Lerp(_dim[i], i == sel ? 0f : IdleDim, k);
            }
            float fit = Fit(n);
            var sway = new Vector2(Mathf.PerlinNoise(now * 0.9f, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, now * 0.9f) - 0.5f) * 28f;
            Vector2 slot = Slot(sel, n);
            Vector2 target = slot + new Vector2(CardW * 0.5f * fit * SelScale - 22f, CardH * 0.5f * fit * SelScale - 50f) + sway;
            Spring(ref _hand, ref _handVel, target, HandSpring, HandDrag, dt);
            Spring(ref _rest, ref _restVel, PickerBall + new Vector2(-120f, -30f) + sway * 0.5f, HandSpring * 0.5f, HandDrag, dt);
        }

        private void StartSession(GameFlow flow, float now)
        {
            _pickFighter = flow.Picker;
            _pickPhase = flow.Phase;
            _pickColor = flow.Picker.Color;
            _sessionStart = now;
            int n = flow.Offer.Count;
            _shown = new CardDefinition[n];
            _flipFrom = new CardDefinition[n];
            _flipAt = new float[n];
            _scale = new float[n];
            _dim = new float[n];
            _spin0 = new float[n];
            for (int i = 0; i < n; i++)
            {
                _shown[i] = flow.Offer[i];
                _flipAt[i] = -10f;
                _scale[i] = IdleScale;
                _dim[i] = IdleDim;
                _spin0[i] = UnityEngine.Random.Range(-28f, 28f);
            }
            _dealtSounds = 0;
            _lastSel = Mathf.Clamp(flow.OfferIndex, 0, Mathf.Max(0, n - 1));
            _hand = _rest = PickerBall;
            _handVel = _restVel = Vector2.zero;
        }

        /// <summary>The pick ended: the chosen card flies to the picker, the others get a random push and spin and fall away.</summary>
        private void StartExit()
        {
            _exit.Clear();
            int n = _shown.Length;
            float now = Time.unscaledTime;
            float fit = Fit(n);
            for (int i = 0; i < n; i++)
            {
                if (_shown[i] == null || now < DealStart(i)) continue;
                Vector2 pos = Slot(i, n);
                bool chosen = i == _lastSel;
                float side = Mathf.Sign(pos.x - W / 2f + 0.01f);
                _exit.Add(new ExitCard
                {
                    Card = _shown[i],
                    Pos = pos,
                    Scale = (i < _scale.Length ? _scale[i] : IdleScale) * fit,
                    Chosen = chosen,
                    Vel = new Vector2(side * UnityEngine.Random.Range(150f, 700f), UnityEngine.Random.Range(-900f, -350f)),
                    Spin = UnityEngine.Random.Range(-420f, 420f),
                });
            }
            _exitStart = now;
            _exitTarget = PickerBall;
            var cam = Camera.main;
            if (cam != null && _pickFighter != null && _pickFighter.IsVisible) _exitTarget = WorldToGui(cam, _pickFighter.Position);
            _pickFighter = null;
        }

        /// <summary>Draws a card rotated/scaled around its centre. sx &lt; 1 squashes it horizontally (reroll flip).</summary>
        private static void DrawCardAt(CardDefinition card, Vector2 center, float scale, float sx, float angle, bool selected, Color accent, float dim)
        {
            var baseM = GUI.matrix;
            GUI.matrix = baseM * Matrix4x4.TRS(center, Quaternion.Euler(0f, 0f, angle), new Vector3(scale * sx, scale, 1f))
                               * Matrix4x4.Translate(new Vector3(-CardW / 2f, -CardH / 2f, 0f));
            var r = new Rect(0f, 0f, CardW, CardH);
            DrawCard(r, card, selected, accent);
            if (dim > 0.01f) Fill(r, new Color(0.02f, 0.02f, 0.04f, dim));
            GUI.matrix = baseM;
        }

        private void DrawPickExit()
        {
            if (_exit.Count == 0) return;
            float t = Time.unscaledTime - _exitStart;
            if (t > ExitTime) { _exit.Clear(); return; }
            foreach (var c in _exit)
            {
                if (c.Chosen) continue;
                Vector2 p = c.Pos + c.Vel * t + new Vector2(0f, 0.5f * ExitGravity * t * t);
                DrawCardAt(c.Card, p, c.Scale, 1f, c.Angle + c.Spin * t, false, _pickColor, IdleDim + 0.25f * t);
            }
            foreach (var c in _exit)
            {
                if (!c.Chosen) continue;
                float u = Mathf.Clamp01((t - 0.1f) / 0.35f);
                float e = Mathf.SmoothStep(0f, 1f, u); // ROUNDS uses an EaseInOut AnimationCurve here
                if (u < 1f) DrawCardAt(c.Card, Vector2.LerpUnclamped(c.Pos, _exitTarget, e), Mathf.Lerp(c.Scale * SelScale, 0.1f, e), 1f, 0f, true, _pickColor, 0f);
                else
                {
                    float a = Mathf.Clamp01((t - 0.45f) / (ExitTime - 0.45f));
                    Disc(_exitTarget, 24f + 90f * a, new Color(_pickColor.r, _pickColor.g, _pickColor.b, 0.55f * (1f - a)));
                }
            }
        }

        public const float UrgentSeconds = 5f, BlinkPeriod = 0.5f;
        private int _lastTickSecond = -1;

        /// <summary>True during the "on" (red) half of the 0.5 s blink cycle in the last 5 seconds.</summary>
        public static bool TimerBlinkOn(float left) => left > 0f && left <= UrgentSeconds && Mathf.Repeat(left, BlinkPeriod) >= BlinkPeriod * 0.5f;

        /// <summary>Card pick time limit at the top of the card screen; the last 5 seconds blink red (0.5 s cycle).</summary>
        private void DrawPickTimer(GameFlow flow)
        {
            float left = flow.PickTimeLeft;
            int limit = flow.PickTimeLimit;
            if (left < 0f || limit <= 0) { _lastTickSecond = -1; return; }
            int shown = Mathf.CeilToInt(left);
            bool urgent = left <= UrgentSeconds;
            bool on = TimerBlinkOn(left);
            if (urgent && Event.current.type == EventType.Repaint && shown != _lastTickSecond && shown > 0)
            {
                _lastTickSecond = shown;
                Audio.Sfx.Play("timer_tick");
            }
            var red = new Color(1f, 0.22f, 0.18f);
            if (urgent)
            {
                // red screen edge pulsing with the number
                float a = on ? 0.45f : 0.15f;
                var edge = new Color(red.r, red.g, red.b, a);
                Fill(new Rect(0, 0, W, 10), edge);
                Fill(new Rect(0, H - 10, W, 10), edge);
                Fill(new Rect(0, 0, 10, H), edge);
                Fill(new Rect(W - 10, 0, 10, H), edge);
            }
            var panel = new Rect(W / 2 - 150, 20, 300, 100);
            Fill(new Rect(panel.x - 3, panel.y - 3, panel.width + 6, panel.height + 6), urgent && on ? red : new Color(1f, 1f, 1f, 0.35f));
            Fill(panel, new Color(0.04f, 0.045f, 0.06f, 0.96f));
            Text(new Rect(panel.x, panel.y + 4, panel.width, 26), "남은 시간", new GUIStyle(Tiny) { alignment = TextAnchor.MiddleCenter, fontSize = 18 }, new Color(1, 1, 1, 0.65f));
            int size = urgent ? (on ? 60 : 52) : 50;
            Color numberColor = urgent ? (on ? red : new Color(1f, 0.92f, 0.9f)) : Color.white;
            Shadowed(new Rect(panel.x, panel.y + 26, panel.width, 60), shown + "초", new GUIStyle(Big) { fontSize = size, alignment = TextAnchor.MiddleCenter }, numberColor);
            float k = Mathf.Clamp01(left / limit);
            var bar = new Rect(panel.x + 16, panel.yMax - 12, panel.width - 32, 6);
            Fill(bar, new Color(1, 1, 1, 0.12f));
            Fill(new Rect(bar.x, bar.y, bar.width * k, bar.height), urgent ? red : new Color(1f, 0.85f, 0.4f));
        }

        private static void DrawPickerBall(Color c)
        {
            float pulse = 1f + 0.04f * Mathf.Sin(Time.unscaledTime * 4f);
            Disc(PickerBall, 50f * pulse, new Color(c.r, c.g, c.b, 0.25f));
            Disc(PickerBall, 40f, Color.Lerp(c, Color.white, 0.6f));
            Disc(PickerBall, 35f, c);
            Disc(PickerBall + new Vector2(-11f, -11f), 9f, new Color(1f, 1f, 1f, 0.35f));
        }

        private static void DrawHand(Vector2 p, Color c, float alpha)
        {
            Disc(p + new Vector2(4f, 6f), 30f, new Color(0f, 0f, 0f, 0.35f * alpha));
            Disc(p, 30f, new Color(0.95f, 0.96f, 1f, alpha));
            Disc(p, 25f, new Color(c.r, c.g, c.b, alpha));
            Disc(p + new Vector2(-8f, -8f), 7f, new Color(1f, 1f, 1f, 0.4f * alpha));
        }

        private void DrawPick(GameFlow flow)
        {
            var picker = flow.Picker;
            var offer = flow.Offer;
            int n = Mathf.Min(offer.Count, _shown.Length);
            float now = Time.unscaledTime;
            float fit = Fit(n);
            Fill(new Rect(0, 0, W, H), new Color(0.03f, 0.035f, 0.05f, 0.82f));
            string phase = flow.Phase == MatchPhase.InitialCardPick ? "시작 카드" : "라운드 패배 보상";
            Shadowed(new Rect(0, 130, W, 70), picker.DisplayName + " — " + phase, Big, picker.Color);
            DrawPickTimer(flow);
            DrawPickerBall(picker.Color);
            int sel = Mathf.Clamp(flow.OfferIndex, 0, Mathf.Max(0, n - 1));
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < n; i++)
                {
                    if ((i == sel) != (pass == 1)) continue; // the selected card is drawn on top
                    float d = (now - DealStart(i)) / DealTime;
                    if (d <= 0f) continue;
                    d = Mathf.Clamp01(d);
                    float e = EaseOutBack(d);
                    Vector2 pos = Vector2.LerpUnclamped(PickerBall, Slot(i, n), e);
                    float scale = Mathf.LerpUnclamped(0.15f, _scale[i], e) * fit;
                    float angle = _spin0[i] * (1f - d);
                    if (i == sel)
                    {
                        pos.y += Mathf.Sin(now * 3f) * 4f - 10f;
                        angle += (Mathf.PerlinNoise(now * 1.3f, 0.5f) - 0.5f) * 3f; // ROUNDS "shake" on the selected card
                    }
                    float sx = 1f;
                    var face = _shown[i];
                    float ft = (now - _flipAt[i]) / FlipTime;
                    if (ft >= 0f && ft < 1f)
                    {
                        sx = Mathf.Max(0.02f, Mathf.Abs(Mathf.Cos(ft * Mathf.PI)));
                        if (ft < 0.5f && _flipFrom[i] != null) face = _flipFrom[i];
                    }
                    DrawCardAt(face, pos, scale, sx, angle, i == sel, picker.Color, _dim[i]);
                }
            }
            // Reroll button under every card (only the player who is picking can press it).
            int left = flow.PickerRerolls;
            bool usable = flow.LocalPicker && left > 0;
            for (int i = 0; i < n; i++)
            {
                if (now < DealStart(i) + DealTime) continue;
                Vector2 s = Slot(i, n);
                var rb = new Rect(s.x - CardW * fit / 2f + 30f, CardCenterY + CardH * fit / 2f + 44f, CardW * fit - 60f, 50f);
                Fill(rb, usable ? new Color(1f, 1f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.12f));
                Text(rb, "↻ 새로고침", new GUIStyle(Small) { fontSize = 22, fontStyle = FontStyle.Bold }, usable ? Palette.Ink : new Color(1, 1, 1, 0.35f));
                if (usable && Clicked(rb)) { int k = i; flow.RequestReroll(k); }
            }
            if (n > 0 && now >= DealStart(0)) DrawHand(_rest, picker.Color, 0.75f);
            if (n > 0 && now >= DealStart(n - 1) + DealTime * 0.5f) DrawHand(_hand, picker.Color, 1f);
            string help;
            if (picker.IsBot) help = "CPU가 고르는 중…";
            else if (flow.Role == NetRole.Client && picker.PlayerId != flow.LocalSlot) help = picker.DisplayName + " 플레이어가 고르는 중…";
            else if (flow.Role == NetRole.Host && picker.Input is NetworkInput) help = picker.DisplayName + " 플레이어가 원격으로 고르는 중…";
            else help = "← / → 로 고르고 사격 키(F · . · 패드 X/RT)로 결정";
            Text(new Rect(0, 868, W, 36), help, Small, new Color(1, 1, 1, 0.8f));
            string rerollText = "남은 새로고침: " + left + "회" + (flow.LocalPicker ? "  ·  카드 아래 버튼 또는 R / 패드 Y (선택한 카드)" : "");
            Text(new Rect(0, 906, W, 40), rerollText, new GUIStyle(Mid) { fontSize = 28 }, left > 0 ? new Color(1f, 0.85f, 0.4f) : new Color(1, 1, 1, 0.5f));
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
            const float border = CardArt.Border;
            var inner = new Rect(r.x + border, r.y + border, r.width - 2f * border, r.height - 2f * border);
            Fill(inner, new Color(0.97f, 0.96f, 0.93f));
            // rarity band
            var band = new Rect(inner.x, inner.y, inner.width, CardArt.BandHeight);
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
            // art area (CardArt: 260×112 rectangle, emblem circle 96 Ø in its centre). Docs/CARD_ART.md
            var art = new Rect(inner.x + CardArt.ArtInsetX, band.yMax + CardArt.ArtGapY, inner.width - 2f * CardArt.ArtInsetX, CardArt.ArtHeight);
            var image = CardArt.Get(card);
            bool icon = CardArt.IsIcon(card);
            Vector2 c = art.center;
            if (image != null && !icon)
            {
                // Fill mode: the image covers the whole art rectangle (centre-cropped to 260:112).
                var oldColor = GUI.color;
                GUI.color = Color.white;
                GUI.DrawTexture(art, image, ScaleMode.ScaleAndCrop, true);
                GUI.color = oldColor;
                Fill(new Rect(art.x, art.yMax - 3, art.width, 3), Color.Lerp(rc, Palette.Ink, 0.3f));
            }
            else
            {
                Fill(art, Color.Lerp(rc, Palette.Ink, 0.62f));
                for (int s = 0; s < 6; s++)
                    Fill(new Rect(art.x, art.y + s * 20 + 6, art.width, 2), new Color(1, 1, 1, 0.05f));
                Disc(c, CardArt.EmblemOuterRadius, Color.Lerp(rc, Color.white, 0.25f));
                Disc(c, CardArt.EmblemInnerRadius, Color.Lerp(rc, Palette.Ink, 0.35f));
                if (image != null)
                {
                    // Icon mode: a (transparent) image fitted into the 80×80 square inside the circle.
                    var oldColor = GUI.color;
                    GUI.color = Color.white;
                    GUI.DrawTexture(new Rect(c.x - CardArt.IconSize / 2f, c.y - CardArt.IconSize / 2f, CardArt.IconSize, CardArt.IconSize), image, ScaleMode.ScaleToFit, true);
                    GUI.color = oldColor;
                }
                else
                {
                    string glyph = Emblem(card);
                    Text(new Rect(c.x - 50, c.y - 32, 100, 64), glyph, new GUIStyle(Mid) { fontSize = glyph.Length > 2 ? 26 : 36 }, Color.white);
                }
            }
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

