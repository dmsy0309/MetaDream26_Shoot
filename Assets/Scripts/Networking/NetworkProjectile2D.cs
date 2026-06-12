using Unity.Netcode;
using UnityEngine;

namespace TwoVsTwoShooter.Networking
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkProjectile2D : NetworkBehaviour
    {
        [SerializeField] private float speed = 14f;
        [SerializeField] private float lifetime = 2f;
        [SerializeField] private float maxTravelDistance = 28f;
        [SerializeField] private float collisionRadius = 0.18f;
        [SerializeField] private int damage = 25;
        [SerializeField] private LayerMask hitMask = ~0;

        private readonly NetworkVariable<Vector2> networkPosition = new NetworkVariable<Vector2>(
            Vector2.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private ulong ownerClientId;
        private int ownerTeam = -1;
        private Vector2 direction = Vector2.right;
        private Vector2 spawnPosition;
        private float despawnTime;
        private bool isDespawning;

        public void Initialize(ulong shooterClientId, int shooterTeam, Vector2 shotDirection)
        {
            ownerClientId = shooterClientId;
            ownerTeam = shooterTeam;
            direction = shotDirection.sqrMagnitude > 0.01f ? shotDirection.normalized : Vector2.right;
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                spawnPosition = transform.position;
                despawnTime = Time.time + lifetime;
                networkPosition.Value = transform.position;
            }
        }

        private void Update()
        {
            if (IsServer)
            {
                return;
            }

            transform.position = Vector2.Lerp(transform.position, networkPosition.Value, 18f * Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (!IsServer)
            {
                return;
            }

            if (isDespawning)
            {
                return;
            }

            if (direction.sqrMagnitude < 0.01f)
            {
                DespawnProjectile();
                return;
            }

            transform.position += (Vector3)(direction * speed * Time.fixedDeltaTime);
            networkPosition.Value = transform.position;

            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, collisionRadius, hitMask);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].transform.IsChildOf(transform))
                {
                    continue;
                }

                NetworkPlayerShooter2D player = hits[i].GetComponentInParent<NetworkPlayerShooter2D>();

                if (player != null)
                {
                    if (player.OwnerClientId == ownerClientId || player.Team == ownerTeam)
                    {
                        continue;
                    }

                    player.TakeDamage(damage);
                    DespawnProjectile();
                    return;
                }

                if (!hits[i].isTrigger)
                {
                    DespawnProjectile();
                    return;
                }
            }

            float traveledDistance = Vector2.Distance(spawnPosition, transform.position);
            if (Time.time >= despawnTime || traveledDistance >= maxTravelDistance)
            {
                DespawnProjectile();
            }
        }

        private void DespawnProjectile()
        {
            if (isDespawning)
            {
                return;
            }

            isDespawning = true;
            HideProjectileClientRpc();
            HideLocalVisuals();

            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn(true);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        [ClientRpc]
        private void HideProjectileClientRpc()
        {
            HideLocalVisuals();
        }

        private void HideLocalVisuals()
        {
            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = false;
            }

            Collider2D[] colliders = GetComponentsInChildren<Collider2D>();
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }
        }
    }
}
