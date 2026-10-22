using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using Metadream.Shoot.Content;
using Metadream.Shoot.Maps;
using Metadream.Shoot.Presentation;

namespace Metadream.MapEditor
{
    public enum Tool { Select, Solid, Box, Spawn, Mover, Pad, Hazard, Wind, Area, DragSelect }

    /// <summary>
    /// Metadream Shoot Map Maker. The map is drawn by the game's MapRoot (same sprites, colours, colliders),
    /// validated by the game's ContentDatabase.ParseMap and played by the game's GameFlow.
    /// </summary>
    public sealed partial class MapEditorApp : MonoBehaviour
    {
        [SerializeField] private Material spriteMaterial;

        public static MapEditorApp Instance { get; private set; }
        public const float TopBar = 48f, LeftPanel = 230f, RightPanel = 344f, StatusBar = 26f;
        public const float ArenaW = ContentDatabase.ArenaHalfWidth, ArenaH = ContentDatabase.ArenaHalfHeight;
        private static readonly Color EditorBackdrop = new Color(0.03f, 0.033f, 0.045f);

        public MapDocument Doc { get; private set; }
        public ObjRef Selection => _sel;
        public Tool CurrentTool => _tool;
        public Playtest Play => _play;
        public bool IsPlaying => _play.Running;
        public IReadOnlyList<ContentIssue> Issues => _issues;

        private Camera _cam;
        private MapRoot _preview;
        private MapDefinition _previewDef;
        private int _docVersion, _previewVersion = -1;
        private DrawLayer _bg, _under, _over;
        private Tool _tool = Tool.Select;
        private ObjRef _sel = ObjRef.None;
        private readonly Playtest _play = new Playtest();

        private enum DragMode { None, Move, Resize, Rotate, Create, PathPoint, Pan, Marquee }
        private DragMode _drag;
        private int _panButton;
        private Vector2 _dragStartWorld, _dragStartScreen, _createStart;
        private Shape _startShape;
        private int _hx, _hy, _pathIndex;
        private Vector3 _panCamStart;
        private bool _dragMoved, _clickWasSelected;
        private List<ObjRef> _clickHits = new List<ObjRef>();

        private float _snap = 0.5f;
        private bool _snapOn = true, _showGrid = true, _showLabels = true, _animateMovers = true;
        private float _moverClock;

        private readonly List<string> _undo = new List<string>();
        private readonly List<string> _redo = new List<string>();
        private string _committed = "", _savedJson = "";
        private bool _pendingCommit;
        private float _lastChange;

        private List<ContentIssue> _issues = new List<ContentIssue>();
        private MapDefinition _validDef;
        private int _issuesVersion = -1;

        private bool _textFocus;
        private string _toast;
        private float _toastUntil;
        private Color _toastColor = Color.white;
        private Vector2 _cursorWorld;
        private bool _cursorInView;
        private string _clipboard;
        private float _nextAutosave;
        private bool _quitConfirmed;

        public bool Unsaved => _pendingCommit ? Doc.ToJson(true) != _savedJson : _committed != _savedJson;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            Instance = this;
            if (spriteMaterial != null) SpriteFactory.Material = spriteMaterial;
            // Same frame settings as the game's GameBootstrap.Awake.
            Application.targetFrameRate = 120;
            Application.runInBackground = true;
            Time.fixedDeltaTime = 0.02f;
            _cam = Camera.main;
            if (_cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                _cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = EditorBackdrop;
            _cam.transform.position = new Vector3(0f, 0f, -10f);

            // The game's content loader only ever sees the editor's own external folder.
            Directory.CreateDirectory(GameContent.PlaytestContentDir);
            GameContent.ClearPlaytestContent();
            ContentDatabase.ExternalDirectory = GameContent.PlaytestContentDir;

            _bg = new DrawLayer("EditorBackground", -100, SpriteFactory.Material);
            _under = new DrawLayer("EditorGrid", -50, SpriteFactory.Material);
            _over = new DrawLayer("EditorOverlay", 100, SpriteFactory.Material);
            LoadSettings();
            SetDocument(MapDocument.CreateDefault(), false);
            Application.wantsToQuit += OnWantsToQuit;
            Debug.Log("[MapEditor] start " + Application.version + " · " + GameRuntimeInfo.Current.Short);
        }

        private void Start()
        {
            UpdateCameraRect();
            FitView();
            RunCommandLine();
        }

        private void OnDestroy()
        {
            Application.wantsToQuit -= OnWantsToQuit;
            if (Instance == this) Instance = null;
        }

