using UnityEngine;

namespace Metadream.Shoot.Player
{
    /// <summary>
    /// Main-menu character preview: a body + CharacterFace far outside the arena, rendered by its own camera into a
    /// RenderTexture that the menu draws with IMGUI. Exactly the same face code as in-game fighters.
    /// </summary>
    public sealed class CharacterPreview : MonoBehaviour
    {
        private static readonly Vector3 Origin = new Vector3(10000f, 10000f, 0f);
        private static CharacterPreview _instance;

        private CharacterModel _model;
        private Camera _camera;
        private RenderTexture _texture;
        private Color _color;
        private Vector2 _aim = Vector2.right;

        public static RenderTexture Texture => _instance != null ? _instance._texture : null;

        /// <summary>Creates the preview if needed and updates its look, colour and aim.</summary>
        public static void Show(CharacterLook look, Color color, Vector2 aim)
        {
            if (_instance == null)
            {
                var go = new GameObject("CharacterPreview");
                go.transform.position = Origin;
                _instance = go.AddComponent<CharacterPreview>();
                _instance.Build(color);
            }
            var p = _instance;
            if (p._color != color)
            {
                p._color = color;
                p._model.Body.color = color;
                p._model.Face.SetBodyColor(color);
            }
            if (p._model.Face.Look != look.Sanitized) p._model.Face.Apply(look, true);
            if (aim.sqrMagnitude > 0.0001f) p._aim = aim.normalized;
        }

        public static void Hide()
        {
            if (_instance != null) Destroy(_instance.gameObject);
            _instance = null;
        }

        private void Build(Color color)
        {
            _color = color;
            var model = new GameObject("Model").transform;
            model.SetParent(transform, false);
            model.localScale = Vector3.one * 1.2f;
            _model = new CharacterModel(model, color);

            _texture = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32) { name = "CharacterPreviewRT", antiAliasing = 4 };
            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0.1f, 0.35f, -10f);
            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 1.95f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.13f, 0.14f, 0.19f, 1f);
            _camera.targetTexture = _texture;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 50f;
        }

        private void Update()
        {
            _model.Tick(_aim, Vector2.zero, true, false, 3, 3, false, 1, 1, false, _color, true);
        }

        private void OnDestroy()
        {
            if (_camera != null) _camera.targetTexture = null;
            if (_texture != null) { _texture.Release(); Destroy(_texture); }
            if (_instance == this) _instance = null;
        }
    }
}

