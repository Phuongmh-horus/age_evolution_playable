using GamePlay.Entities;
using UnityEngine;

namespace GamePlay.ComponentSystems
{
    /// <summary>
    /// Base component cho các component trong gameplay
    /// Đã được tối ưu: Xóa Alchemy/KBCore attributes để đảm bảo Luna-safe.
    /// </summary>
    public abstract class BaseComponent : MonoBehaviour, IComponent
    {
        [SerializeField, HideInInspector] public Transform CacheTransform;

        /// <summary>
        /// Trả về vị trí hiện tại dựa trên CacheTransform hoặc transform mặc định.
        /// </summary>
        public Vector3 CachePosition => CacheTransform != null ? CacheTransform.position : transform.position;

        /// <summary>
        /// Trả về góc quay hiện tại dựa trên CacheTransform hoặc transform mặc định.
        /// </summary>
        public Quaternion CacheRotation => CacheTransform != null ? CacheTransform.rotation : transform.rotation;

        [SerializeField] protected PoolEntity poolEntity;

        /// <summary>
        /// EntityType lấy từ PoolEntity gắn kèm. Trả về None nếu không tìm thấy.
        /// </summary>
        public EntityType EntityType
        {
            get
            {
                if (poolEntity == null)
                    poolEntity = GetComponentInParent<PoolEntity>();

                return poolEntity != null ? poolEntity.EntityType : EntityType.None;
            }
        }

        // Implementation of IComponent.Transform
        public Transform Transform => CacheTransform != null ? CacheTransform : transform;

        protected virtual void Awake()
        {
            if (CacheTransform == null) CacheTransform = transform;
        }

        internal void SetPoolEntity(PoolEntity owner)
        {
            if (poolEntity == null)
                poolEntity = owner;
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            // Đảm bảo dữ liệu luôn đúng trong Editor
            if (CacheTransform == null) CacheTransform = transform;
            if (poolEntity == null) poolEntity = GetComponentInParent<PoolEntity>();
        }
#endif

        /// <summary>
        /// Khởi tạo logic cho component. Các lớp con (như AttackComponent) sẽ override lại.
        /// </summary>
        public virtual void Initialize()
        {
            // Mặc định không làm gì, có thể override ở class con
        }

        /// <summary>
        /// Giải phóng hoặc dọn dẹp component khi bị hủy hoặc đưa vào pool.
        /// </summary>
        public virtual void Dispose()
        {
            // Mặc định không làm gì, có thể override ở class con
        }
    }
}
