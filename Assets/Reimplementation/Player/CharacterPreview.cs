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

        private CharacterFace _face;
        private SpriteRenderer _body, _outline, _barrel;
        private Transform _barrelPivot;
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
                p._body.color = color;
                p._outline.color = Color.Lerp(color, Color.white, 0.6f);
                p._barrel.color = Color.Lerp(color, Color.black, 0.45f);
                p._face.SetBodyColor(color);
            }
            if (p._face.Look != look.Sanitized) p._face.Apply(look, true);
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
            _body = Sprite(model, "Body", Presentation.SpriteFactory.Circle, 1f, 10, color);
            _outline = Sprite(model, "Outline", Presentation.SpriteFactory.Circle, 1.14f, 9, Color.Lerp(color, Color.white, 0.6f));
            var shine = Sprite(model, "Shine", Presentation.SpriteFactory.Circle, 0.34f, 11, new Color(1f, 1f, 1f, 0.32f));
            shine.transform.localPosition = new Vector3(-0.17f, 0.2f, 0f);
            _barrelPivot = new GameObject("Barrel").transform;
            _barrelPivot.SetParent(model, false);
            _barrel = Sprite(_barrelPivot, "BarrelSprite", Presentation.SpriteFactory.Square, 1f, 11, Color.Lerp(color, Color.black, 0.45f));
            _barrel.transform.localPosition = new Vector3(0.55f, 0f, 0f);
            _barrel.transform.localScale = new Vector3(0.5f, 0.2f, 1f);
            _face = new CharacterFace(model, color);

            _texture = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32) { name = "CharacterPreviewRT", antiAliasing = 4 };
            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0f, 0.18f, -10f);
            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 1.25f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.13f, 0.14f, 0.19f, 1f);
            _camera.targetTexture = _texture;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 50f;
        }

        private static SpriteRenderer Sprite(Transform parent, string name, Sprite sprite, float scale, int order, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = Presentation.SpriteFactory.Material;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        private void Update()
        {
            _barrelPivot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(_aim.y, _aim.x) * Mathf.Rad2Deg);
            _face.Tick(_aim, false);
            _face.TintCovers(_body.color);
        }

        private void OnDestroy()
        {
            if (_camera != null) _camera.targetTexture = null;
            if (_texture != null) { _texture.Release(); Destroy(_texture); }
            if (_instance == this) _instance = null;
        }
    }
}

