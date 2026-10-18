using UnityEngine;
using Metadream.Shoot.Combat;

namespace Metadream.Shoot.Network
{
    /// <summary>Client-side bullet drawn from host snapshots and extrapolated ballistically between them.</summary>
    public sealed class ProjectileProxy : MonoBehaviour
    {
        private Vector2 _pos;
        private Vector2 _vel;
        private float _gravity;
        private float _time;

        public static ProjectileProxy Create(in ProjectileState s, Color color)
        {
            var go = new GameObject("ProjectileProxy");
            go.transform.position = s.Position;
            var p = go.AddComponent<ProjectileProxy>();
            Projectile.BuildVisual(go, color, s.Radius);
            p.Set(s);
            return p;
        }

        public void Set(in ProjectileState s)
        {
            _pos = s.Position;
            _vel = s.Velocity;
            _gravity = s.Gravity;
            _time = Time.unscaledTime;
        }

        private void Update()
        {
            float age = Mathf.Min(Time.unscaledTime - _time, 0.15f) * Time.timeScale;
            transform.position = _pos + _vel * age + Vector2.down * (0.5f * _gravity * age * age);
        }
    }
}

