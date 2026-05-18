using System.Collections.Generic;
using UnityEngine;
using GamePlay.Entities;

using GamePlay.Items;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GamePlay.Roads
{
    public class RoadSegment : PoolEntity
    {
        [Header("References")]
        [SerializeField] private MeshRenderer meshRenderer;

        [SerializeField] private Transform content;
        [SerializeField] private Transform finish;
        [SerializeField] private Transform totalSegment;
        [SerializeField] private Transform middlePoint;

        [Header("Validates")]
        [SerializeField] private bool autoUpdateOnValidate = true;

        [Header("Connections")]
        [SerializeField] private Transform entryPoint;
        [SerializeField] private Transform exitPoint;

        [Header("Segment Dimensions")]
        [SerializeField] private float contentLength = 300f;
        [SerializeField] private float finishLength = 50f;

        #region ConveyorProperties

        [Header("ConveyorProperties")]
        [Tooltip("Mesh renderer của conveyor")]
        [SerializeField] public MeshRenderer conveyorMeshRenderer;

        [Tooltip("Tốc độ cuộn")]
        [SerializeField] public float scrollSpeed = 4f;

        private readonly string _texturePropertyName = "_MainTex";
        private Material _material;
        private Vector2 _currentOffset = Vector2.zero;

        #endregion

        public Transform EntryPoint => entryPoint;
        public Transform ExitPoint => exitPoint;
        public Transform MiddlePoint => middlePoint;

        public float Length => (entryPoint != null && exitPoint != null)
            ? Vector3.Distance(entryPoint.position, exitPoint.position)
            : (contentLength + finishLength);

        public float TotalLength => contentLength + finishLength;
        public float ContentLength => contentLength;
        public float FinishLength => finishLength;

        // (Nếu bạn không dùng list này nữa có thể xóa)
        private readonly List<ItemUnit> spawnedItems = new List<ItemUnit>();

        #region Unity

        private void Awake()
        {
            SetupConveyor();
        }

        private void Update()
        {
            if (!GameplayManager.IsGameStarted) return;
            ScrollConveyor();
        }

        private void OnDestroy()
        {
            ClearConveyor();
        }

        #endregion

        public void SetLength(float newContentLength, float newFinishLength)
        {
            contentLength = newContentLength;
            finishLength = newFinishLength;
            UpdateSegmentDimensions();
        }

        private bool UpdateSegmentDimensions()
        {
            if (content == null || finish == null)
            {
                Debug.LogWarning($"[RoadSegment] Content hoặc Finish chưa được gán cho {gameObject.name}");
                return false;
            }

            bool changed = false;

            // content scale Z
            var contentScale = content.localScale;
            if (!Mathf.Approximately(contentScale.z, contentLength))
            {
                contentScale.z = contentLength;
                content.localScale = contentScale;
                changed = true;
            }

            // finish position Z
            var finishPos = finish.localPosition;
            if (!Mathf.Approximately(finishPos.z, contentLength))
            {
                finishPos.z = contentLength;
                finish.localPosition = finishPos;
                changed = true;
            }

            // finish scale Z
            var finishScale = finish.localScale;
            if (!Mathf.Approximately(finishScale.z, finishLength))
            {
                finishScale.z = finishLength;
                finish.localScale = finishScale;
                changed = true;
            }

            // total segment scale Z
            if (totalSegment != null)
            {
                var totalScale = totalSegment.localScale;
                float totalLen = contentLength + finishLength;
                if (!Mathf.Approximately(totalScale.z, totalLen))
                {
                    totalScale.z = totalLen;
                    totalSegment.localScale = totalScale;
                    changed = true;
                }
            }

            // middle point pos Z
            if (middlePoint != null)
            {
                var middlePos = middlePoint.localPosition;
                if (!Mathf.Approximately(middlePos.z, contentLength))
                {
                    middlePos.z = contentLength;
                    middlePoint.localPosition = middlePos;
                    changed = true;
                }
            }

            // update entry/exit
            changed |= UpdateConnectionPoints();
            return changed;
        }

        private bool UpdateConnectionPoints()
        {
            if (entryPoint == null || exitPoint == null) return false;

            bool changed = false;

            if (entryPoint.localPosition != Vector3.zero)
            {
                entryPoint.localPosition = Vector3.zero;
                changed = true;
            }

            float totalLen = contentLength + finishLength;
            var targetExit = new Vector3(0f, 0f, totalLen);
            if (exitPoint.localPosition != targetExit)
            {
                exitPoint.localPosition = targetExit;
                changed = true;
            }

            return changed;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Auto-bind removed; assign MeshRenderer manually in Inspector.

            if (autoUpdateOnValidate)
            {
                // Tránh thao tác phá Prefab Asset
                if (!PrefabUtility.IsPartOfPrefabAsset(this))
                {
                    bool changed = UpdateSegmentDimensions();
                    changed |= AutoCalculatePoints();
                    if (changed)
                        EditorUtility.SetDirty(this);
                }
            }
        }

        private bool AutoCalculatePoints()
        {
            bool changed = false;

            if (entryPoint == null) entryPoint = FindChildTransform("Entry");
            if (exitPoint == null) exitPoint = FindChildTransform("Exit");

            if (entryPoint == null)
            {
                entryPoint = CreatePoint("Entry");
                changed = true;
            }
            if (exitPoint == null)
            {
                exitPoint = CreatePoint("Exit");
                changed = true;
            }

            changed |= UpdateConnectionPoints();
            return changed;
        }

        private Transform FindChildTransform(string childName)
        {
            var t = transform.Find(childName);
            return t;
        }

        private Transform CreatePoint(string name)
        {
            var go = new GameObject(name);

            bool isPartOfPrefabAsset = PrefabUtility.IsPartOfPrefabAsset(gameObject);
            if (isPartOfPrefabAsset)
            {
                go.transform.SetParent(transform, false);
                Undo.RegisterCreatedObjectUndo(go, "Create " + name + " Point");
            }
            else
            {
                go.transform.SetParent(transform, false);
            }

            go.transform.localRotation = Quaternion.identity;
            return go.transform;
        }

        [ContextMenu("Force Update Points")]
        private void ForceUpdatePoints()
        {
            AutoCalculatePoints();
        }

        private void OnDrawGizmos()
        {
            if (entryPoint != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(entryPoint.position, 0.3f);
                Gizmos.DrawCube(entryPoint.position, Vector3.one * 0.1f);
            }

            if (exitPoint != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(exitPoint.position, 0.3f);
                Gizmos.DrawCube(exitPoint.position, Vector3.one * 0.1f);
            }
        }
#endif

        #region Conveyor Management

        private void SetupConveyor()
        {
            if (conveyorMeshRenderer == null) return;

            // material sẽ instance per-renderer; OK cho playable (ít object)
            _material = conveyorMeshRenderer.material;
        }

        public void SyncWithWorldSpeed(float worldSpeed)
        {
            scrollSpeed = -worldSpeed * 0.5f;
        }

        private void ScrollConveyor()
        {
            if (_material == null) return;

            float dt = Time.deltaTime;
            _currentOffset.y += scrollSpeed * dt;
            _currentOffset.y %= 1.0f;

            _material.SetTextureOffset(_texturePropertyName, _currentOffset);
        }

        private void ClearConveyor()
        {
            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }
        }

        #endregion
    }
}
