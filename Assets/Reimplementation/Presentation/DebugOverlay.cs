using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using Metadream.Shoot.Cards;
using Metadream.Shoot.Combat;
using Metadream.Shoot.Content;
using Metadream.Shoot.Online;
using Metadream.Shoot.Player;
using static Metadream.Shoot.Presentation.UiKit;

namespace Metadream.Shoot.Presentation
{
    /// <summary>
    /// Developer debug mode (Settings → 개발자 디버그 모드, or -debug). Draws content errors, world gizmos,
    /// fighter/ability state and recent errors, and offers offline commands:
    /// F1 panel · F2 next map · F3 give next card · F4 kill enemies · F5 reload content (title) · F6 god mode · F7 slow motion.
    /// </summary>
    public sealed class DebugOverlay : MonoBehaviour
    {
        /// <summary>Hides the overlay without leaving debug mode (replay "디버그 UI" toggle).</summary>
        public static bool Suppressed;
        private readonly Queue<string> _log = new Queue<string>();
        private bool _panel = true;
        private int _cardCursor;
        private string _toast;
        private float _toastUntil;
        private float _fps;
        private bool _slow;

        private void OnEnable() => Application.logMessageReceived += OnLog;
        private void OnDisable() => Application.logMessageReceived -= OnLog;

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Log) return;
            string line = (type == LogType.Warning ? "W " : "E ") + message.Split('\n')[0];
            _log.Enqueue(line.Length > 160 ? line.Substring(0, 160) + "…" : line);
            while (_log.Count > 8) _log.Dequeue();
        }

        private void Toast(string text)
        {
            _toast = text;
            _toastUntil = Time.unscaledTime + 2.5f;
            Debug.Log("[Debug] " + text);
        }

        private void Update()
        {
            _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.05f);
            if (!DevSettings.DebugMode) return;
            var kb = Keyboard.current;
            var flow = GameFlow.Instance;
            if (kb == null || flow == null) return;
            if (kb.f1Key.wasPressedThisFrame) _panel = !_panel;
            if (kb.f5Key.wasPressedThisFrame)
            {
                if (flow.InMenu && NetHostSession.Active == null && NetClientSession.Active == null)
                {
                    ContentDatabase.Reload();
                    Toast("콘텐츠 다시 불러옴: 카드 " + ContentDatabase.Cards.Count + ", 맵 " + ContentDatabase.Maps.Count + ", 오류 " + ContentDatabase.ErrorCount);
                }
                else Toast("F5 콘텐츠 리로드는 타이틀 화면에서만 가능합니다");
            }
            if (!flow.CanUseDebugCommands)
            {
                if (kb.f2Key.wasPressedThisFrame || kb.f3Key.wasPressedThisFrame || kb.f4Key.wasPressedThisFrame || kb.f6Key.wasPressedThisFrame || kb.f7Key.wasPressedThisFrame)
                    Toast("디버그 명령은 오프라인 게임(튜토리얼/CPU 대전)에서만 동작합니다");
                return;
            }
            if (kb.f2Key.wasPressedThisFrame) { flow.DebugNextMap(); Toast("맵: " + flow.CurrentMap.Definition.Id); }
            if (kb.f3Key.wasPressedThisFrame && CardCatalog.All.Count > 0)
            {
                var card = CardCatalog.All[_cardCursor++ % CardCatalog.All.Count];
                flow.DebugGiveCard(card);
                Toast("카드 지급: " + card.Title + " (" + card.Id + ")");
            }
            if (kb.f4Key.wasPressedThisFrame) { flow.DebugKillEnemies(); Toast("적 처치"); }
            if (kb.f6Key.wasPressedThisFrame) Toast("무적 " + (flow.DebugToggleGodMode() ? "켜짐" : "꺼짐"));
            if (kb.f7Key.wasPressedThisFrame)
            {
                _slow = !_slow;
                Time.timeScale = _slow ? 0.25f : 1f;
                Toast("슬로모션 " + (_slow ? "켜짐 (x0.25)" : "꺼짐"));
            }
            if (_slow && Time.timeScale > 0.26f && flow.Phase == Core.MatchPhase.Battle) Time.timeScale = 0.25f;
        }

        private void OnGUI()
        {
            if (!DevSettings.DebugMode || Suppressed) return;
            var flow = GameFlow.Instance;
            if (flow == null) return;
            GUI.depth = -100;
            Begin();
            if (!flow.InMenu && flow.HasMatch) DrawGizmos(flow);
            // Menus already show a content summary (title/settings); the panel would cover their headings.
            if (_panel && !flow.InMenu) DrawPanel(flow);
            if (_toast != null && Time.unscaledTime < _toastUntil)
            {
                Fill(new Rect(W / 2 - 400, H - 130, 800, 44), new Color(0, 0, 0, 0.75f));
                Text(new Rect(W / 2 - 400, H - 130, 800, 44), _toast, Small, new Color(1f, 0.9f, 0.4f));
            }
        }

        // ---------------------------------------------------------------- panel

        private void DrawPanel(GameFlow flow)
        {
            var sb = new StringBuilder();
            sb.Append("DEBUG  ").Append(Mathf.RoundToInt(_fps)).Append(" fps  ·  ").Append(flow.Role);
            if (flow.HasMatch) sb.Append("  ·  ").Append(flow.Phase).Append(flow.IsSandbox ? " (sandbox)" : "");
            if (flow.CurrentMap != null) sb.Append("  ·  map ").Append(flow.CurrentMap.Definition.Id);
            sb.Append('\n');
            sb.Append("content ").Append(ContentDatabase.Cards.Count).Append(" cards / ").Append(ContentDatabase.Maps.Count).Append(" maps  hash ").Append(ContentDatabase.Hash)
              .Append("  errors ").Append(ContentDatabase.ErrorCount).Append("  warnings ").Append(ContentDatabase.WarningCount);
            if (ContentDatabase.LoadedExternalDirectory != null) sb.Append("\nexternal ").Append(ContentDatabase.LoadedExternalDirectory);
            if (NetHostSession.Active != null) sb.Append("\nhost ").Append(NetHostSession.Active.StatusLine());
            if (NetClientSession.Active != null) sb.Append("\nclient slot ").Append(NetClientSession.Active.MySlot).Append(" snapshots ").Append(NetClientSession.Active.SnapshotsReceived).Append(" inputs ").Append(NetClientSession.Active.InputsSent);
            if (flow.HasMatch)
            {
                foreach (var f in flow.Fighters)
                {
                    sb.Append('\n').Append(f.DisplayName).Append(" T").Append(f.TeamId).Append("  hp ").Append(f.Health.ToString("0")).Append('/').Append(f.MaxHealth.ToString("0"))
                      .Append("  ammo ").Append(f.Ammo.currentAmmo).Append('/').Append(f.Ammo.maxAmmo)
                      .Append("  v ").Append(f.Velocity.magnitude.ToString("0.0"));
                    if (f.Invulnerable) sb.Append("  GOD");
                    if (f.IsPoisoned) sb.Append("  POISON");
                    if (f.Effects.Count > 0)
                    {
                        sb.Append("  fx[");
                        for (int i = 0; i < f.Effects.Count; i++) sb.Append(i > 0 ? "," : "").Append(f.Effects[i].Spec.type);
                        sb.Append(']');
                    }
                }
                sb.Append("\nprojectiles ").Append(Projectile.Active.Count);
            }
            sb.Append("\nF1 패널  F2 다음 맵  F3 카드 지급  F4 적 처치  F5 콘텐츠 리로드(타이틀)  F6 무적  F7 슬로모");

            int issueLines = 0;
            var issues = new StringBuilder();
            foreach (var issue in ContentDatabase.Issues)
            {
                if (issue.Severity == ContentSeverity.Info) continue;
                if (issueLines++ >= 10) { issues.Append("…\n"); break; }
                issues.Append(issue.Severity == ContentSeverity.Error ? "[오류] " : "[경고] ").Append(issue.Source).Append(": ").Append(issue.Message).Append('\n');
            }
            var logs = new StringBuilder();
            foreach (var l in _log) logs.Append(l).Append('\n');

            var text = sb.ToString();
            int lines = text.Split('\n').Length;
            // Anchored bottom-left (above the Esc footer) so it never covers the score or the card lists.
            const float line = 24f;
            float y = H - 52;
            if (_log.Count > 0)
            {
                var lr = new Rect(14, y - (line * _log.Count + 16), 1200, line * _log.Count + 16);
                Fill(lr, new Color(0.2f, 0.12f, 0, 0.75f));
                Text(new Rect(lr.x + 10, lr.y + 8, lr.width - 20, lr.height - 16), logs.ToString(), Tiny, new Color(1f, 0.85f, 0.5f));
                y = lr.y - 6;
            }
            if (issueLines > 0)
            {
                int n = Mathf.Min(issueLines, 11);
                var ir = new Rect(14, y - (line * n + 16), 1200, line * n + 16);
                Fill(ir, new Color(0.25f, 0, 0, 0.8f));
                Text(new Rect(ir.x + 10, ir.y + 8, ir.width - 20, ir.height - 16), issues.ToString(), Tiny, new Color(1f, 0.75f, 0.6f));
                y = ir.y - 6;
            }
            var r = new Rect(14, y - (line * lines + 16), 1000, line * lines + 16);
            Fill(r, new Color(0, 0, 0, 0.7f));
            Text(new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - 16), text, Tiny, new Color(0.6f, 1f, 0.7f));
        }

        // ---------------------------------------------------------------- world gizmos

        private void DrawGizmos(GameFlow flow)
        {
            var cam = Camera.main;
            if (cam == null) return;
            WorldRect(cam, Vector2.zero, new Vector2(ContentDatabase.ArenaHalfWidth * 2f, ContentDatabase.ArenaHalfHeight * 2f), new Color(1f, 0.9f, 0.2f, 0.9f), 3f, "arena");
            var map = flow.CurrentMap;
            if (map != null)
            {
                Vector2 origin = map.transform.position;
                var def = map.Definition;
                for (int i = 0; i < def.Solids.Length; i++)
                    WorldRect(cam, origin + def.Solids[i].Center, def.Solids[i].Size, new Color(0.3f, 0.9f, 1f, 0.9f), 2f, "solid " + i);
                for (int i = 0; i < map.Bodies.Count; i++)
                {
                    var b = map.Bodies[i];
                    if (b == null) continue;
                    WorldRect(cam, b.transform.position, b.transform.lossyScale, new Color(1f, 0.6f, 0.2f, 0.9f), 2f, "box " + i);
                }
                for (int s = 0; s < def.Spawns.Length; s++)
                {
                    Vector2 p = WorldToGui(cam, origin + def.Spawns[s]);
                    Disc(p, 9, new Color(0.2f, 1f, 0.3f, 0.9f));
                    Text(new Rect(p.x + 10, p.y - 12, 140, 24), "spawn " + s + " (T" + s + ")", Tiny, new Color(0.2f, 1f, 0.3f));
                }
            }
            foreach (var f in flow.Fighters)
            {
                if (!f.IsVisible) continue;
                Vector2 c = WorldToGui(cam, f.Position);
                Vector2 v = WorldToGui(cam, f.Position + f.Velocity * 0.25f);
                Line(c, v, new Color(1f, 0.3f, 1f, 0.9f), 2f);
                Vector2 a = WorldToGui(cam, f.Position + f.AimDirection * 3f);
                Line(c, a, new Color(1f, 1f, 1f, 0.6f), 1f);
                Text(new Rect(c.x + 18, c.y + 8, 200, 22), (f.IsGrounded ? "ground " : "air ") + f.Velocity.magnitude.ToString("0.0"), Tiny, Color.white);
            }
            foreach (var p in Projectile.Active)
            {
                if (p == null) continue;
                Vector2 c = WorldToGui(cam, p.Position);
                Vector2 e = WorldToGui(cam, p.Position + Vector2.right * p.Radius);
                Disc(c, Mathf.Max(3f, e.x - c.x), new Color(1f, 0.2f, 0.2f, 0.35f));
            }
        }

        private static void WorldRect(Camera cam, Vector2 center, Vector2 size, Color color, float thickness, string label)
        {
            Vector2 a = WorldToGui(cam, center + new Vector2(-size.x, size.y) * 0.5f);
            Vector2 b = WorldToGui(cam, center + new Vector2(size.x, -size.y) * 0.5f);
            var r = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            Fill(new Rect(r.x, r.y, r.width, thickness), color);
            Fill(new Rect(r.x, r.yMax - thickness, r.width, thickness), color);
            Fill(new Rect(r.x, r.y, thickness, r.height), color);
            Fill(new Rect(r.xMax - thickness, r.y, thickness, r.height), color);
            if (label != null) Text(new Rect(r.x + 4, r.y + 2, 160, 20), label, Tiny, color);
        }

        private static void Line(Vector2 from, Vector2 to, Color color, float width)
        {
            Vector2 d = to - from;
            float len = d.magnitude;
            if (len < 0.5f) return;
            var m = GUI.matrix;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(angle, from);
            Fill(new Rect(from.x, from.y - width / 2f, len, width), color);
            GUI.matrix = m;
        }
    }
}

