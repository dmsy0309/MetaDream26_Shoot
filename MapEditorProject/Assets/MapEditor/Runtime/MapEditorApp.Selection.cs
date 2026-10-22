using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metadream.MapEditor
{
    /// <summary>Multi-selection (drag select / Shift-click), group move · delete · duplicate · copy, and 2v2 spawns.</summary>
    public sealed partial class MapEditorApp
    {
        private readonly List<ObjRef> _multi = new List<ObjRef>();
        private readonly List<KeyValuePair<ObjRef, Shape>> _groupStart = new List<KeyValuePair<ObjRef, Shape>>();
        private bool _marqueeAdd;
        private int _teamSyncVersion = -1;

        public IReadOnlyList<ObjRef> SelectionSet => _multi;
        public bool IsSelected(ObjRef o) => _multi.Contains(o);

        /// <summary>Moves every selected object (arrow keys, tests).</summary>
        public void MoveSelectionBy(Vector2 delta)
        {
            PruneSelection();
            if (_multi.Count > 0) CommitIfNeeded(true);
            foreach (var o in _multi)
            {
                var s = Doc.GetShape(o);
                s.Center = new Vector2((float)Math.Round(s.x + delta.x, 4), (float)Math.Round(s.y + delta.y, 4));
                Doc.SetShape(o, s);
            }
            if (_multi.Count > 0) { Changed(); CommitIfNeeded(true); }
        }

        public void SetSelection(IList<ObjRef> list)
        {
            _multi.Clear();
            foreach (var o in list) if (Doc.Exists(o) && !_multi.Contains(o)) _multi.Add(o);
            _sel = _multi.Count > 0 ? _multi[_multi.Count - 1] : ObjRef.None;
            GUIUtility.keyboardControl = 0;
        }

        public void ToggleSelected(ObjRef o)
        {
            if (!Doc.Exists(o)) return;
            if (_multi.Contains(o))
            {
                _multi.Remove(o);
                if (_sel == o) _sel = _multi.Count > 0 ? _multi[_multi.Count - 1] : ObjRef.None;
            }
            else
            {
                _multi.Add(o);
                _sel = o;
            }
            GUIUtility.keyboardControl = 0;
        }

        private void PruneSelection()
        {
            _multi.RemoveAll(o => !Doc.Exists(o));
            if (Doc.Exists(_sel) && !_multi.Contains(_sel)) _multi.Add(_sel);
            if (!Doc.Exists(_sel)) _sel = _multi.Count > 0 ? _multi[_multi.Count - 1] : ObjRef.None;
        }

        private void BeginMove()
        {
            _drag = DragMode.Move;
            _startShape = Doc.GetShape(_sel);
            _groupStart.Clear();
            if (!_multi.Contains(_sel)) _multi.Add(_sel);
            foreach (var o in _multi) _groupStart.Add(new KeyValuePair<ObjRef, Shape>(o, Doc.GetShape(o)));
        }

        /// <summary>Delta comes from the snapped primary object, so the whole group keeps its spacing.</summary>
        private void MoveGroup(Vector2 delta)
        {
            foreach (var kv in _groupStart)
            {
                if (!Doc.Exists(kv.Key)) continue;
                var s = kv.Value;
                s.Center = new Vector2((float)Math.Round(kv.Value.x + delta.x, 4), (float)Math.Round(kv.Value.y + delta.y, 4));
                Doc.SetShape(kv.Key, s);
            }
            Changed();
        }

        /// <summary>Drag select tool: drag on empty space = selection box, drag on an object = move (the group if selected).</summary>
        private void BeginDragSelectClick(Vector2 w, Vector2 sp)
        {
            _clickHits = PickAll(w);
            if (_clickHits.Count > 0)
            {
                if (Shift) { ToggleSelected(_clickHits[0]); return; }
                if (IsSelected(_clickHits[0])) _sel = _clickHits[0];
                else Select(_clickHits[0]);
                _clickWasSelected = false;
                BeginMove();
                return;
            }
            _drag = DragMode.Marquee;
            _marqueeAdd = Shift;
        }

        /// <summary>Left → right: objects fully inside. Right → left: everything the box touches.</summary>
        public int FinishMarquee(Vector2 a, Vector2 b, bool add)
        {
            var box = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            bool crossing = b.x < a.x;
            var picked = add ? new List<ObjRef>(_multi) : new List<ObjRef>();
            int added = 0;
            foreach (var o in Doc.All())
            {
                var bounds = Bounds(o);
                bool hit = crossing ? box.Overlaps(bounds) : box.Contains(bounds.min) && box.Contains(bounds.max);
                if (hit && !picked.Contains(o)) { picked.Add(o); added++; }
            }
            SetSelection(picked);
            Toast(_multi.Count + "개 선택" + (crossing ? " (닿은 것 포함)" : " (완전히 들어간 것)") + " · 끌어서 함께 이동 · Delete 삭제 · Ctrl/⌘+D 복제", EUi.TextDim);
            return added;
        }

        private Rect Bounds(ObjRef o)
        {
            var s = Doc.GetShape(o);
            if (o.Kind == ObjKind.Spawn) return new Rect(s.x - 0.62f, s.y - 0.62f, 1.24f, 1.24f);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var c in DrawLayer.Corners(s.Center, new Vector2(Mathf.Max(0.01f, s.w), Mathf.Max(0.01f, s.h)), s.r))
            {
                minX = Mathf.Min(minX, c.x); minY = Mathf.Min(minY, c.y);
                maxX = Mathf.Max(maxX, c.x); maxY = Mathf.Max(maxY, c.y);
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private string CopySelection()
        {
            PruneSelection();
            var parts = new List<string>();
            foreach (var o in _multi)
            {
                string c = Doc.CopyObject(o);
                if (c != null) parts.Add(c);
            }
            return parts.Count > 0 ? string.Join("\n", parts) : null;
        }

        private List<ObjRef> PasteObjects(string clip, Vector2 offset)
        {
            var added = new List<ObjRef>();
            if (string.IsNullOrEmpty(clip)) return added;
            foreach (var line in clip.Split('\n'))
            {
                var o = Doc.PasteObject(line, offset);
                if (!o.IsNone) added.Add(o);
            }
            return added;
        }

        // ------------------------------------------------------------------ spawns

        /// <summary>Spawn tool: spawns[0..1] are the team spawns; the 3rd and 4th become 2v2 teammate spawns.</summary>
        private void AddSpawn(Vector2 p)
        {
            AddObject(ObjKind.Spawn, p);
            int count = Doc.Count(ObjKind.Spawn);
            if (count >= 3 && count <= 4 && MapDocument.GameNativeTeamSpawns)
            {
                Toast(MapDocument.SpawnRole(count - 1) + " 스폰 추가 — 게임이 직접 지원하므로 '준비' 순간부터 이 위치에 섭니다", EUi.OkColor);
            }
            else if (count >= 3 && count <= 4 && !Doc.HasTeamSpawnScript)
            {
                Doc.SetTeamSpawns(true, false);
                Select(new ObjRef(ObjKind.Spawn, count - 1));
                Changed();
                Toast("2대2 팀원 개별 스폰을 켰습니다: " + MapDocument.SpawnRole(count - 1) + " · 게임 스크립트로 적용됩니다 (오른쪽 '2대2 스폰')", EUi.OkColor);
            }
        }

        public void SetTeamSpawns(bool enabled)
        {
            Doc.SetTeamSpawns(enabled);
            PruneSelection();
            Changed();
        }

        /// <summary>Called every frame: regenerates the 2v2 block after spawns move (not while the script window is open).</summary>
        private void SyncTeamSpawns()
        {
            if (_teamSyncVersion == _docVersion || _modal == Modal.Script) return;
            _teamSyncVersion = _docVersion;
            if (Doc.SyncTeamSpawnScript())
            {
                _docVersion++;
                _teamSyncVersion = _docVersion;
                _pendingCommit = true;
                _lastChange = Time.unscaledTime;
            }
        }
    }
}
