using UnityEngine;
using GamePlay.ComponentSystems;

namespace GamePlay.Effects
{
    public class BlockDebrisController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private HitComponent hitComponent;
        [SerializeField] private DebrisBlock debrisPrefab;

        [Header("Spawn Settings")]
        [SerializeField] private int minBlocks = 5;
        [SerializeField] private int maxBlocks = 10;

        [Header("Physics Settings")]
        [SerializeField] private float explosionForce = 5f;
        [SerializeField] private float upwardForce = 1f;
        [SerializeField] private float lifetime = 1f;

        [Header("Block Settings")]
        [SerializeField] private Vector2 blockScaleRange = new Vector2(0.1f, 0.3f);

        public Color BaseColor;
        public void TriggerDebrisEffect()
        {
            if (hitComponent == null || debrisPrefab == null)
            {
                Debug.LogWarning("BlockDebrisController: Missing references!");
                return;
            }

            Vector3 center = hitComponent.Position;
            int blockCount = Random.Range(minBlocks, maxBlocks + 1);

            for (int i = 0; i < blockCount; i++)
            {
                SpawnDebrisBlock(center);
            }
        }

        private void SpawnDebrisBlock(Vector3 center)
        {
            // Get random position inside the collision shape
            Vector3 randomPos = GetRandomPositionInShape(center);

            // Instantiate block
            var blockObj = PoolManager.Instance.Get(debrisPrefab);
            blockObj.transform.SetParent(PoolManager.Instance.transform);
            blockObj.transform.SetPositionAndRotation(randomPos, Random.rotation);
            blockObj.gameObject.SetActive(true);

            // Random scale
            float scale = Random.Range(blockScaleRange.x, blockScaleRange.y);
            blockObj.transform.localScale = Vector3.one * scale;

            // Calculate explosion direction (from center outward)
            Vector3 direction = (randomPos - center).normalized;
            if (direction == Vector3.zero)
            {
                direction = Random.onUnitSphere;
            }

            // Calculate initial velocity
            Vector3 velocity = direction * explosionForce;
            velocity.y += upwardForce;

            // Initialize debris block
            blockObj.SetColor(BaseColor);
            blockObj.Initialize(velocity, lifetime);
        }

        private Vector3 GetRandomPositionInShape(Vector3 center)
        {
            ShapeType shapeType = hitComponent.shapeType;
            Vector3 colliderSize = hitComponent.colliderSize;

            switch (shapeType)
            {
                case ShapeType.Sphere:
                    return GetRandomPositionInSphere(center, colliderSize);

                case ShapeType.Box:
                    return GetRandomPositionInBox(center, colliderSize);

                case ShapeType.Cylinder:
                    return GetRandomPositionInCylinder(center, colliderSize);

                default:
                    return center;
            }
        }

        private Vector3 GetRandomPositionInSphere(Vector3 center, Vector3 size)
        {
            // size.x là diameter trong HitComponent -> radius = size.x * 0.5f
            float radius = size.x * 0.5f;
            // size.y là offset của tâm sphere
            Vector3 sphereCenter = center + new Vector3(0, size.y, 0);

            // Random point inside sphere
            Vector3 randomPoint = Random.insideUnitSphere * radius;
            return sphereCenter + randomPoint;
        }

        private Vector3 GetRandomPositionInBox(Vector3 center, Vector3 size)
        {
            // colliderSize là full size trong HitComponent
            // Tâm box = center + Vector3.up * size.y
            Vector3 boxCenter = center + new Vector3(0, size.y, 0);

            // Random point inside box (size là full size, không phải half-extents)
            Vector3 randomPoint = new Vector3(
                Random.Range(-size.x, size.x),
                Random.Range(-size.y, size.y),
                Random.Range(-size.z, size.z)
            );

            return boxCenter + randomPoint;
        }

        private Vector3 GetRandomPositionInCylinder(Vector3 center, Vector3 size)
        {
            // size.x là radius, size.y là half height trong HitComponent
            float radius = size.x;
            float halfHeight = size.y;
            Vector3 cylinderCenter = center + new Vector3(0, halfHeight, 0);

            // Random point inside cylinder
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float r = Mathf.Sqrt(Random.Range(0f, 1f)) * radius;
            float x = r * Mathf.Cos(angle);
            float z = r * Mathf.Sin(angle);
            float y = Random.Range(-halfHeight, halfHeight);

            return cylinderCenter + new Vector3(x, y, z);
        }

#if UNITY_EDITOR
        [ContextMenu("Test Debris Effect")]
        private void TestDebrisEffect()
        {
            TriggerDebrisEffect();
        }
#endif
    }
}
