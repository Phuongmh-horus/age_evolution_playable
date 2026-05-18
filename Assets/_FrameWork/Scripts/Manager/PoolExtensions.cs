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
            where T : MonoBehaviour, IPoolable
        {
            // Gọi đúng hàm Spawn có 4 tham số trong PoolSystem.cs
            return PoolSystem.Spawn(prefab, position, rotation, parent);
        }

        /// <summary>
        /// Spawn instance làm con của parent (Local Position = 0)
        /// </summary>
        public static T Spawn<T>(this T prefab, Transform parent) where T : MonoBehaviour, IPoolable
        {
            return PoolSystem.Spawn(prefab, parent);
        }

        /// <summary>
        /// Despawn instance ngay lập tức
        /// FIX: Thêm ràng buộc IPoolable để PoolSystem.Despawn chấp nhận instance
        /// </summary>
        public static void Despawn<T>(this T instance) where T : MonoBehaviour, IPoolable
        {
            if (instance == null) return;
            PoolSystem.Despawn(instance);
        }

        /// <summary>
        /// Despawn instance sau delay
        /// FIX: Thay đổi ràng buộc thành MonoBehaviour để có thể gọi StartCoroutine trực tiếp từ instance
        /// </summary>
        public static void Despawn<T>(this T instance, float delay) where T : MonoBehaviour, IPoolable
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

        private static IEnumerator DespawnDelayed<T>(T instance, float delay) where T : MonoBehaviour, IPoolable
        {
            yield return new WaitForSeconds(delay);

            if (instance != null)
            {
                PoolSystem.Despawn(instance);
            }
        }
    }
}