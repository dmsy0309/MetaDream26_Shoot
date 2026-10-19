using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Metadream.Shoot.Player;
using static Metadream.Shoot.Presentation.UiKit;

namespace Metadream.Shoot.Presentation
{
    /// <summary>
    /// "정말 나가시겠습니까?" dialog for Esc during a game. '예' unlocks after YesDelay seconds; '아니요' or Esc again closes it.
    /// Offline games pause while it is open; online games keep running (the local player just stops sending input).
    /// </summary>
    public sealed class QuitConfirm : MonoBehaviour
    {
        public const float YesDelay = 3f;
        public static bool IsOpen { get; private set; }
        public static QuitConfirm Instance { get; private set; }

        private Action _onYes;
        private float _openedAt;
        private float _prevScale = 1f;
        private bool _paused;

        public static float YesUnlocksIn => IsOpen && Instance != null ? Mathf.Max(0f, YesDelay - (Time.unscaledTime - Instance._openedAt)) : YesDelay;
        public static bool YesEnabled => IsOpen && YesUnlocksIn <= 0f;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) { Close(); Instance = null; }
        }

        public static void Open(Action onYes)
        {
            if (Instance == null || IsOpen) return;
            var q = Instance;
            q._onYes = onYes;
            q._openedAt = Time.unscaledTime;
            IsOpen = true;
            Fighter.InputSuspended = true;
            var flow = GameFlow.Instance;
            q._paused = flow != null && flow.Role == NetRole.Offline && !flow.ReplayMode;
            if (q._paused) { q._prevScale = Time.timeScale; Time.timeScale = 0f; }
            Audio.Sfx.Play("ui_click");
        }

        public static void Close()
        {
            if (!IsOpen || Instance == null) return;
            var q = Instance;
            IsOpen = false;
            Fighter.InputSuspended = CheatPanel.IsOpen;
            if (q._paused) Time.timeScale = q._prevScale > 0.01f ? q._prevScale : 1f;
            q._paused = false;
            q._onYes = null;
        }

        /// <summary>Confirms (only after the delay). Returns false while '예' is still locked.</summary>
        public static bool Confirm()
        {
            if (!YesEnabled) return false;
            var action = Instance._onYes;
            Close();
            action?.Invoke();
            return true;
        }

        private void Update()
        {
            if (!IsOpen) return;
            var flow = GameFlow.Instance;
            if (flow == null || flow.InMenu || !flow.HasMatch) { Close(); return; }
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.nKey.wasPressedThisFrame) Close();
            else if (YesEnabled && (kb.enterKey.wasPressedThisFrame || kb.yKey.wasPressedThisFrame)) Confirm();
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            UiKit.Begin();
            GUI.depth = -95;
            Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.72f));
            var panel = new Rect(W / 2 - 400, H / 2 - 190, 800, 380);
            Fill(new Rect(panel.x + 8, panel.y + 10, panel.width, panel.height), new Color(0, 0, 0, 0.45f));
            // Linear colour space brightens IMGUI colours, so this reads as a deep navy; alpha 1 keeps card text from bleeding through.
            Fill(new Rect(panel.x - 3, panel.y - 3, panel.width + 6, panel.height + 6), new Color(0.35f, 0.38f, 0.48f, 1f));
            Fill(panel, new Color(0.012f, 0.014f, 0.025f, 1f));
            Fill(new Rect(panel.x, panel.y, panel.width, 8), new Color(1f, 0.45f, 0.35f));
            Shadowed(new Rect(panel.x, panel.y + 40, panel.width, 70), "정말 나가시겠습니까?", Big, Color.white);
            var flow = GameFlow.Instance;
            string sub = flow == null ? "" : flow.Role == NetRole.Client ? "방에서 나가면 같은 컴퓨터에서만 다시 들어올 수 있습니다"
                : flow.Role == NetRole.Host ? "방장이 나가면 방이 닫힙니다" : flow.IsSandbox ? "튜토리얼을 종료합니다" : "진행 중인 게임이 끝납니다";
            Text(new Rect(panel.x, panel.y + 120, panel.width, 34), sub, Small, new Color(1, 1, 1, 0.7f));

            float left = YesUnlocksIn;
            bool yes = left <= 0f;
            var yesRect = new Rect(panel.x + 90, panel.y + 220, 280, 80);
            var noRect = new Rect(panel.xMax - 370, panel.y + 220, 280, 80);
            Fill(yesRect, yes ? new Color(1f, 0.45f, 0.35f) : new Color(1, 1, 1, 0.12f));
            Text(yesRect, yes ? "예 (Enter)" : "예 (" + Mathf.CeilToInt(left) + ")", Mid, yes ? Palette.Ink : new Color(1, 1, 1, 0.4f));
            if (!yes) Fill(new Rect(yesRect.x, yesRect.yMax - 6, yesRect.width * (1f - left / YesDelay), 6), new Color(1f, 0.45f, 0.35f));
            Fill(noRect, new Color(1, 1, 1, 0.9f));
            Text(noRect, "아니요 (Esc)", Mid, Palette.Ink);
            if (Clicked(noRect)) Close();
            else if (yes && Clicked(yesRect)) Confirm();
        }
    }
}
