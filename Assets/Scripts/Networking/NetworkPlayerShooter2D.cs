using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TwoVsTwoShooter.Networking
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkPlayerShooter2D : NetworkBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float interpolationSpeed = 18f;

        [Header("Combat")]
        [SerializeField] private NetworkProjectile2D projectilePrefab;
        [SerializeField] private Transform muzzle;
        [SerializeField] private float fireCooldown = 0.18f;
        [SerializeField] private int maxHealth = 100;

        [Header("Visuals")]
        [SerializeField] private Color team0Color = new Color(0.2f, 0.55f, 1f);
        [SerializeField] private Color team1Color = new Color(1f, 0.28f, 0.22f);
        [SerializeField] private Vector3 healthBarOffset = new Vector3(0f, 0.8f, 0f);
        [SerializeField] private Vector2 healthBarSize = new Vector2(0.9f, 0.12f);
        [SerializeField] private int visualSortingOrder = 5;

        private static Sprite squareSprite;

        private readonly NetworkVariable<Vector2> networkPosition = new NetworkVariable<Vector2>(
            Vector2.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> health = new NetworkVariable<int>(
            100,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> team = new NetworkVariable<int>(
            -1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private Vector2 serverMoveInput;
        private Vector2 serverAimWorld;
        private bool serverFireHeld;
        private float nextServerFireTime;
        private SpriteRenderer bodyRenderer;
        private Transform healthBarRoot;
        private SpriteRenderer healthFillRenderer;
        private SpriteRenderer healthBackRenderer;

        public int Team => team.Value;
        public int Health => health.Value;
        public bool IsAlive => health.Value > 0;

        public override void OnNetworkSpawn()
        {
            EnsureVisuals();
            team.OnValueChanged += OnTeamChanged;
            health.OnValueChanged += OnHealthChanged;

            if (IsServer)
            {
                team.Value = (int)(OwnerClientId % 2);
                health.Value = maxHealth;
                networkPosition.Value = transform.position;
            }

            ApplyTeamVisuals(team.Value);
            UpdateHealthBar();
        }

        public override void OnNetworkDespawn()
        {
            team.OnValueChanged -= OnTeamChanged;
            health.OnValueChanged -= OnHealthChanged;
        }

        private void Update()
        {
            if (IsOwner)
            {
                Vector2 moveInput = ReadMoveInput();
                Vector2 aimWorld = ReadAimWorldPosition();
                bool fireHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;

                SubmitInputServerRpc(moveInput, aimWorld, fireHeld);

                if (!IsServer)
                {
                    transform.position += (Vector3)(moveInput * moveSpeed * Time.deltaTime);
                }
            }

            if (!IsServer)
            {
                Vector2 current = transform.position;
                Vector2 target = networkPosition.Value;
                transform.position = Vector2.Lerp(current, target, interpolationSpeed * Time.deltaTime);
            }
        }

        private void FixedUpdate()
        {
            if (!IsServer || !IsAlive)
            {
                return;
            }

            Vector2 current = transform.position;
            Vector2 next = current + serverMoveInput * moveSpeed * Time.fixedDeltaTime;
            transform.position = next;
            networkPosition.Value = next;

            if (serverFireHeld && Time.time >= nextServerFireTime)
            {
                FireProjectile();
                nextServerFireTime = Time.time + fireCooldown;
            }
        }

        public void TakeDamage(int amount)
        {
            if (!IsServer || !IsAlive)
            {
                return;
            }

            health.Value = Mathf.Max(0, health.Value - amount);

            if (health.Value == 0)
            {
                Respawn();
            }
        }

        private void Respawn()
        {
            Vector2 spawnPosition = Team == 0 ? new Vector2(-4f, 0f) : new Vector2(4f, 0f);
            transform.position = spawnPosition;
            networkPosition.Value = spawnPosition;
            health.Value = maxHealth;
        }

        private void EnsureVisuals()
        {
            EnsureSquareSprite();

            if (bodyRenderer == null)
            {
                bodyRenderer = GetComponent<SpriteRenderer>();
            }

            if (bodyRenderer == null)
            {
                bodyRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            if (bodyRenderer == null)
            {
                bodyRenderer = gameObject.AddComponent<SpriteRenderer>();
                bodyRenderer.sprite = squareSprite;
                bodyRenderer.sortingOrder = visualSortingOrder;
                transform.localScale = new Vector3(0.6f, 0.6f, 1f);
            }

            bodyRenderer.sortingOrder = Mathf.Max(bodyRenderer.sortingOrder, visualSortingOrder);

            if (healthBarRoot == null)
            {
                CreateHealthBar();
            }
        }

        private void CreateHealthBar()
        {
            healthBarRoot = new GameObject("Health Bar").transform;
            healthBarRoot.SetParent(transform, false);
            healthBarRoot.localPosition = healthBarOffset;

            healthBackRenderer = CreateBarRenderer("Back", new Color(0.05f, 0.05f, 0.05f, 0.85f), -0.01f);
            healthBackRenderer.transform.localScale = new Vector3(healthBarSize.x, healthBarSize.y, 1f);

            healthFillRenderer = CreateBarRenderer("Fill", Color.green, -0.02f);
            healthFillRenderer.transform.localScale = new Vector3(healthBarSize.x, healthBarSize.y * 0.65f, 1f);
        }

        private SpriteRenderer CreateBarRenderer(string objectName, Color color, float zOffset)
        {
            GameObject segment = new GameObject(objectName);
            segment.transform.SetParent(healthBarRoot, false);
            segment.transform.localPosition = new Vector3(0f, 0f, zOffset);

            SpriteRenderer renderer = segment.AddComponent<SpriteRenderer>();
            renderer.sprite = squareSprite;
            renderer.color = color;
            renderer.sortingOrder = visualSortingOrder + 10;
            return renderer;
        }

        private void OnTeamChanged(int previousValue, int newValue)
        {
            ApplyTeamVisuals(newValue);
        }

        private void OnHealthChanged(int previousValue, int newValue)
        {
            UpdateHealthBar();
        }

        private void ApplyTeamVisuals(int teamValue)
        {
            EnsureVisuals();

            Color teamColor = teamValue == 1 ? team1Color : team0Color;
            bodyRenderer.color = teamColor;

            if (healthFillRenderer != null)
            {
                healthFillRenderer.color = Color.Lerp(teamColor, Color.white, 0.25f);
            }
        }

        private void UpdateHealthBar()
        {
            EnsureVisuals();

            if (healthFillRenderer == null)
            {
                return;
            }

            float healthPercent = maxHealth <= 0 ? 0f : Mathf.Clamp01((float)health.Value / maxHealth);
            float fillWidth = healthBarSize.x * healthPercent;
            healthFillRenderer.transform.localScale = new Vector3(fillWidth, healthBarSize.y * 0.65f, 1f);
            healthFillRenderer.transform.localPosition = new Vector3(-(healthBarSize.x - fillWidth) * 0.5f, 0f, -0.02f);

            if (healthBackRenderer != null)
            {
                healthBackRenderer.enabled = IsAlive;
            }

            healthFillRenderer.enabled = IsAlive;
        }

        private static void EnsureSquareSprite()
        {
            if (squareSprite != null)
            {
                return;
            }

            Texture2D texture = new Texture2D(1, 1)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();

            squareSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            squareSprite.hideFlags = HideFlags.HideAndDontSave;
        }

        private void FireProjectile()
        {
            NetworkProjectile2D prefab = projectilePrefab != null ? projectilePrefab : FindRegisteredProjectilePrefab();

            if (prefab == null)
            {
                Debug.LogWarning(
                    "Projectile prefab is not assigned. Create a projectile prefab with NetworkObject + NetworkProjectile2D, " +
                    "register it in NetworkManager Network Prefabs, then assign it to the player's Projectile Prefab field.");
                return;
            }

            Vector2 origin = muzzle != null ? muzzle.position : transform.position;
            Vector2 direction = (serverAimWorld - origin).normalized;

            if (direction.sqrMagnitude < 0.01f)
            {
                direction = Vector2.right;
            }

            NetworkProjectile2D projectile = Instantiate(prefab, origin, Quaternion.identity);
            projectile.Initialize(OwnerClientId, Team, direction);
            projectile.NetworkObject.Spawn();
        }

        private NetworkProjectile2D FindRegisteredProjectilePrefab()
        {
            NetworkManager manager = NetworkManager.Singleton;

            if (manager == null || manager.NetworkConfig == null || manager.NetworkConfig.Prefabs == null)
            {
                return null;
            }

            foreach (NetworkPrefab networkPrefab in manager.NetworkConfig.Prefabs.Prefabs)
            {
                if (networkPrefab?.Prefab == null)
                {
                    continue;
                }

                NetworkProjectile2D registeredProjectile = networkPrefab.Prefab.GetComponent<NetworkProjectile2D>();
                if (registeredProjectile != null)
                {
                    projectilePrefab = registeredProjectile;
                    return registeredProjectile;
                }
            }

            foreach (NetworkPrefabsList prefabsList in manager.NetworkConfig.Prefabs.NetworkPrefabsLists)
            {
                if (prefabsList == null)
                {
                    continue;
                }

                foreach (NetworkPrefab networkPrefab in prefabsList.PrefabList)
                {
                    if (networkPrefab?.Prefab == null)
                    {
                        continue;
                    }

                    NetworkProjectile2D registeredProjectile = networkPrefab.Prefab.GetComponent<NetworkProjectile2D>();
                    if (registeredProjectile != null)
                    {
                        projectilePrefab = registeredProjectile;
                        return registeredProjectile;
                    }
                }
            }

            return null;
        }

        private Vector2 ReadMoveInput()
        {
            Vector2 input = Vector2.zero;

            Keyboard keyboard = Keyboard.current;

            if (keyboard == null)
            {
                return Vector2.zero;
            }

            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            {
                input.x -= 1f;
            }

            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            {
                input.x += 1f;
            }

            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            {
                input.y -= 1f;
            }

            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            {
                input.y += 1f;
            }

            return input.sqrMagnitude > 1f ? input.normalized : input;
        }

        private Vector2 ReadAimWorldPosition()
        {
            Camera mainCamera = Camera.main;
            Mouse mouse = Mouse.current;

            if (mainCamera == null || mouse == null)
            {
                return transform.position + Vector3.right;
            }

            Vector3 mousePosition = mouse.position.ReadValue();
            mousePosition.z = -mainCamera.transform.position.z;
            return mainCamera.ScreenToWorldPoint(mousePosition);
        }

        [ServerRpc]
        private void SubmitInputServerRpc(Vector2 moveInput, Vector2 aimWorld, bool fireHeld)
        {
            serverMoveInput = moveInput.sqrMagnitude > 1f ? moveInput.normalized : moveInput;
            serverAimWorld = aimWorld;
            serverFireHeld = fireHeld;
        }
    }
}
