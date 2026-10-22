using UnityEngine;

namespace Metadream.Shoot.Presentation
{
    /// <summary>Orthographic camera framing the observed ±35.56 × ±20 arena, with additive shake.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        public static CameraRig Instance { get; private set; }
        public const float OrthographicSize = 20f;
        /// <summary>Replay zoom (1 = whole arena) and the world point it centres on.</summary>
        public static float Zoom = 1f;
        public static Vector2 Focus;
        private float _trauma;
        private Camera _cam;

        private void Awake()
        {
            Instance = this;
            _cam = GetComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = OrthographicSize;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Palette.Background;
            transform.position = new Vector3(0f, 0f, -10f);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void AddShake(float amount) => _trauma = Mathf.Clamp01(_trauma + amount);

        private void LateUpdate()
        {
            // Keep the full 71.12 x 40 arena visible on non-16:9 windows.
            float aspect = Mathf.Max(0.1f, _cam.aspect);
            _cam.orthographicSize = Mathf.Max(OrthographicSize, 35.56f / aspect) / Mathf.Max(1f, Zoom);
            _trauma = Mathf.Max(0f, _trauma - Time.unscaledDeltaTime * 1.8f);
            float s = _trauma * _trauma * 0.9f;
            Vector2 c = Zoom > 1f ? Focus : Vector2.zero;
            transform.position = new Vector3(c.x + Random.Range(-s, s), c.y + Random.Range(-s, s), -10f);
        }
    }
}
