using UnityEngine;
using Metadream.Shoot.Presentation;

namespace Metadream.MapEditor
{
    public sealed partial class MapEditorApp
    {
        private DrawLayer _dim;
        private static readonly Color WindColor = new Color(0.55f, 0.8f, 1f);
        private static readonly Color AreaColor = new Color(0.35f, 1f, 0.6f);
        private static readonly Color SpareSpawn = new Color(0.62f, 0.64f, 0.7f);

        public static Color KindColor(ObjKind k)
        {
            switch (k)
            {
                case ObjKind.Solid: return Palette.Solid;
                case ObjKind.Box: return Palette.Box;
                case ObjKind.Mover: return Palette.Mover;
                case ObjKind.Pad: return Palette.JumpPad;
                case ObjKind.Hazard: return Palette.Hazard;
                case ObjKind.Wind: return WindColor;
                case ObjKind.Area: return AreaColor;
                case ObjKind.Spawn: return Palette.PlayerOne;
                default: return Color.white;
            }
        }

        /// <summary>spawns[i] uses the colour of player slot i (slot i plays for team i % 2).</summary>
        public static Color SpawnColor(int index) => index >= 0 && index < 4 ? Palette.SlotColor(index) : SpareSpawn;

        private void DrawWorld()
        {
            if (_dim == null) _dim = new DrawLayer("EditorDim", 50, SpriteFactory.Material);
            float px = PixelWorld;
            foreach (var l in new[] { _bg, _under, _dim, _over }) { l.Pixel = px; l.Clear(); }

            // Arena = what the game camera shows (71.12 × 40). Outside it a fighter dies.
            Color top = Doc.Background.TopColor, bottom = Doc.Background.BottomColor;
            Vector2 a = new Vector2(-ArenaW, -ArenaH), b = new Vector2(-ArenaW, ArenaH), c = new Vector2(ArenaW, ArenaH), d = new Vector2(ArenaW, -ArenaH);
            _bg.Quad(a, b, c, d, bottom, top, top, bottom);
            if (_showGrid) DrawGrid(px);
            const float far = 2000f;
            var shade = new Color(0f, 0f, 0f, 0.5f);
            _dim.Quad(new Vector2(-far, -far), new Vector2(-far, far), new Vector2(-ArenaW, far), new Vector2(-ArenaW, -far), shade);
            _dim.Quad(new Vector2(ArenaW, -far), new Vector2(ArenaW, far), new Vector2(far, far), new Vector2(far, -far), shade);
            _dim.Quad(new Vector2(-ArenaW, ArenaH), new Vector2(-ArenaW, far), new Vector2(ArenaW, far), new Vector2(ArenaW, ArenaH), shade);
            _dim.Quad(new Vector2(-ArenaW, -far), new Vector2(-ArenaW, -ArenaH), new Vector2(ArenaW, -ArenaH), new Vector2(ArenaW, -far), shade);
            _over.RectOutline(Vector2.zero, new Vector2(2f * ArenaW, 2f * ArenaH), 0f, new Color(1f, 0.85f, 0.2f, 0.85f), 2f);

            foreach (var o in Doc.All())
            {
                if (o.Kind == ObjKind.Spawn) continue;
                var s = Doc.GetShape(o);
                var col = KindColor(o.Kind);
                if (o.Kind == ObjKind.Area)
                {
                    // Trigger areas: the game draws them only when "show" is on; the editor always outlines them.
                    var area = Doc.AreaAt(o);
                    var fill = MapBackground.ParseHex(area.color, AreaColor);
                    if (!area.show) _over.RectFill(s.Center, new Vector2(Mathf.Max(0.01f, s.w), Mathf.Max(0.01f, s.h)), s.r, new Color(fill.r, fill.g, fill.b, 0.08f));
                    var p = DrawLayer.Corners(s.Center, new Vector2(Mathf.Max(0.01f, s.w), Mathf.Max(0.01f, s.h)), s.r);
                    for (int k = 0; k < 4; k++) _over.Dashed(p[k], p[(k + 1) % 4], new Color(AreaColor.r, AreaColor.g, AreaColor.b, 0.8f), 6f, 1.5f);
                    continue;
                }
                col.a = o.Kind == ObjKind.Wind ? 0.55f : 0.35f;
                _over.RectOutline(s.Center, new Vector2(Mathf.Max(0.01f, s.w), Mathf.Max(0.01f, s.h)), s.r, col);
            }

            for (int i = 0; i < Doc.Count(ObjKind.Pad); i++)
            {
                var z = Doc.Data.pads[i];
                if (z == null) continue;
                Vector2 up = DrawLayer.Rotate(Vector2.up, z.r);
                Vector2 from = new Vector2(z.x, z.y);
                _over.Arrow(from, from + up * Mathf.Clamp(z.power * 0.09f, 1.2f, 6f), new Color(0.6f, 1f, 0.75f, 0.9f));
            }
            for (int i = 0; i < Doc.Count(ObjKind.Wind); i++)
            {
                var z = Doc.Data.winds[i];
                if (z == null) continue;
                Vector2 f = new Vector2(z.fx, z.fy);
                if (f.sqrMagnitude < 1e-6f) continue;
                Vector2 dir = f.normalized * Mathf.Clamp(f.magnitude * 0.06f, 0.8f, 2.4f);
                int nx = Mathf.Clamp(Mathf.RoundToInt(z.w / 3.5f), 1, 12), ny = Mathf.Clamp(Mathf.RoundToInt(z.h / 3.5f), 1, 12);
                for (int ix = 0; ix < nx; ix++)
                for (int iy = 0; iy < ny; iy++)
                {
                    Vector2 local = new Vector2((ix + 0.5f) / nx - 0.5f, (iy + 0.5f) / ny - 0.5f);
                    Vector2 p = new Vector2(z.x, z.y) + DrawLayer.Rotate(new Vector2(local.x * z.w, local.y * z.h), z.r);
                    _over.Arrow(p - dir * 0.5f, p + dir * 0.5f, new Color(0.7f, 0.9f, 1f, 0.55f), 2f);
                }
            }

            // Mover paths: offsets from the start, looping 0 → 1 → … → 0 (MapMover.OffsetAt).
            for (int i = 0; i < Doc.Count(ObjKind.Mover); i++)
            {
                var m = Doc.Data.movers[i];
                if (m == null || m.path == null) continue;
                bool selected = _sel == new ObjRef(ObjKind.Mover, i);
                Vector2 c0 = new Vector2(m.x, m.y);
                var pathColor = new Color(0.75f, 0.65f, 1f, selected ? 0.95f : 0.45f);
                for (int p = 0; p < m.path.Length; p++)
                {
                    if (m.path[p] == null) continue;
                    Vector2 pa = c0 + new Vector2(m.path[p].x, m.path[p].y);
                    var next = m.path[(p + 1) % m.path.Length];
                    if (next != null && m.path.Length > 1) _over.Dashed(pa, c0 + new Vector2(next.x, next.y), pathColor, 7f, selected ? 2f : 1.4f);
                    if (p > 0 || (m.path[p].x != 0f || m.path[p].y != 0f))
                        _over.RectOutline(pa, new Vector2(Mathf.Max(0.01f, m.w), Mathf.Max(0.01f, m.h)), m.r, new Color(0.75f, 0.65f, 1f, selected ? 0.45f : 0.18f));
                    if (selected)
                    {
                        _over.Disc(pa, 6f * px, Color.white);
                        _over.Circle(pa, 6f * px, Palette.Mover, 2f);
                    }
                    else _over.Disc(pa, 3f * px, pathColor);
                }
            }

            // Spawns: player diameter ≈ 1.24. Without a custom teammate spawn the second player of a 2v2 team stands
            // 2.6 towards the centre (MapRoot.SpawnPoint); with one (spawns[2]/[3] + generated script) it goes there.
            bool teamSpawns = Doc.TeamSpawnsEnabled;
            for (int i = 0; i < Doc.Count(ObjKind.Spawn); i++)
            {
                var p = Doc.Data.spawns[i];
                if (p == null) continue;
                Vector2 sc = new Vector2(p.x, p.y);
                var col = i >= 2 && i < 4 && !teamSpawns ? SpareSpawn : SpawnColor(i);
                _over.Disc(sc, 0.62f, new Color(col.r, col.g, col.b, 0.9f));
                _over.Circle(sc, 0.62f, new Color(1f, 1f, 1f, 0.85f), 1.5f);
                if (i < 2)
                {
                    bool custom = teamSpawns && i + 2 < Doc.Count(ObjKind.Spawn) && Doc.Data.spawns[i + 2] != null;
                    if (custom)
                    {
                        var mate = Doc.Data.spawns[i + 2];
                        _over.Dashed(sc, new Vector2(mate.x, mate.y), new Color(col.r, col.g, col.b, 0.45f), 6f, 1.2f);
                    }
                    else
                    {
                        Vector2 mate = sc + new Vector2((p.x < 0f ? 1f : -1f) * 2.6f, 0f);
                        _over.Circle(mate, 0.62f, new Color(col.r, col.g, col.b, 0.45f), 1.2f);
                    }
                }
            }

            if (_cursorInView && _drag == DragMode.None && (_tool == Tool.Select || _tool == Tool.DragSelect))
            {
                var hits = PickAll(_cursorWorld);
                if (hits.Count > 0 && hits[0] != _sel) Outline(hits[0], new Color(1f, 1f, 1f, 0.55f), 1.5f, 0f);
            }

            foreach (var o in _multi)
                if (o != _sel && Doc.Exists(o)) Outline(o, new Color(EUi.Accent.r, EUi.Accent.g, EUi.Accent.b, 0.85f), 2f, 2f * px);
            if (_drag == DragMode.Marquee && _dragMoved)
            {
                Vector2 a0 = _dragStartWorld, b0 = _cursorWorld;
                bool crossing = b0.x < a0.x;
                var mc = crossing ? new Color(0.45f, 1f, 0.6f) : EUi.Accent;
                Vector2 center = (a0 + b0) * 0.5f, size = new Vector2(Mathf.Abs(b0.x - a0.x), Mathf.Abs(b0.y - a0.y));
                _over.RectFill(center, size, 0f, new Color(mc.r, mc.g, mc.b, 0.12f));
                var corners = DrawLayer.Corners(center, size, 0f);
                for (int k = 0; k < 4; k++)
                {
                    if (crossing) _over.Dashed(corners[k], corners[(k + 1) % 4], mc, 6f, 1.5f);
                    else _over.Thick(corners[k], corners[(k + 1) % 4], mc, 1.5f);
                }
            }
            if (Doc.Exists(_sel))
            {
                var s = Doc.GetShape(_sel);
                Outline(_sel, EUi.Accent, 2.5f, 3f * px);
                if (MapDocument.IsRect(_sel.Kind) && _multi.Count <= 1 && _tool == Tool.Select)
                {
                    Vector2 topMid = HandlePos(s, 0, 1), rot = RotHandlePos(s);
                    _over.Thick(topMid, rot, EUi.Accent, 1.5f);
                    _over.Disc(rot, 5.5f * px, Color.white);
                    _over.Circle(rot, 5.5f * px, EUi.Accent, 2f);
                    for (int sx = -1; sx <= 1; sx++)
                    for (int sy = -1; sy <= 1; sy++)
                    {
                        if (sx == 0 && sy == 0) continue;
                        Vector2 h = HandlePos(s, sx, sy);
                        _over.RectFill(h, Vector2.one * 9f * px, s.r, Color.white);
                        _over.RectOutline(h, Vector2.one * 9f * px, s.r, EUi.Accent, 1.5f);
                    }
                }
            }

            if (_drag == DragMode.Create)
            {
                Vector2 p0 = _createStart, p1 = Snap(_cursorWorld);
                var col = KindColor(KindOf(_tool));
                Vector2 center = (p0 + p1) * 0.5f, size = new Vector2(Mathf.Abs(p1.x - p0.x), Mathf.Abs(p1.y - p0.y));
                _over.RectFill(center, size, 0f, new Color(col.r, col.g, col.b, 0.35f));
                _over.RectOutline(center, size, 0f, col, 2f);
            }
            else if (_cursorInView && _tool != Tool.Select && _tool != Tool.DragSelect)
            {
                Vector2 p = Snap(_cursorWorld);
                var col = KindColor(KindOf(_tool));
                _over.Thick(p + new Vector2(-7f * px, 0f), p + new Vector2(7f * px, 0f), col, 1.5f);
                _over.Thick(p + new Vector2(0f, -7f * px), p + new Vector2(0f, 7f * px), col, 1.5f);
                if (_tool == Tool.Spawn) _over.Circle(p, 0.62f, col, 1.5f);
            }

            _bg.Apply();
            _under.Apply();
            _dim.Apply();
            _over.Apply();
        }

