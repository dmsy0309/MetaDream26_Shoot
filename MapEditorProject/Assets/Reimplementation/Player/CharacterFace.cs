using System.Collections.Generic;
using UnityEngine;

namespace Metadream.Shoot.Player
{
    /// <summary>Extracted face parts shared by in-game fighters and the customization preview.</summary>
    public sealed class CharacterFace
    {
        private readonly Transform _root;
        private readonly List<GameObject> _parts = new List<GameObject>();
        private bool _visible = true;
        public CharacterLook Look { get; private set; }

        public CharacterFace(Transform root)
        {
            _root = root;
            Apply(CharacterLook.Default, true);
        }

        public void Apply(CharacterLook look, bool visible)
        {
            Look = look.Sanitized;
            _visible = visible;
            foreach (var go in _parts)
            {
                if (go == null) continue;
                if (Application.isPlaying) Object.Destroy(go);
                else Object.DestroyImmediate(go);
            }
            _parts.Clear();
            Add(1, Look.Expression, Look.EyeOffset);
            Add(3, Look.Mouth, Look.MouthOffset);
            Add(2, Look.Accessory, Look.AccessoryOffset);
            Add(4, Look.Accessory2, Look.Accessory2Offset);
        }

        private void Add(int category, int value, Vector2 offset)
        {
            var item = OriginalCharacterArt.Resolve(category, value);
            if (item != null) _parts.Add(OriginalCharacterArt.Create(item, _root, offset, _visible));
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            foreach (var go in _parts)
                if (go != null) foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true)) sr.enabled = visible;
        }
    }
}
