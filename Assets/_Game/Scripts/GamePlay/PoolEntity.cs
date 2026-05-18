using Pools;
// Unity.VisualScripting not available in Luna build
using UnityEngine;

namespace GamePlay.Entities
{
    public class PoolEntity : MonoBehaviour, IPoolable
    {
        [SerializeField] protected Transform _transform;

        // --- THÊM DÒNG NÀY ĐỂ FIX LỖI ---
        [SerializeField] protected EntityType _entityType;
        public EntityType EntityType => _entityType;
        // --------------------------------

        public Transform Transform => _transform != null ? _transform : transform;

        protected virtual void Awake()
        {
            if (_transform == null) _transform = transform;
        }

        /// <summary>
        /// IPoolable API
        /// </summary>
        public virtual void New()
        {
            // mặc định không làm gì thêm, PoolSystem sẽ SetActive + set pos/rot
        }

        public virtual void Free()
        {
            // mặc định không làm gì thêm, PoolSystem sẽ SetActive(false)
        }

        /// <summary>
        /// Trả entity về pool.
        /// </summary>
        public void Despawn()
        {
            PoolSystem.Despawn(this);
        }

        // Backward compatibility
        [System.Obsolete("Use New() instead.")]
        public void OnSpawn() => New();

        [System.Obsolete("Use Free() instead.")]
        public void OnDespawn() => Free();
    }
}