        private bool OnWantsToQuit()
        {
            if (_quitConfirmed || Doc == null || !Unsaved || Application.isBatchMode) return true;
            AskDiscard("저장하지 않은 변경 사항이 있습니다. 종료할까요?", () => { _quitConfirmed = true; Application.Quit(); });
            return false;
        }

        private void Update()
        {
            if (_play.Running)
            {
                _play.Update();
                return;
            }
            UpdateCameraRect();
            HandleKeys();
            HandleMouse();
            SyncTeamSpawns();
            CommitIfNeeded();
            if (_issuesVersion != _docVersion) Revalidate();
            if (_previewVersion != _docVersion) RebuildPreview();
            AnimatePreview();
            DrawWorld();
            Autosave();
        }

        // ------------------------------------------------------------------ document

        public void SetDocument(MapDocument doc, bool fromFile)
        {
            Doc = doc;
            Doc.Normalize();
            _sel = ObjRef.None;
            _multi.Clear();
            _undo.Clear();
            _redo.Clear();
            _committed = Doc.ToJson(true);
            _savedJson = _committed;
            _pendingCommit = false;
            _docVersion++;
            _drag = DragMode.None;
            GUIUtility.keyboardControl = 0;
            if (fromFile && Doc.FilePath != null) AddRecent(Doc.FilePath);
        }

        public void Changed()
        {
            _docVersion++;
            _pendingCommit = true;
            _lastChange = Time.unscaledTime;
        }

        private void CommitIfNeeded(bool force = false)
        {
            if (!_pendingCommit) return;
            if (!force && (_drag != DragMode.None || (_textFocus && Time.unscaledTime - _lastChange < 0.8f))) return;
            string now = Doc.ToJson(true);
            if (now != _committed)
            {
                _undo.Add(_committed);
                if (_undo.Count > 300) _undo.RemoveAt(0);
                _redo.Clear();
                _committed = now;
            }
            _pendingCommit = false;
        }

        public void Undo()
        {
            CommitIfNeeded(true);
            if (_undo.Count == 0) { Toast("더 되돌릴 작업이 없습니다", EUi.TextDim); return; }
            _redo.Add(_committed);
            _committed = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            Restore(_committed);
        }

        public void Redo()
        {
            CommitIfNeeded(true);
            if (_redo.Count == 0) return;
            _undo.Add(_committed);
            _committed = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            Restore(_committed);
        }

        private void Restore(string json)
        {
            string path = Doc.FilePath;
            var sel = _sel;
            Doc = MapDocument.FromJson(json);
            Doc.FilePath = path;
            _sel = Doc.Exists(sel) ? sel : ObjRef.None;
            PruneSelection();
            _docVersion++;
            GUIUtility.keyboardControl = 0;
        }

        private void Revalidate()
        {
            _issuesVersion = _docVersion;
            try { _issues = GameContent.Validate(Doc, true, out _validDef); }
            catch (Exception e) { _issues = new List<ContentIssue> { new ContentIssue(ContentSeverity.Error, "editor", e.Message) }; _validDef = null; }
        }

        public bool GameAccepts => _validDef != null && GameContent.ErrorCount(_issues) == 0;

        public void Select(ObjRef o)
        {
            if (o != _sel) GUIUtility.keyboardControl = 0;
            _sel = Doc != null && Doc.Exists(o) ? o : ObjRef.None;
            _multi.Clear();
            if (!_sel.IsNone) _multi.Add(_sel);
        }

        public void DeleteSelection()
        {
            PruneSelection();
            if (_multi.Count == 0) return;
            CommitIfNeeded(true);
            // Highest index first so the remaining indices of the same list stay valid.
            var order = new List<ObjRef>(_multi);
            order.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : b.Index.CompareTo(a.Index));
            foreach (var o in order) Doc.Remove(o);
            if (order.Count > 1) Toast(order.Count + "개 삭제", EUi.TextDim);
            Select(ObjRef.None);
            Changed();
            CommitIfNeeded(true);
        }

        public void DuplicateSelection()
        {
            PruneSelection();
            if (_multi.Count == 0) return;
            CommitIfNeeded(true);
            float d = _snapOn ? Mathf.Max(_snap, 1f) : 1f;
            var copies = PasteObjects(CopySelection(), new Vector2(d, -d));
            if (copies.Count > 0) { SetSelection(copies); Changed(); CommitIfNeeded(true); }
        }

        public ObjRef AddObject(ObjKind kind, Vector2 center, Vector2? size = null)
        {
            var o = Doc.Add(kind, center, size);
            Select(o);
            Changed();
            return o;
        }

        // ------------------------------------------------------------------ view

