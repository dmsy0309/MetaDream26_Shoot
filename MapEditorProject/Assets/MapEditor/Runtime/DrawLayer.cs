using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Metadream.MapEditor
{
    /// <summary>Immediate-mode world overlay (lines + filled triangles) rebuilt every frame. No colliders.</summary>
    public sealed class DrawLayer
    {
        private readonly Mesh _mesh;
        private readonly GameObject _go;
        private readonly List<Vector3> _v = new List<Vector3>(4096);
        private readonly List<Color> _c = new List<Color>(4096);
        private readonly List<int> _lines = new List<int>(4096);
        private readonly List<int> _tris = new List<int>(4096);
        /// <summary>World units per screen pixel (set by the view each frame) for pixel-sized strokes.</summary>
        public float Pixel = 0.05f;

        public DrawLayer(string name, int sortingOrder, Material material)
        {
            _go = new GameObject(name);
            _mesh = new Mesh { name = name };
            _mesh.MarkDynamic();
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = _go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { material, material };
            mr.sortingOrder = sortingOrder;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        public bool Visible { get => _go.activeSelf; set => _go.SetActive(value); }

        public void Clear()
        {
            _v.Clear(); _c.Clear(); _lines.Clear(); _tris.Clear();
        }

        public void Line(Vector2 a, Vector2 b, Color color)
        {
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _c.Add(color); _c.Add(color);
            _lines.Add(i); _lines.Add(i + 1);
        }

        /// <summary>Line with a width in screen pixels.</summary>
        public void Thick(Vector2 a, Vector2 b, Color color, float pixels = 2f)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 1e-10f) return;
            Vector2 n = new Vector2(-d.y, d.x).normalized * (pixels * Pixel * 0.5f);
            Quad(a - n, a + n, b + n, b - n, color);
        }

        public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color) => Quad(a, b, c, d, color, color, color, color);

        public void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd)
        {
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            _c.Add(ca); _c.Add(cb); _c.Add(cc); _c.Add(cd);
            _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2);
            _tris.Add(i); _tris.Add(i + 2); _tris.Add(i + 3);
        }

        public static Vector2 Rotate(Vector2 v, float degrees)
        {
            float a = degrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        public static Vector2[] Corners(Vector2 center, Vector2 size, float degrees)
        {
            Vector2 h = size * 0.5f;
            return new[]
            {
                center + Rotate(new Vector2(-h.x, -h.y), degrees),
                center + Rotate(new Vector2(-h.x, h.y), degrees),
                center + Rotate(new Vector2(h.x, h.y), degrees),
                center + Rotate(new Vector2(h.x, -h.y), degrees)
            };
        }

        public void RectOutline(Vector2 center, Vector2 size, float degrees, Color color, float pixels = 0f)
        {
            var p = Corners(center, size, degrees);
            for (int i = 0; i < 4; i++)
            {
                if (pixels > 0f) Thick(p[i], p[(i + 1) % 4], color, pixels);
                else Line(p[i], p[(i + 1) % 4], color);
            }
        }

        public void RectFill(Vector2 center, Vector2 size, float degrees, Color color)
        {
            var p = Corners(center, size, degrees);
            Quad(p[0], p[1], p[2], p[3], color);
        }

        public void Disc(Vector2 center, float radius, Color color, int segments = 24)
        {
            int i0 = _v.Count;
            _v.Add(center); _c.Add(color);
            for (int s = 0; s <= segments; s++)
            {
                float a = s * Mathf.PI * 2f / segments;
                _v.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                _c.Add(color);
                if (s > 0) { _tris.Add(i0); _tris.Add(i0 + s + 1); _tris.Add(i0 + s); }
            }
        }

        public void Circle(Vector2 center, float radius, Color color, float pixels = 1.5f, int segments = 32)
        {
            Vector2 prev = center + new Vector2(radius, 0f);
            for (int s = 1; s <= segments; s++)
            {
                float a = s * Mathf.PI * 2f / segments;
                Vector2 p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                Thick(prev, p, color, pixels);
                prev = p;
            }
        }

        public void Arrow(Vector2 from, Vector2 to, Color color, float pixels = 2.5f)
        {
            Vector2 d = to - from;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            Vector2 u = d / len;
            float head = Mathf.Min(len * 0.4f, 12f * Pixel);
            Thick(from, to - u * head * 0.6f, color, pixels);
            Vector2 n = new Vector2(-u.y, u.x);
            int i = _v.Count;
            _v.Add(to); _v.Add(to - u * head + n * head * 0.55f); _v.Add(to - u * head - n * head * 0.55f);
            _c.Add(color); _c.Add(color); _c.Add(color);
            _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2);
        }

        public void Dashed(Vector2 a, Vector2 b, Color color, float dashPixels = 8f, float pixels = 1.5f)
        {
            float len = Vector2.Distance(a, b);
            float dash = Mathf.Max(1e-3f, dashPixels * Pixel);
            int n = Mathf.Min(400, Mathf.CeilToInt(len / dash));
            for (int i = 0; i < n; i += 2)
            {
                float t0 = i * dash / len, t1 = Mathf.Min(1f, (i + 1) * dash / len);
                Thick(Vector2.Lerp(a, b, t0), Vector2.Lerp(a, b, t1), color, pixels);
            }
        }

        public void Apply()
        {
            _mesh.Clear();
            if (_v.Count == 0) return;
            _mesh.indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            _mesh.SetVertices(_v);
            _mesh.SetColors(_c);
            _mesh.subMeshCount = 2;
            _mesh.SetIndices(_lines, MeshTopology.Lines, 0, false);
            _mesh.SetIndices(_tris, MeshTopology.Triangles, 1, false);
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(100000f, 100000f, 10f));
        }
    }
}
