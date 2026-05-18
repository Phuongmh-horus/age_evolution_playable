using System;
using GamePlay.Entities;
using UnityEngine;

namespace GamePlay.ComponentSystems
{
    public interface IHitable : IComponent
    {
        event Action<IAttacker> OnHitComplete;
        bool IsActive { get; }
        Vector3 Position { get; }
        EntityType EntityType { get; }
        ColliderData GetColliderData();
        void OnHit(IAttacker source);
    }

    public struct ColliderData
    {
        public ShapeType Type;

        // Sphere: x=Radius, y=CenterOffsetY (tính từ feet), z=0
        // Box: xyz=HalfExtents
        // Cylinder: x=Radius, y=HalfHeight, z=0
        public Vector3 Size;

        // Offset thêm (giữ lại để tương thích logic cũ nếu chỗ khác đang dùng)
        public float Offset;

        public uint CategoryBits;
    }

    public enum ShapeType : byte
    {
        Sphere,
        Box,
        Cylinder
    }
}
