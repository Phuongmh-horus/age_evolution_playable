using System;
using GamePlay.Entities;
using UnityEngine;

namespace GamePlay.ComponentSystems
{
    public class HitComponent : BaseComponent, IHitable
    {
        public event Action<IAttacker> OnHitComplete = delegate { };

        [Header("Collision Settings")]
        public ShapeType shapeType = ShapeType.Sphere;

        // Quy ước giữ như code cũ:
        // Sphere: colliderSize.x = "đường kính" (diameter)
        // Cylinder: x=diameter, y=height
        // Box: x,y,z = full size
        // Quy ước giữ như code cũ:
        // Sphere: colliderSize.x = "đường kính" (diameter)
        // Cylinder: x=diameter, y=height
        // Box: x,y,z = full size
        public Vector3 colliderSize = new Vector3(1, 1, 1);

        [SerializeField] private bool isActive = true; // [FIX] Default to true to prevent accidental disable

        [Header("Caster (Optional)")]
        [SerializeField] private bool isCustomCaster;
        [SerializeField] private Transform hitTransform;

        public bool IsActive => isActive;

        public Vector3 Position
        {
            get
            {
                if (isCustomCaster && hitTransform != null) return hitTransform.position;
                return CachePosition;
            }
        }

        public override void Initialize()
        {
            base.Initialize();
            isActive = true;
        }

        public override void Dispose()
        {
            base.Dispose();
            isActive = false;
        }

        public void OnHit(IAttacker source)
        {
            OnHitComplete?.Invoke(source);
        }