        private Rect ViewportPixels
        {
            get
            {
                float s = EUi.Scale;
                return new Rect(LeftPanel * s, StatusBar * s, Mathf.Max(10f, Screen.width - (LeftPanel + RightPanel) * s), Mathf.Max(10f, Screen.height - (TopBar + StatusBar) * s));
            }
        }

        private Rect ViewportGui => new Rect(LeftPanel, TopBar, EUi.W - LeftPanel - RightPanel, EUi.H - TopBar - StatusBar);

        private void UpdateCameraRect()
        {
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var vp = ViewportPixels;
            _cam.rect = new Rect(vp.x / Screen.width, vp.y / Screen.height, vp.width / Screen.width, vp.height / Screen.height);
        }

        private float PixelWorld => 2f * _cam.orthographicSize / Mathf.Max(1f, ViewportPixels.height);

        public void FitView()
        {
            var vp = ViewportPixels;
            float aspect = vp.width / Mathf.Max(1f, vp.height);
            _cam.orthographicSize = Mathf.Max(ArenaH + 2.5f, (ArenaW + 2.5f) / Mathf.Max(0.1f, aspect));
            _cam.transform.position = new Vector3(0f, 0f, -10f);
        }

        private Vector2 ScreenToWorld(Vector2 sp) => _cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 10f));

        private Vector2 WorldToGui(Vector2 w)
        {
            Vector3 sp = _cam.WorldToScreenPoint(new Vector3(w.x, w.y, 0f));
            return new Vector2(sp.x / EUi.Scale, (Screen.height - sp.y) / EUi.Scale);
        }

        private float SnapF(float v)
        {
            if (!_snapOn || _snap <= 0f) return (float)Math.Round(v, 3);
            return (float)Math.Round(Math.Round(v / _snap) * _snap, 4);
        }

        private Vector2 Snap(Vector2 v) => new Vector2(SnapF(v.x), SnapF(v.y));

        private void RebuildPreview()
        {
            _previewVersion = _docVersion;
            if (_preview != null) Destroy(_preview.gameObject);
            _previewDef = Doc.ToPreviewDefinition();
            _preview = MapRoot.Build(_previewDef, Vector2.zero);
            _preview.name = "EditorPreview_" + _previewDef.Id;
            _preview.SetSimulated(false);
        }

        /// <summary>Play tests must start with no editor colliders in the physics world.</summary>
        private void DestroyPreviewImmediate()
        {
            if (_preview != null) DestroyImmediate(_preview.gameObject);
            _preview = null;
            _previewVersion = -1;
        }

        private void AnimatePreview()
        {
            if (_preview == null || _previewDef == null) return;
            _moverClock = _animateMovers && _drag == DragMode.None ? _moverClock + Time.deltaTime : 0f;
            int boxCount = _previewDef.Boxes.Length;
            var bodies = _preview.Bodies;
            for (int i = 0; i < _previewDef.Movers.Length; i++)
            {
                int bi = boxCount + i;
                if (bi >= bodies.Count || bodies[bi] == null) continue;
                var m = _previewDef.Movers[i];
                bodies[bi].transform.localPosition = m.Block.Center + (_animateMovers ? m.OffsetAt(_moverClock) : Vector2.zero);
            }
        }

        // ------------------------------------------------------------------ picking

        private List<ObjRef> PickAll(Vector2 w)
        {
            var hits = new List<ObjRef>();
            float tol = 3f * PixelWorld;
            for (int i = Doc.Count(ObjKind.Spawn) - 1; i >= 0; i--)
            {
                var o = new ObjRef(ObjKind.Spawn, i);
                if (Doc.Exists(o) && Vector2.Distance(Doc.GetShape(o).Center, w) <= 0.62f + tol) hits.Add(o);
            }
            foreach (var kind in new[] { ObjKind.Area, ObjKind.Pad, ObjKind.Hazard, ObjKind.Mover, ObjKind.Box, ObjKind.Solid, ObjKind.Wind })
            {
                for (int i = Doc.Count(kind) - 1; i >= 0; i--)
                {
                    var o = new ObjRef(kind, i);
                    if (!Doc.Exists(o)) continue;
                    var s = Doc.GetShape(o);
                    var block = new MapBlock(s.x, s.y, Mathf.Max(0.2f, s.w), Mathf.Max(0.2f, s.h), s.r);
                    if (block.Contains(w, tol)) hits.Add(o);
                }
            }
            return hits;
        }

        private bool Near(Vector2 a, Vector2 b, float pixels) => Vector2.Distance(a, b) <= pixels * PixelWorld;
        private Vector2 HandlePos(Shape s, int sx, int sy) => s.Center + DrawLayer.Rotate(new Vector2(sx * s.w * 0.5f, sy * s.h * 0.5f), s.r);
        private Vector2 RotHandlePos(Shape s) => s.Center + DrawLayer.Rotate(new Vector2(0f, s.h * 0.5f + 26f * PixelWorld), s.r);

        private static ObjKind KindOf(Tool t)
        {
            switch (t)
            {
                case Tool.Solid: return ObjKind.Solid;
                case Tool.Box: return ObjKind.Box;
                case Tool.Spawn: return ObjKind.Spawn;
                case Tool.Mover: return ObjKind.Mover;
                case Tool.Pad: return ObjKind.Pad;
                case Tool.Hazard: return ObjKind.Hazard;
                case Tool.Wind: return ObjKind.Wind;
                case Tool.Area: return ObjKind.Area;
                default: return ObjKind.None;
            }
        }

        public void SetTool(Tool t)
        {
            _tool = t;
            _drag = DragMode.None;
        }

        // ------------------------------------------------------------------ mouse

        private bool Shift
        {
            get { var kb = Keyboard.current; return kb != null && kb.shiftKey.isPressed; }
        }

        private void HandleMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 sp = mouse.position.ReadValue();
            bool blocked = _modal != Modal.None;
            _cursorInView = !blocked && ViewportPixels.Contains(sp);
            Vector2 w = ScreenToWorld(sp);
            _cursorWorld = w;

            float scroll = mouse.scroll.ReadValue().y;
            if (_cursorInView && Mathf.Abs(scroll) > 0.01f && _drag != DragMode.Pan)
            {
                Vector2 before = w;
                float factor = scroll > 0f ? 1f / 1.12f : 1.12f;
                _cam.orthographicSize = Mathf.Clamp(_cam.orthographicSize * factor, 2.5f, 70f);
                Vector2 after = ScreenToWorld(sp);
                _cam.transform.position += (Vector3)(before - after);
                w = ScreenToWorld(sp);
            }

            var kb = Keyboard.current;
            bool space = kb != null && kb.spaceKey.isPressed && !_textFocus;
            if (_drag == DragMode.None && _cursorInView)
            {
                if (mouse.middleButton.wasPressedThisFrame) BeginPan(sp, 2);
                else if (mouse.rightButton.wasPressedThisFrame) BeginPan(sp, 1);
                else if (mouse.leftButton.wasPressedThisFrame)
                {
                    if (space) BeginPan(sp, 0);
                    else BeginLeft(w, sp);
                }
            }

            if (_drag == DragMode.Pan)
            {
                var button = _panButton == 0 ? mouse.leftButton : _panButton == 1 ? mouse.rightButton : mouse.middleButton;
                if (!button.isPressed) _drag = DragMode.None;
                else _cam.transform.position = _panCamStart - (Vector3)((sp - _dragStartScreen) * PixelWorld);
                return;
            }
            if (_drag != DragMode.None)
            {
                if (mouse.leftButton.isPressed) ContinueDrag(w, sp);
                else EndDrag(w);
            }
        }

        private void BeginPan(Vector2 sp, int button)
        {
            _drag = DragMode.Pan;
            _panButton = button;
            _dragStartScreen = sp;
            _panCamStart = _cam.transform.position;
        }

        private void BeginLeft(Vector2 w, Vector2 sp)
        {
            GUIUtility.keyboardControl = 0;
            _dragMoved = false;
            _dragStartWorld = w;
            _dragStartScreen = sp;
            if (_tool != Tool.Select)
            {
                if (_tool == Tool.DragSelect)
                {
                    BeginDragSelectClick(w, sp);
                    return;
                }
                if (_tool == Tool.Spawn)
                {
                    AddSpawn(Snap(w));
                    if (!Shift) _tool = Tool.Select;
                    return;
                }
                _drag = DragMode.Create;
                _createStart = Snap(w);
                return;
            }
            if (Doc.Exists(_sel) && _multi.Count <= 1)
            {
                var s = Doc.GetShape(_sel);
                if (MapDocument.IsRect(_sel.Kind))
                {
                    if (Near(w, RotHandlePos(s), 9f)) { _drag = DragMode.Rotate; _startShape = s; return; }
                    for (int sx = -1; sx <= 1; sx++)
                    for (int sy = -1; sy <= 1; sy++)
                    {
                        if (sx == 0 && sy == 0) continue;
                        if (Near(w, HandlePos(s, sx, sy), 8f)) { _drag = DragMode.Resize; _hx = sx; _hy = sy; _startShape = s; return; }
                    }
                }
                if (_sel.Kind == ObjKind.Mover)
                {
                    var m = Doc.MoverAt(_sel);
                    for (int i = m.path.Length - 1; i >= 0; i--)
                    {
                        if (m.path[i] == null) continue;
                        if (Near(w, s.Center + new Vector2(m.path[i].x, m.path[i].y), 8f)) { _drag = DragMode.PathPoint; _pathIndex = i; _startShape = s; return; }
                    }
                }
            }
            _clickHits = PickAll(w);
            if (_clickHits.Count == 0)
            {
                if (!Shift) Select(ObjRef.None);
                BeginPan(sp, 0);
                return;
            }
            if (Shift) { ToggleSelected(_clickHits[0]); return; }
            _clickWasSelected = _clickHits.Contains(_sel);
            // Clicking an object of a multi-selection drags the whole group.
            if (_multi.Count > 1 && IsSelected(_clickHits[0])) { _sel = _clickHits[0]; _clickWasSelected = false; }
            else if (!_clickWasSelected) Select(_clickHits[0]);
            BeginMove();
        }

        private void ContinueDrag(Vector2 w, Vector2 sp)
        {
            if (!_dragMoved && (sp - _dragStartScreen).magnitude > 3f) _dragMoved = true;
            if (!_dragMoved || _drag == DragMode.Create || _drag == DragMode.Marquee) return;
            if (!Doc.Exists(_sel)) { _drag = DragMode.None; return; }
            var start = _startShape;
            var s = start;
            switch (_drag)
            {
                case DragMode.Move:
                {
                    Vector2 delta = w - _dragStartWorld;
                    if (_sel.Kind == ObjKind.Spawn || start.r != 0f) s.Center = Snap(start.Center + delta);
                    else
                    {
                        Vector2 min = Snap(start.Center - start.Size * 0.5f + delta);
                        s.Center = new Vector2((float)Math.Round(min.x + start.w * 0.5f, 4), (float)Math.Round(min.y + start.h * 0.5f, 4));
                    }
                    MoveGroup(s.Center - start.Center);
                    return;
                }
                case DragMode.Resize:
                {
                    Vector2 local = DrawLayer.Rotate(w - start.Center, -start.r);
                    float left = -start.w * 0.5f, right = start.w * 0.5f, bottom = -start.h * 0.5f, top = start.h * 0.5f;
                    const float min = 0.1f;
                    if (_hx != 0)
                    {
                        float edge = local.x;
                        if (start.r == 0f) edge = SnapF(start.x + edge) - start.x;
                        else if (_hx > 0) edge = left + SnapF(edge - left);
                        else edge = right - SnapF(right - edge);
                        if (_hx > 0) right = Mathf.Max(edge, left + min); else left = Mathf.Min(edge, right - min);
                    }
                    if (_hy != 0)
                    {
                        float edge = local.y;
                        if (start.r == 0f) edge = SnapF(start.y + edge) - start.y;
                        else if (_hy > 0) edge = bottom + SnapF(edge - bottom);
                        else edge = top - SnapF(top - edge);
                        if (_hy > 0) top = Mathf.Max(edge, bottom + min); else bottom = Mathf.Min(edge, top - min);
                    }
                    s.w = (float)Math.Round(right - left, 4);
                    s.h = (float)Math.Round(top - bottom, 4);
                    Vector2 c = start.Center + DrawLayer.Rotate(new Vector2((left + right) * 0.5f, (bottom + top) * 0.5f), start.r);
                    s.Center = new Vector2((float)Math.Round(c.x, 4), (float)Math.Round(c.y, 4));
                    break;
                }
                case DragMode.Rotate:
                {
                    float a0 = Mathf.Atan2(_dragStartWorld.y - start.y, _dragStartWorld.x - start.x) * Mathf.Rad2Deg;
                    float a1 = Mathf.Atan2(w.y - start.y, w.x - start.x) * Mathf.Rad2Deg;
                    float r = start.r + Mathf.DeltaAngle(a0, a1);
                    float step = Shift ? 15f : _snapOn ? 5f : 0f;
                    if (step > 0f) r = Mathf.Round(r / step) * step;
                    r = Mathf.Repeat(r + 180f, 360f) - 180f;
                    s.r = (float)Math.Round(r, 2);
                    break;
                }
                case DragMode.PathPoint:
                {
                    var m = Doc.MoverAt(_sel);
                    if (_pathIndex < m.path.Length && m.path[_pathIndex] != null)
                    {
                        Vector2 off = Snap(w - start.Center);
                        m.path[_pathIndex].x = off.x;
                        m.path[_pathIndex].y = off.y;
                        Changed();
                    }
                    return;
                }
            }
            Doc.SetShape(_sel, s);
            Changed();
        }

        private void EndDrag(Vector2 w)
        {
            if (_drag == DragMode.Marquee)
            {
                if (_dragMoved) FinishMarquee(_dragStartWorld, w, _marqueeAdd);
                else if (!_marqueeAdd) Select(ObjRef.None);
            }
            else if (_drag == DragMode.Create)
            {
                Vector2 a = _createStart, b = Snap(w);
                Vector2 size = new Vector2(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
                var kind = KindOf(_tool);
                if (kind == ObjKind.Spawn) AddSpawn(a);
                else if (size.x < 0.25f || size.y < 0.25f) AddObject(kind, a);
                else AddObject(kind, (a + b) * 0.5f, size);
                if (!Shift) _tool = Tool.Select;
            }
            else if (_drag == DragMode.Move && !_dragMoved && _clickWasSelected && _clickHits.Count > 1)
            {
                int i = _clickHits.IndexOf(_sel);
                Select(_clickHits[(i + 1) % _clickHits.Count]);
            }
            _drag = DragMode.None;
        }

        // ------------------------------------------------------------------ keyboard

        private void HandleKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            bool mod = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed || kb.leftMetaKey.isPressed || kb.rightMetaKey.isPressed;
            bool shift = kb.shiftKey.isPressed;
            if (mod && kb.sKey.wasPressedThisFrame) { if (shift) OpenSaveAs(); else Save(); return; }
            if (mod && kb.oKey.wasPressedThisFrame) { OpenModal(Modal.Open); return; }
            if (mod && kb.nKey.wasPressedThisFrame) { NewMap(); return; }
            if (mod && kb.eKey.wasPressedThisFrame) { OpenModal(Modal.Export); return; }
            if (_modal != Modal.None)
            {
                if (kb.escapeKey.wasPressedThisFrame) CloseModal();
                return;
            }
            if (kb.f5Key.wasPressedThisFrame) { StartPlaytest(_ptKind, _ptOpponent); return; }
            if (_textFocus)
            {
                if (kb.escapeKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) GUIUtility.keyboardControl = 0;
                return;
            }
            if (mod && kb.zKey.wasPressedThisFrame) { if (shift) Redo(); else Undo(); return; }
            if (mod && kb.yKey.wasPressedThisFrame) { Redo(); return; }
            if (mod && kb.dKey.wasPressedThisFrame) { DuplicateSelection(); return; }
            if (mod && kb.cKey.wasPressedThisFrame) { _clipboard = CopySelection(); if (_clipboard != null) Toast(_multi.Count > 1 ? _multi.Count + "개 복사됨" : "복사됨", EUi.TextDim); return; }
            if (mod && kb.vKey.wasPressedThisFrame)
            {
                var pasted = PasteObjects(_clipboard, new Vector2(1f, -1f));
                if (pasted.Count > 0) { SetSelection(pasted); Changed(); }
                return;
            }
            if (mod && kb.aKey.wasPressedThisFrame) { SetSelection(new List<ObjRef>(Doc.All())); return; }
            if (mod) return;
            if (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) { DeleteSelection(); return; }
            if (kb.escapeKey.wasPressedThisFrame) { if (_tool != Tool.Select) SetTool(Tool.Select); else Select(ObjRef.None); return; }
            if (kb.vKey.wasPressedThisFrame) SetTool(Tool.Select);
            if (kb.mKey.wasPressedThisFrame) SetTool(Tool.DragSelect);
            if (kb.digit1Key.wasPressedThisFrame) SetTool(Tool.Solid);
            if (kb.digit2Key.wasPressedThisFrame) SetTool(Tool.Box);
            if (kb.digit3Key.wasPressedThisFrame) SetTool(Tool.Spawn);
            if (kb.digit4Key.wasPressedThisFrame) SetTool(Tool.Mover);
            if (kb.digit5Key.wasPressedThisFrame) SetTool(Tool.Pad);
            if (kb.digit6Key.wasPressedThisFrame) SetTool(Tool.Hazard);
            if (kb.digit7Key.wasPressedThisFrame) SetTool(Tool.Wind);
            if (kb.digit8Key.wasPressedThisFrame) SetTool(Tool.Area);
            if (kb.fKey.wasPressedThisFrame) FitView();
            if (kb.gKey.wasPressedThisFrame) _showGrid = !_showGrid;
            if (Doc.Exists(_sel))
            {
                float step = (_snapOn ? _snap : 0.1f) * (shift ? 5f : 1f);
                Vector2 nudge = Vector2.zero;
                if (kb.leftArrowKey.wasPressedThisFrame) nudge.x -= step;
                if (kb.rightArrowKey.wasPressedThisFrame) nudge.x += step;
                if (kb.upArrowKey.wasPressedThisFrame) nudge.y += step;
                if (kb.downArrowKey.wasPressedThisFrame) nudge.y -= step;
                if (nudge != Vector2.zero) MoveSelectionBy(nudge);
                if (kb.leftBracketKey.wasPressedThisFrame || kb.rightBracketKey.wasPressedThisFrame)
                {
                    float d = (shift ? 15f : 5f) * (kb.leftBracketKey.wasPressedThisFrame ? 1f : -1f);
                    foreach (var o in _multi)
                    {
                        if (!MapDocument.IsRect(o.Kind)) continue;
                        var s = Doc.GetShape(o);
                        s.r = Mathf.Repeat(s.r + d + 180f, 360f) - 180f;
                        Doc.SetShape(o, s);
                    }
                    Changed();
                }
            }
        }

        // ------------------------------------------------------------------ files

        public void NewMap() => GuardUnsaved(() => { SetDocument(MapDocument.CreateDefault(), false); Toast("새 맵", EUi.TextDim); });

        public bool OpenFile(string path)
        {
            try
            {
                path = path.Trim();
                var doc = MapDocument.FromJson(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
                doc.FilePath = path;
                SetDocument(doc, true);
                FitView();
                Toast("열기: " + path, EUi.OkColor);
                SaveLastDir(path);
                return true;
            }
            catch (Exception e)
            {
                Toast("열 수 없습니다: " + e.Message, EUi.ErrorColor);
                return false;
            }
        }

        public bool OpenBuiltin(string id)
        {
            string json = GameContent.BuiltinJson(id);
            if (json == null) { Toast("내장 맵이 없습니다: " + id, EUi.ErrorColor); return false; }
            SetDocument(MapDocument.FromJson(json, id), false);
            FitView();
            Toast("내장 맵 '" + id + "'을 열었습니다. 같은 id로 게임에 적용하면 내장 맵을 덮어씁니다.", EUi.TextDim);
            return true;
        }

        public bool SaveTo(string path)
        {
            try
            {
                path = path.Trim();
                if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) path += ".json";
                CommitIfNeeded(true);
                GameContent.WriteMapFile(Doc, path, true);
                Doc.FilePath = path;
                _savedJson = _committed;
                AddRecent(path);
                SaveLastDir(path);
                DeleteAutosave();
                string key = Path.GetFileNameWithoutExtension(path);
                if (key != Doc.Data.id) Toast("저장됨 (참고: 파일 이름 '" + key + "'과 id '" + Doc.Data.id + "'가 다릅니다. '게임에 적용'은 id로 이름을 맞춥니다)", EUi.WarnColor);
                else Toast("저장됨: " + path, EUi.OkColor);
                return true;
            }
            catch (Exception e)
            {
                Toast("저장 실패: " + e.Message, EUi.ErrorColor);
                return false;
            }
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(Doc.FilePath)) OpenSaveAs();
            else SaveTo(Doc.FilePath);
        }

        private string AutosavePath => Path.Combine(Application.persistentDataPath, "autosave.json");

        private void Autosave()
        {
            if (Time.unscaledTime < _nextAutosave) return;
            _nextAutosave = Time.unscaledTime + 30f;
            if (!Unsaved) return;
            try { File.WriteAllText(AutosavePath, Doc.ToJson(true)); }
            catch (Exception e) { Debug.LogWarning("[MapEditor] autosave failed: " + e.Message); }
        }

        private void DeleteAutosave()
        {
            try { if (File.Exists(AutosavePath)) File.Delete(AutosavePath); } catch (Exception) { }
        }

        // ------------------------------------------------------------------ settings

        private readonly List<string> _recent = new List<string>();
        public string GamePath = "";
        private string _lastDir = "";

        private void LoadSettings()
        {
            GamePath = PlayerPrefs.GetString("mm.gamePath", "");
            if (string.IsNullOrEmpty(GamePath) || GameLauncher.FindExecutable(GamePath) == null) GamePath = DetectGame() ?? GamePath;
            _lastDir = PlayerPrefs.GetString("mm.lastDir", "");
            _snap = PlayerPrefs.GetFloat("mm.snap", 0.5f);
            _snapOn = PlayerPrefs.GetInt("mm.snapOn", 1) == 1;
            _recent.Clear();
            foreach (var p in PlayerPrefs.GetString("mm.recent", "").Split('\n')) if (p.Length > 0) _recent.Add(p);
        }

        private void SaveSettings()
        {
            PlayerPrefs.SetString("mm.gamePath", GamePath ?? "");
            PlayerPrefs.SetString("mm.lastDir", _lastDir ?? "");
            PlayerPrefs.SetFloat("mm.snap", _snap);
            PlayerPrefs.SetInt("mm.snapOn", _snapOn ? 1 : 0);
            PlayerPrefs.SetString("mm.recent", string.Join("\n", _recent));
            PlayerPrefs.Save();
        }

        private void AddRecent(string path)
        {
            _recent.Remove(path);
            _recent.Insert(0, path);
            while (_recent.Count > 8) _recent.RemoveAt(_recent.Count - 1);
            SaveSettings();
        }

        private void SaveLastDir(string path)
        {
            _lastDir = Path.GetDirectoryName(path) ?? "";
            SaveSettings();
        }

        private string DefaultDir => !string.IsNullOrEmpty(_lastDir) && Directory.Exists(_lastDir) ? _lastDir : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        /// <summary>Game build next to the editor, or the builds of the game project this runtime was synced from.</summary>
        public static string DetectGame()
        {
            var candidates = new List<string>();
            string own = Path.GetDirectoryName(Application.dataPath);
            if (own != null && own.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) own = Path.GetDirectoryName(own);
            if (own != null)
            {
                string parent = Path.GetDirectoryName(own);
                foreach (var root in new[] { own, parent })
                {
                    if (root == null) continue;
                    candidates.Add(Path.Combine(root, "MetadreamShoot.app"));
                    candidates.Add(Path.Combine(root, "MetadreamShoot.exe"));
                    candidates.Add(Path.Combine(root, "MetadreamShoot", "MetadreamShoot.exe"));
                }
            }
            string src = GameRuntimeInfo.Current.sourceProject;
            if (!string.IsNullOrEmpty(src))
            {
                bool win = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
                candidates.Add(win ? Path.Combine(src, "Builds", "Windows", "MetadreamShoot", "MetadreamShoot.exe") : Path.Combine(src, "Builds", "macOS", "MetadreamShoot.app"));
            }
            foreach (var c in candidates)
                if (GameLauncher.FindExecutable(c) != null && (c.EndsWith(".app") ? Directory.Exists(c) : File.Exists(c))) return c;
            return null;
        }

        public string GameContentDir => GameContent.ContentDirForGame(GamePath);

        // ------------------------------------------------------------------ play test

        private Playtest.Kind _ptKind = Playtest.Kind.Sandbox;
        private Playtest.Opponent _ptOpponent = Playtest.Opponent.Dummy;
        private int _ptRounds = 2;
        private bool _ptUseGameContent = true;

        public string StartPlaytest(Playtest.Kind kind, Playtest.Opponent opponent)
        {
            CommitIfNeeded(true);
            _ptKind = kind;
            _ptOpponent = opponent;
            _modal = Modal.None;
            Revalidate();
            if (!GameAccepts)
            {
                Toast("검증 오류가 있어 게임이 이 맵을 불러오지 않습니다. 오른쪽 '검증' 목록을 먼저 고치세요.", EUi.ErrorColor);
                return "validation";
            }
            DestroyPreviewImmediate();
            SetEditorLayersVisible(false);
            string error = _play.Start(Doc, kind, opponent, _ptRounds, _ptUseGameContent ? GameContentDir : null, _cam, OnPlaytestStopped);
            if (error != null)
            {
                SetEditorLayersVisible(true);
                Toast(error, EUi.ErrorColor);
            }
            return error;
        }

        private void OnPlaytestStopped()
        {
            SetEditorLayersVisible(true);
            _cam.backgroundColor = EditorBackdrop;
            _previewVersion = -1;
            ContentDatabase.ExternalDirectory = GameContent.PlaytestContentDir;
        }

        private void SetEditorLayersVisible(bool visible)
        {
            _bg.Visible = visible;
            _under.Visible = visible;
            _over.Visible = visible;
            if (_dim != null) _dim.Visible = visible;
        }

        public string LaunchRealGame(GameLauncher.Mode mode, string extraArgs = null)
        {
            CommitIfNeeded(true);
            Revalidate();
            if (!GameAccepts) return "검증 오류가 있어 게임이 이 맵을 불러오지 않습니다.";
            if (GameLauncher.FindExecutable(GamePath) == null) return "게임 위치를 먼저 지정하세요 (MetadreamShoot.app / MetadreamShoot.exe).";
            string dir = GameContent.PrepareExclusiveGameContent(Doc, GameContentDir);
            return GameLauncher.Launch(GamePath, dir, mode, extraArgs);
        }

        // ------------------------------------------------------------------ toast

        public void Toast(string text, Color color)
        {
            _toast = text;
            _toastColor = color;
            _toastUntil = Time.unscaledTime + 6f;
            Debug.Log("[MapEditor] " + text);
        }
    }
}
