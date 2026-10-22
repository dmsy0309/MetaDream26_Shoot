using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using Metadream.Shoot.Presentation;
using static Metadream.Shoot.Presentation.UiKit;

namespace Metadream.Shoot.Online
{
    public static class ChatRules
    {
        public const int MaxLength = 160;
        public static string Clean(string value)
        {
            var b = new StringBuilder();
            foreach (char c in value ?? "")
            {
                if (b.Length >= MaxLength) break;
                if (char.IsControl(c) || char.IsSurrogate(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format) continue;
                b.Append(c);
            }
            return b.ToString().Trim();
        }
    }
    public struct ChatEntry
    {
        public int Slot;
        public string Name, Text;
        public void Write(BinaryWriter w) { w.Write((byte)Slot); w.Write(Name ?? ""); w.Write(Text ?? ""); }
        public static ChatEntry Read(BinaryReader r) => new ChatEntry { Slot = r.ReadByte(), Name = PlayerProfile.Sanitize(r.ReadString()) ?? "플레이어", Text = ChatRules.Clean(r.ReadString()) };
    }
    public sealed class ChatHistory
    {
        public const int Capacity = 64;
        public int Revision { get; private set; }
        public readonly List<ChatEntry> Entries = new List<ChatEntry>();
        public void Add(ChatEntry entry) { Revision++; Entries.Add(entry); if (Entries.Count > Capacity) Entries.RemoveAt(0); }
    }
    public sealed class ChatRateLimit
    {
        private readonly Dictionary<int, float> _next = new Dictionary<int, float>();
        public bool Allow(int sender, float now)
        {
            if (_next.TryGetValue(sender, out float next) && now < next) return false;
            _next[sender] = now + 0.75f;
            return true;
        }
    }

    [DefaultExecutionOrder(-200)]
    public sealed class ChatPanel : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        private static int _blockedFrame = -1;
        public static bool BlocksInput => IsOpen || Time.frameCount == _blockedFrame;
        private string _draft = "", _composition = "";
        private Keyboard _keyboard;
        private void OnEnable() { _keyboard = Keyboard.current; if (_keyboard != null) { _keyboard.onIMECompositionChange += OnComposition; _keyboard.SetIMEEnabled(true); } }
        private void OnDisable() { if (_keyboard != null) _keyboard.onIMECompositionChange -= OnComposition; Close(); }
        private void OnComposition(UnityEngine.InputSystem.LowLevel.IMECompositionString value) { _composition = value.ToString(); }
        private int _openedFrame, _lastCount;
        private bool _focus;
        private Vector2 _scroll;
        private float _lastMessageAt;
        private ChatHistory _history;
        private bool Available => (NetHostSession.Active != null && !NetHostSession.Active.Dedicated) ||
            (NetClientSession.Active != null && NetClientSession.Active.MySlot >= 0);
        private void Update()
        {
            var history = NetHostSession.Active != null ? NetHostSession.Active.Chat : NetClientSession.Active?.Chat;
            if (!Available) { Close(); _history = null; return; }
            if (_history != history) { Close(); _draft = ""; _lastCount = 0; _history = history; }
            if (_history != null && _history.Revision != _lastCount)
            { _lastCount = _history.Revision; _lastMessageAt = Time.unscaledTime; _scroll.y = 100000; }
            var kb = Keyboard.current;
            if (kb == null || QuitConfirm.IsOpen || CheatPanel.IsOpen) return;
            if (IsOpen && kb.escapeKey.wasPressedThisFrame) { Close(); return; }
            if (!IsOpen && (kb.tKey.wasPressedThisFrame || (kb.enterKey.wasPressedThisFrame && GameFlow.Instance != null && !GameFlow.Instance.InMenu && !GameFlow.Instance.IsPicking && GameFlow.Instance.Phase != Core.MatchPhase.GameOver)))
            { IsOpen = true; _focus = true; _openedFrame = Time.frameCount; }
        }
        private static void Close() { if (IsOpen) _blockedFrame = Time.frameCount; IsOpen = false; }
        private void Submit()
        {
            string message = ChatRules.Clean(_draft);
            if (message.Length > 0)
            {
                if (NetHostSession.Active != null) NetHostSession.Active.SendChat(message);
                else NetClientSession.Active?.SendChat(message);
            }
            _draft = ""; Close(); GUI.FocusControl(null);
        }
        private void OnDestroy() { Close(); }
        private void OnGUI()
        {
            if (!Available || _history == null) return;
            GUI.depth = -50;
            Begin();
            float x = 24, y = H - 355, width = 580;
            if (!IsOpen && Time.unscaledTime - _lastMessageAt > 9f)
            { Text(new Rect(x, H - 76, width, 30), "T  채팅", Tiny, new Color(1,1,1,0.5f)); return; }
            Fill(new Rect(x, y, width, 300), new Color(0.025f,0.03f,0.045f, IsOpen ? 0.96f : 0.76f));
            Text(new Rect(x + 14, y + 8, width - 28, 30), "방 채팅  ·  모두에게", new GUIStyle(Small) { alignment = TextAnchor.MiddleLeft }, Color.white);
            var style = new GUIStyle(Tiny) { fontSize = 19, alignment = TextAnchor.UpperLeft, wordWrap = true, richText = false };
            float contentHeight = 0;
            var heights = new List<float>();
            int start = IsOpen ? 0 : Mathf.Max(0, _history.Entries.Count - 4);
            for (int i = start; i < _history.Entries.Count; i++)
            { var e = _history.Entries[i]; float h = style.CalcHeight(new GUIContent(e.Name + ": " + e.Text), width - 52) + 10; heights.Add(h); contentHeight += h; }
            _scroll = GUI.BeginScrollView(new Rect(x + 14, y + 45, width - 28, 184), _scroll, new Rect(0,0,width - 52,contentHeight));
            float cy = 0;
            for (int i = start; i < _history.Entries.Count; i++)
            { var e = _history.Entries[i]; float h = heights[i-start]; Text(new Rect(0,cy,width - 52,h), e.Name + ": " + e.Text, style, Palette.SlotColor(Mathf.Clamp(e.Slot,0,3))); cy += h; }
            GUI.EndScrollView();
            if (!IsOpen) { Text(new Rect(x+14,y+247,width-28,35), "T 입력 · Enter 전송 · Esc 닫기", Tiny, Color.white); return; }
            var ev = Event.current;
            if (ev.type == EventType.KeyDown && (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter) && Time.frameCount != _openedFrame && string.IsNullOrEmpty(_composition))
            { Submit(); ev.Use(); return; }
            GUI.SetNextControlName("room-chat");
            _draft = GUI.TextField(new Rect(x+14,y+241,width-110,44), _draft, ChatRules.MaxLength, new GUIStyle(GUI.skin.textField) { font = Tiny.font, fontSize = 21 });
            if (_focus && ev.type == EventType.Repaint) { GUI.FocusControl("room-chat"); _focus = false; }
            if (GUI.Button(new Rect(x+width-86,y+241,72,44), "전송")) Submit();
        }
    }
}
