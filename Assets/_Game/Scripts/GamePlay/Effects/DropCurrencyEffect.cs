
using GamePlay.CollisionSystems;
using UnityEngine;
using GamePlay.ComponentSystems;
using Pools;

namespace GamePlay.Effects
{
    public class DropCurrencyEffect : MonoBehaviour, IComponent
    {
        [Header("References")]
        [SerializeField] private CurrencyDropItem currencyDropItemPrefab;

        [Header("Spawn Settings")]
        [SerializeField] private int minCurrencyItems = 1;
        [SerializeField] private int maxCurrencyItems = 1;

        public Vector2 CurrencyValue;

        [Header("Physics Settings")]
        [SerializeField] private float explosionForce = 5f;
        [SerializeField] private float upwardForce = 3f;

        [Header("Spawn Area Settings")]
        [SerializeField] private float spawnRadius = 1f;

        [SerializeField] private bool flyUp = true;

        /// <summary>
        /// Spawn currency items với số lượng xác định tại vị trí center
        /// </summary>
        /// <param name="center">Vị trí spawn</param>
        /// <param name="count">Số lượng currency items cần spawn</param>
        public void SpawnCurrency(Vector3 center, int count)
        {
            if (currencyDropItemPrefab == null)
            {
                Debug.LogWarning("DropCurrencyEffect: currencyDropItemPrefab is null!");
                return;
            }

            // Reference flow: pick total value once, split across items
            float totalValue = Random.Range(CurrencyValue.x, CurrencyValue.y);
            float valuePerItem = count > 0 ? totalValue / count : 0f;

            for (int i = 0; i < count; i++)
            {
                SpawnCurrencyItem(center, valuePerItem);
            }
        }

        /// <summary>
        /// Spawn a single currency item at an exact position.
        /// </summary>
        public void SpawnCurrencyAt(Vector3 position, float value)
        {
            if (currencyDropItemPrefab == null)
            {
                Debug.LogWarning("DropCurrencyEffect: currencyDropItemPrefab is null!");
                return;
            }

            SpawnCurrencyItem(position, value, useRandomPosition: false);
        }

        /// <summary>
        /// Spawn currency items với số lượng ngẫu nhiên tại vị trí center
        /// </summary>
        /// <param name="center">Vị trí spawn</param>
        public void SpawnCurrency(Vector3 center)
        {
            int count = Random.Range(minCurrencyItems, maxCurrencyItems + 1);
            SpawnCurrency(center, 3);
        }

        private void SpawnCurrencyItem(Vector3 center, float value, bool useRandomPosition = true)
        {
            transform.localScale = Vector3.one;
            // Get random position inside spawn radius
            Vector3 randomPos = useRandomPosition ? GetRandomPositionInRadius(center) : center;

            // Instantiate currency item from pool
            var itemObj = currencyDropItemPrefab.Spawn();

            itemObj.Initialize();
            CollisionSystem.Register(itemObj.Pack.Hitable, itemObj.transform);

            itemObj.transform.SetPositionAndRotation(randomPos, Random.rotation);
            itemObj.gameObject.SetActive(true);

            // Calculate explosion direction (from center outward)
            Vector3 direction = (randomPos - center).normalized;
            if (direction == Vector3.zero)
            {
                direction = Random.onUnitSphere;
            }

            // Calculate initial velocity
            Vector3 velocity = direction * explosionForce;
            velocity.y += upwardForce;

            itemObj.Initialize(velocity, value, flyUp);
        }

        private Vector3 GetRandomPositionInRadius(Vector3 center)
        {
            // Random point inside a sphere with spawnRadius
            Vector3 randomPoint = Random.insideUnitSphere * spawnRadius;
            var result = center + randomPoint;
            result.y = Mathf.Max(result.y, 2f);
            return result;
        }

        public void SpawnItem()
        {
            SpawnCurrency(transform.position);
        }

#if UNITY_EDITOR
        [ContextMenu("Test Spawn Currency (Random Count)")]
        private void TestSpawnCurrencyRandom()
        {
            SpawnCurrency(transform.position);
        }

        [ContextMenu("Test Spawn Currency (10 items)")]
        private void TestSpawnCurrency10()
        {
            SpawnCurrency(transform.position, 10);
        }
#endif
        public void Initialize()
        {

        }

        public Transform Transform => transform;

        public void Dispose()
        {

        }
    }
}