        private void Outline(ObjRef o, Color color, float pixels, float grow)
        {
            var s = Doc.GetShape(o);
            if (o.Kind == ObjKind.Spawn) _over.Circle(s.Center, 0.62f + grow, color, pixels);
            else _over.RectOutline(s.Center, new Vector2(Mathf.Max(0.01f, s.w) + grow * 2f, Mathf.Max(0.01f, s.h) + grow * 2f), s.r, color, pixels);
        }

        private void DrawGrid(float px)
        {
            float halfH = _cam.orthographicSize, halfW = halfH * _cam.aspect;
            Vector2 c = _cam.transform.position;
            float step = _snapOn ? Mathf.Max(0.25f, _snap) : 1f;
            while (step / px < 9f) step *= step < 1f ? 2f : 5f;
            float x0 = Mathf.Floor((c.x - halfW) / step) * step, x1 = c.x + halfW;
            float y0 = Mathf.Floor((c.y - halfH) / step) * step, y1 = c.y + halfH;
            var minor = new Color(1f, 1f, 1f, 0.035f);
            var major = new Color(1f, 1f, 1f, 0.085f);
            var axis = new Color(1f, 1f, 1f, 0.17f);
            int guard = 0;
            for (float x = x0; x <= x1 && guard < 600; x += step, guard++)
            {
                float r = Mathf.Round(x * 1000f) / 1000f;
                var col = Mathf.Abs(r) < 1e-4f ? axis : Mathf.Abs(r / 5f - Mathf.Round(r / 5f)) < 1e-4f ? major : minor;
                _under.Line(new Vector2(r, c.y - halfH), new Vector2(r, c.y + halfH), col);
            }
            guard = 0;
            for (float y = y0; y <= y1 && guard < 600; y += step, guard++)
            {
                float r = Mathf.Round(y * 1000f) / 1000f;
                var col = Mathf.Abs(r) < 1e-4f ? axis : Mathf.Abs(r / 5f - Mathf.Round(r / 5f)) < 1e-4f ? major : minor;
                _under.Line(new Vector2(c.x - halfW, r), new Vector2(c.x + halfW, r), col);
            }
        }
    }
}
