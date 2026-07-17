using System.Collections;
using UnityEngine;
// Unity.VisualScripting not available in Luna build

namespace Pools
{
    /// <summary>
    /// Extension methods cho Pool system
    /// Cho phép gọi prefab.Spawn() thay vì PoolSystem.Spawn(prefab)
    /// </summary>
    public static class PoolExtensions
    {
        /// <summary>
        /// Spawn instance từ prefab
        /// FIX: Sửa lại tham số (4 tham số) để khớp với PoolSystem.Spawn và thêm ràng buộc MonoBehaviour, IPoolable
        /// </summary>
        public static T Spawn<T>(this T prefab, Vector3 position = default, Quaternion rotation = default, Transform parent = null)
            where T : Component
        {
            // Gọi đúng hàm Spawn có 4 tham số trong PoolSystem.cs
            return PoolSystem.Spawn(prefab, position, rotation, parent);
        }

        /// <summary>
        /// Spawn instance làm con của parent (Local Position = 0)
        /// </summary>
        public static T Spawn<T>(this T prefab, Transform parent) where T : Component
        {
            return PoolSystem.Spawn(prefab, parent);
        }

        /// <summary>
        /// Despawn instance ngay lập tức
        /// FIX: Thêm ràng buộc IPoolable để PoolSystem.Despawn chấp nhận instance
        /// </summary>
        public static void Despawn<T>(this T instance) where T : Component
        {
            if (instance == null) return;
            PoolSystem.Despawn(instance);
        }

        /// <summary>
        /// Despawn instance sau delay
        /// FIX: Thay đổi ràng buộc thành MonoBehaviour để có thể gọi StartCoroutine trực tiếp từ instance
        /// </summary>
        public static void Despawn<T>(this T instance, float delay) where T : MonoBehaviour
        {
            if (instance == null) return;

            if (delay <= 0f)
            {
                PoolSystem.Despawn(instance);
                return;
            }

            // Vì PoolSystem là class static và không có Instance, 
            // ta sử dụng chính instance (nếu đang active) để chạy Coroutine này.
            if (instance.gameObject.activeInHierarchy)
            {
                instance.StartCoroutine(DespawnDelayed(instance, delay));
            }
        }


        public static GameObject Spawn(this GameObject prefab, Vector3 position = default, Quaternion rotation = default, Transform parent = null)
        {
            var tr = PoolSystem.Spawn(prefab.transform, position, rotation, parent);
            return tr != null ? tr.gameObject : null;
        }

        public static GameObject Spawn(this GameObject prefab, Transform parent)
        {
            var tr = PoolSystem.Spawn(prefab.transform, parent);
            return tr != null ? tr.gameObject : null;
        }

        public static void Despawn(this GameObject instance)
        {
            if (instance == null) return;
            PoolSystem.Despawn(instance.transform);
        }

        public static void Despawn(this GameObject instance, float delay)
        {
            if (instance == null) return;
            if (delay <= 0f) { PoolSystem.Despawn(instance.transform); return; }
            if (instance.activeInHierarchy)
            {
                var mono = instance.GetComponent<MonoBehaviour>();
                if (mono != null) mono.StartCoroutine(DespawnDelayed(instance.transform, delay));
            }
        }

        private static IEnumerator DespawnDelayed(Component instance, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (instance != null)
            {
                PoolSystem.Despawn(instance);
            }
        }
    }
}