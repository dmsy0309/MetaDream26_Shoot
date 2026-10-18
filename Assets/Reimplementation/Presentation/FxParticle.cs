using System.Collections.Generic;
using UnityEngine;

namespace Metadream.Shoot.Presentation
{
    /// <summary>Short-lived particle used by all effects. Registered so transitions can clear them.</summary>
    public sealed class FxParticle : MonoBehaviour
    {
        public static readonly List<FxParticle> Active = new List<FxParticle>();
        public Vector2 velocity;
        public float gravity;
        public float life = 0.6f;
        public float grow;
        private float _age;
        private SpriteRenderer _sr;
        private Color _color;
        private Vector3 _scale;

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        public void Init(Sprite sprite, Color color, float scale, int order)
        {
            _sr = gameObject.AddComponent<SpriteRenderer>();
            _sr.sprite = sprite;
            _sr.sharedMaterial = SpriteFactory.Material;
            _sr.color = color;
            _sr.sortingOrder = order;
            _color = color;
            _scale = Vector3.one * scale;
            transform.localScale = _scale;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            velocity += Vector2.down * gravity * dt;
            velocity *= 1f - Mathf.Clamp01(dt * 2.5f);
            transform.position += (Vector3)(velocity * dt);
            float k = Mathf.Clamp01(_age / life);
            transform.localScale = _scale * (1f + grow * k);
            var c = _color;
            c.a *= 1f - k;
            _sr.color = c;
            if (_age >= life) Destroy(gameObject);
        }
    }
}
