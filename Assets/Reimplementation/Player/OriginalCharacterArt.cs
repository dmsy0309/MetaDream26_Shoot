using System;
using System.Collections.Generic;
using UnityEngine;
using Metadream.Shoot.Presentation;

namespace Metadream.Shoot.Player
{
    /// <summary>Original sprite rectangles, pivots, part trees and animation coefficients recovered from the supplied build.</summary>
    public static class OriginalCharacterArt
    {
        public const int EyeStart = 8, MouthStart = 8, AccessoryStart = 7;
        public const float FaceScale = 0.16f * 1.05f;
        [Serializable] public sealed class Catalog { public Item[] eyes, mouths, accessories; public SpriteInfo[] sprites; }
        [Serializable] public sealed class Item { public int id, sprite, order; public string name; public float scale, healthBarUp; public Vector2 offset; public Color color; public Node node; }
        [Serializable] public sealed class SpriteInfo { public int id, width, height; public string name; public float ppu; public Vector2 pivot; }
        [Serializable] public sealed class Node { public string name; public Vector3 position, size; public Quaternion rotation; public bool active, enabled, flipX, flipY; public int sprite, order; public Color color; public Node[] children; public Motion[] animations; }
        [Serializable] public sealed class Motion { public string name; public float duration; public Channel[] channels; }
        [Serializable] public sealed class Channel { public string property; public Key[] keys; }
        [Serializable] public sealed class Key { public float time, a, b, c, d; }
        private static Catalog _data;
        private static readonly Dictionary<int, Sprite> Sprites = new Dictionary<int, Sprite>();
        public static Catalog Data => _data ?? (_data = JsonUtility.FromJson<Catalog>(Resources.Load<TextAsset>("CharacterArt/catalog").text));
        public static Item[] Items(int category) => category == 1 ? Data.eyes : category == 3 ? Data.mouths : Data.accessories;
        public static int Start(int category) => category == 1 ? EyeStart : category == 3 ? MouthStart : AccessoryStart;
        public static Item Resolve(int category, int value)
        {
            if (category == 0) return null;
            // Legacy neutral now uses the original neutral face; other saved legacy choices retain their identities.
            int index = (category == 1 || category == 3) && value == 0 ? 0 : value - Start(category);
            var items = Items(category);
            return index >= 0 && index < items.Length ? items[index] : null;
        }
        public static string[] AppendNames(string[] legacy, int category)
        {
            var items = Items(category); var all = new string[legacy.Length + items.Length];
            Array.Copy(legacy, all, legacy.Length);
            for (int i = 0; i < items.Length; i++) all[legacy.Length + i] = "ROUNDS " + (i + 1).ToString("00");
            return all;
        }
        public static Sprite GetSprite(int id)
        {
            if (id == 0) return null;
            if (Sprites.TryGetValue(id, out var cached)) return cached;
            var info = Array.Find(Data.sprites, s => s.id == id);
            var tex = Resources.Load<Texture2D>("CharacterArt/" + id);
            if (info == null || tex == null) throw new InvalidOperationException("Missing original character sprite " + id);
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), info.pivot, info.ppu, 0, SpriteMeshType.FullRect);
            sprite.name = info.name; Sprites[id] = sprite; return sprite;
        }
        public static GameObject Create(Item item, Transform parent, Vector2 offset, bool visible)
        {
            var go = CreateNode(item.node, parent, visible);
            go.transform.localPosition = (item.offset * FaceScale) + offset;
            go.transform.localScale = Vector3.one * (item.scale * FaceScale);
            go.transform.localRotation = Quaternion.identity;
            return go;
        }
        private static GameObject CreateNode(Node node, Transform parent, bool visible)
        {
            var go = new GameObject(node.name); var t = go.transform; t.SetParent(parent, false);
            t.localPosition = new Vector3(node.position.x, node.position.y, 0);
            t.localRotation = node.rotation; t.localScale = node.size;
            if (node.sprite != 0)
            {
                var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = GetSprite(node.sprite);
                sr.sharedMaterial = SpriteFactory.Material; sr.color = node.color; sr.sortingOrder = 12 + node.order;
                sr.flipX = node.flipX; sr.flipY = node.flipY; sr.enabled = visible;
            }
            foreach (var child in node.children) CreateNode(child, t, visible);
            if (node.animations.Length > 0) go.AddComponent<OriginalPartMotion>().Motion = node.animations[0];
            go.SetActive(node.active); return go;
        }
        public static void DrawThumbnail(Rect rect, Item item)
        {
            var matrix = GUI.matrix; var color = GUI.color;
            GUI.matrix = matrix * Matrix4x4.TRS(new Vector3(rect.center.x,rect.center.y,0),Quaternion.identity,new Vector3(rect.width/9f,rect.height/9f,1));
            DrawNode(item.node,true);
            GUI.matrix=matrix;GUI.color=color;
        }
        private static void DrawNode(Node node,bool root=false)
        {
            if (!root && !node.active) return;
            var matrix=GUI.matrix;
            if(!root)
            {
                float angle=node.rotation.eulerAngles.z;
                GUI.matrix=matrix*Matrix4x4.TRS(new Vector3(node.position.x,-node.position.y,0),Quaternion.Euler(0,0,-angle),new Vector3(node.size.x,node.size.y,1));
            }
            if(node.sprite!=0)
            {
                var sprite=GetSprite(node.sprite);float w=sprite.rect.width/sprite.pixelsPerUnit,h=sprite.rect.height/sprite.pixelsPerUnit;
                GUI.color=node.color;
                GUI.DrawTexture(new Rect(-sprite.pivot.x/sprite.pixelsPerUnit,-h+sprite.pivot.y/sprite.pixelsPerUnit,w,h),sprite.texture);
            }
            foreach(var child in node.children)DrawNode(child);
            GUI.matrix=matrix;
        }

        public static float Evaluate(Key[] keys, float time)
        {
            if (keys == null || keys.Length == 0) return 0;
            int index = 0; while (index + 1 < keys.Length && keys[index + 1].time <= time) index++;
            var k = keys[index]; float dt = Mathf.Max(0, time - k.time);
            return ((k.a * dt + k.b) * dt + k.c) * dt + k.d;
        }
    }
    public sealed class OriginalPartMotion : MonoBehaviour
    {
        public OriginalCharacterArt.Motion Motion;
        private SpriteRenderer _sprite;
        private void Awake() => _sprite = GetComponent<SpriteRenderer>();
        private void Update()
        {
            if (Motion == null || Motion.duration <= 0) return;
            float time = Time.unscaledTime % Motion.duration;
            var p = transform.localPosition; var s = transform.localScale;
            Color c = _sprite != null ? _sprite.color : Color.white;
            foreach (var channel in Motion.channels)
            {
                float v = OriginalCharacterArt.Evaluate(channel.keys, time);
                switch (channel.property)
                {
                    case "px": p.x = v; break; case "py": p.y = v; break; // z flattened for the 2D sorting system
                    case "sx": s.x = v; break; case "sy": s.y = v; break; case "sz": s.z = v; break;
                    case "cr": c.r = v; break; case "cg": c.g = v; break; case "cb": c.b = v; break; case "ca": c.a = v; break;
                }
            }
            transform.localPosition = p; transform.localScale = s;
            if (_sprite != null) _sprite.color = c;
        }
    }
}