        public ColliderData GetColliderData()
        {
            uint bits = 1u << (ushort)EntityType;
            Vector3 worldScale = transform.lossyScale;
            Quaternion rot = transform.rotation;

            // Step 1: Calculate Local Scaled Extents (Half Size)
            Vector3 localHalfExtents = Vector3.zero;

            if (shapeType == ShapeType.Sphere)
            {
                // Sphere rotation doesn't matter for AABB size (if uniform), but let's be safe
                float r = (colliderSize.x * Mathf.Abs(worldScale.x)) * 0.5f;
                localHalfExtents = new Vector3(r, r, r);

                // Keep sphere logic simple? Or treat as AABB?
                // CollisionSystem Sphere Check ignores rotation anyway.
                float centerOffsetY = colliderSize.y * Mathf.Abs(worldScale.y);

                // [FIX] Size.z MUST be radius (r) for AABB checks to work!
                return new ColliderData
                {
                    Type = ShapeType.Sphere,
                    Size = new Vector3(r, centerOffsetY, r),  // [FIX] Z = r, not 0!
                    Offset = colliderSize.x,
                    CategoryBits = bits
                };
            }
            else if (shapeType == ShapeType.Cylinder)
            {
                // Cylinder is Axis-Aligned in Local Space (Y-up for Unity Cylinder)
                float r = (colliderSize.x * Mathf.Abs(worldScale.x)) * 0.5f;
                float h = (colliderSize.y * Mathf.Abs(worldScale.y)) * 0.5f;
                localHalfExtents = new Vector3(r, h, r);

                // [FIX] Size.z MUST be radius (r) for AABB checks to work!
                // Cylinder is circular on XZ plane, so extentX = extentZ = radius
                return new ColliderData
                {
                    Type = ShapeType.Cylinder,
                    Size = new Vector3(r, h, r),  // [FIX] Z = r, not 0!
                    Offset = colliderSize.x,
                    CategoryBits = bits
                };
            }
            else // BOX
            {
                localHalfExtents = new Vector3(
                    (colliderSize.x * Mathf.Abs(worldScale.x)) * 0.5f,
                    (colliderSize.y * Mathf.Abs(worldScale.y)) * 0.5f,
                    (colliderSize.z * Mathf.Abs(worldScale.z)) * 0.5f);
            }

            // Step 2: Rotate Local Extents to World Space AABB Extents
            // For a Box with half-extents (hx, hy, hz) and rotation matrix M:
            // WorldExtents.x = |Mxx * hx| + |Mxy * hy| + |Mxz * hz|
            // ...

            Matrix4x4 m = Matrix4x4.Rotate(rot);
            Vector3 worldHalfExtents = new Vector3(
                Mathf.Abs(m.m00 * localHalfExtents.x) + Mathf.Abs(m.m01 * localHalfExtents.y) + Mathf.Abs(m.m02 * localHalfExtents.z),
                Mathf.Abs(m.m10 * localHalfExtents.x) + Mathf.Abs(m.m11 * localHalfExtents.y) + Mathf.Abs(m.m12 * localHalfExtents.z),
                Mathf.Abs(m.m20 * localHalfExtents.x) + Mathf.Abs(m.m21 * localHalfExtents.y) + Mathf.Abs(m.m22 * localHalfExtents.z)
            );

            return new ColliderData
            {
                Type = ShapeType.Box,
                Size = worldHalfExtents,
                Offset = colliderSize.z,
                CategoryBits = bits
            };
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            UnityEditor.Handles.Label(
                Position + Vector3.up * (colliderSize.y * 2f + 0.5f),
                gameObject.name,
                new UnityEngine.GUIStyle()
                {
                    normal = new UnityEngine.GUIStyleState() { textColor = Color.yellow },
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                }
            );
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;

            // Use Matrix to automatically handle Position, Rotation, and SCALE
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

            if (shapeType == ShapeType.Box)
            {
                // Local Center Y = Half Height
                Vector3 localCenter = new Vector3(0, colliderSize.y * 0.5f, 0);
                Gizmos.DrawWireCube(localCenter, colliderSize);
            }
            else if (shapeType == ShapeType.Sphere)
            {
                float radius = colliderSize.x * 0.5f;
                // Sphere offset logic from GetColliderData: Offset = colliderSize.y (NOT halved in previous code)
                // Checking previous code: "centerOffsetY = colliderSize.y"
                // So center is at (0, colliderSize.y, 0)
                Vector3 localCenter = new Vector3(0, colliderSize.y, 0);
                Gizmos.DrawWireSphere(localCenter, radius);
            }
            else if (shapeType == ShapeType.Cylinder)
            {
                // Cylinder Logic: 
                // Unity Cylinder Primitive: Radius 0.5, Height 2, Center (0,0,0)
                // We want: Radius = colliderSize.x/2, Height = colliderSize.y
                // Center Y = colliderSize.y / 2

                Vector3 localCenter = new Vector3(0, colliderSize.y * 0.5f, 0);

                // Scale needed for primitive:
                // Radius 0.5 -> colliderSize.x/2 => Scale X/Z = colliderSize.x
                // Height 2 -> colliderSize.y => Scale Y = colliderSize.y / 2

                // Since we are already inside localToWorldMatrix (which applies Object Scale),
                // we ONLY apply the Shape dimensions here relative to unit 1.

                // Wait, DrawWireMesh doesn't use the current Gizmos.matrix scale correctly if we supply ANOTHER matrix?
                // Actually Gizmos.DrawWireMesh takes position/rotation/scale arguments which override?
                // No, DrawWireMesh(mesh, position, rotation, scale).
                // Let's rely on DrawMesh with a custom matrix combined with current?
                // Simpler: Just reconstruct the matrix for Cylinder specifically.

                Gizmos.matrix = oldMatrix; // Reset first

                Vector3 worldScale = transform.lossyScale;

                // Re-calculate visual parameters in World Space
                float radius = (colliderSize.x * Mathf.Abs(worldScale.x)) * 0.5f;
                float height = (colliderSize.y * Mathf.Abs(worldScale.y));

                Vector3 feetPos = Position;
                Vector3 center = feetPos + transform.up * (height * 0.5f);

                // Mesh Scale:
                // Unity Cylinder: r=0.5, h=2.
                // Target: r=radius, h=height.
                // ScaleX = radius / 0.5 = 2*radius
                // ScaleY = height / 2
                // ScaleZ = 2*radius

                Vector3 meshScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);

                Gizmos.matrix = Matrix4x4.TRS(center, transform.rotation, meshScale);
                Gizmos.DrawWireMesh(GetCylinderMesh());
            }

            Gizmos.matrix = oldMatrix;
        }

        private Mesh _cylinderMesh;
        private Mesh GetCylinderMesh()
        {
            if (_cylinderMesh == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                _cylinderMesh = go.GetComponent<MeshFilter>().sharedMesh;
                DestroyImmediate(go);
            }
            return _cylinderMesh;
        }
#endif
    }
}
