using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using GamePlay.Roads;

namespace GamePlay.Map
{
    public class MapGenerator : MonoBehaviour
    {
        private enum ForwardAxis
        {
            X,
            Z
        }

        [Header("Data")]
        [SerializeField] private MapDataSO mapData;

        [SerializeField] private Transform backGroundParent;
        [SerializeField] private bool clearAllBackgroundChildren = true;
        [Header("Runtime BG Culling")]
        [SerializeField] private bool enablePassedBackgroundCulling = true;
        [SerializeField, Min(0.02f)] private float cullCheckInterval = 0.08f;
        [SerializeField, Min(0f)] private float cullDistanceBehindWheel = 3f;
        [SerializeField, Min(0f)] private float cullDistanceAheadWheel = 680f;
        [SerializeField, Min(0f)] private float startupCullDistanceAheadWheel = 220f;
        [SerializeField, Min(0f)] private float cullVisibilityPadding = 30f;
        [SerializeField, Min(0.01f)] private float cullUpdateMoveThreshold = 0.15f;
        [SerializeField] private ForwardAxis mapForwardAxis = ForwardAxis.Z;
        [SerializeField] private bool forwardAxisPositive = true;
        [SerializeField] private bool disableWholeBackgroundChunk = true;

        public List<RoadSegment> activeSegments = new List<RoadSegment>();
        private Transform lastExitPoint;

        private GameObject _backGroundGO;
        private Transform _wheelTransform;
        private Coroutine _backgroundCullRoutine;
        private WaitForSeconds _cullWait;
        private readonly List<BackgroundChunk> _backgroundChunks = new List<BackgroundChunk>();
        private float _lastWheelForwardForCulling = float.NaN;

        private sealed class BackgroundChunk
        {
            public Transform Root;
            public Renderer[] Renderers;
            public float MinForward;
            public float MaxForward;
            public bool IsVisible;
        }

        // Public method to allow MapContentGenerator to access active segments
        public List<RoadSegment> GetActiveSegments()
        {
            // Populate activeSegments if empty but children exist
            if (activeSegments.Count == 0 && transform.childCount > 0)
            {
                for (int i = 0; i < transform.childCount; i++)
                {
                    var child = transform.GetChild(i);
                    var segment = child.GetComponent<RoadSegment>();
                    if (segment != null) activeSegments.Add(segment);
                }
            }

            return activeSegments;
        }

        public Transform GetSpawnPlayerTransform()
        {
            var segments = GetActiveSegments();
            if (segments.Count == 0) return null;
            return segments[0].EntryPoint;
        }

        public void GenerateMap(MapDataSO mapDataSo)
        {
            mapData = mapDataSo;
            GenerateAllSegmentsFromData();
        }

        public MapDataSO CurrentMapData => mapData;

        public void BindWheelTransform(Transform wheelTransform)
        {
            _wheelTransform = wheelTransform;
            _lastWheelForwardForCulling = float.NaN;
            if (_backgroundChunks.Count == 0)
            {
                RebuildBackgroundChunks();
            }
            UpdateBackgroundVisibility();
            EnsureBackgroundCullRoutine();
        }

        private void GenerateAllSegmentsFromData()
        {
            ClearMap();

            if (mapData == null || mapData.RoadSegment == null)
            {
                Debug.LogWarning("MapData is not set or has no RoadSegment.");
                return;
            }

            // Playable: spawn duy nhất 1 RoadSegment
            SpawnSegment();

            // Background theo era/map
            if (backGroundParent != null && mapData.BackGround != null)
            {
                _backGroundGO = Instantiate(mapData.BackGround, backGroundParent);
            }

            RebuildBackgroundChunks();
            EnsureBackgroundCullRoutine();
        }

        private void ClearMap()
        {
            // Clear children road segments
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                SafeDestroy(transform.GetChild(i).gameObject);
            }

            if (backGroundParent != null && clearAllBackgroundChildren)
            {
                for (int i = backGroundParent.childCount - 1; i >= 0; i--)
                {
                    SafeDestroy(backGroundParent.GetChild(i).gameObject);
                }
            }
            else if (_backGroundGO != null)
            {
                SafeDestroy(_backGroundGO);
            }
            _backGroundGO = null;

            activeSegments.Clear();
            ClearBackgroundChunks();

            // Reset to self
            lastExitPoint = transform;
        }

        private void OnDisable()
        {
            StopBackgroundCullRoutine();
        }

        private void RebuildBackgroundChunks()
        {
            ClearBackgroundChunks();

            if (!enablePassedBackgroundCulling) return;

            Transform sourceRoot = _backGroundGO != null ? _backGroundGO.transform : backGroundParent;
            if (sourceRoot == null) return;

            var allRenderers = sourceRoot.GetComponentsInChildren<Renderer>(true);
            if (allRenderers == null || allRenderers.Length == 0)
            {
                return;
            }

            var uniqueRoots = new HashSet<Transform>();
            for (int i = 0; i < allRenderers.Length; i++)
            {
                var renderer = allRenderers[i];
                if (renderer == null) continue;

                Transform chunkRoot = ResolveChunkRoot(renderer.transform, sourceRoot);
                if (chunkRoot == null) continue;
                if (!uniqueRoots.Add(chunkRoot)) continue;

                TryAddBackgroundChunk(chunkRoot);
            }
        }

        private void TryAddBackgroundChunk(Transform chunkRoot)
        {
            if (chunkRoot == null) return;

            var renderers = chunkRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0) return;

            float minForward = float.MaxValue;
            float maxForward = float.MinValue;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;

                float rendererMin = GetSignedRendererMinForward(renderer);
                float rendererMax = GetSignedRendererMaxForward(renderer);
                if (rendererMin < minForward) minForward = rendererMin;
                if (rendererMax > maxForward) maxForward = rendererMax;
            }

            if (maxForward <= float.MinValue) return;

            var chunk = new BackgroundChunk
            {
                Root = chunkRoot,
                Renderers = renderers,
                MinForward = minForward,
                MaxForward = maxForward,
                IsVisible = true
            };

            // Ensure all chunks start visible on each generation/reload.
            SetChunkVisible(chunk, true);
            _backgroundChunks.Add(chunk);
        }

        private static Transform ResolveChunkRoot(Transform leaf, Transform sourceRoot)
        {
            if (leaf == null || sourceRoot == null) return leaf;

            var current = leaf;
            while (current.parent != null && current.parent != sourceRoot)
            {
                current = current.parent;
            }

            return current;
        }

        private void EnsureBackgroundCullRoutine()
        {
            if (!Application.isPlaying) return;
            if (!enablePassedBackgroundCulling) return;
            if (_backgroundChunks.Count == 0) return;
            if (_backgroundCullRoutine != null) return;

            _cullWait = new WaitForSeconds(Mathf.Max(0.02f, cullCheckInterval));
            _backgroundCullRoutine = StartCoroutine(CoCullPassedBackground());
        }

        private IEnumerator CoCullPassedBackground()
        {
            while (true)
            {
                if (!enablePassedBackgroundCulling || _backgroundChunks.Count == 0)
                {
                    _backgroundCullRoutine = null;
                    yield break;
                }

                if (_wheelTransform != null)
                {
                    if (!GameplayManager.IsGameStarted)
                    {
                        yield return _cullWait;
                        continue;
                    }
                    UpdateBackgroundVisibility();
                }

                yield return _cullWait;
            }
        }

        private void UpdateBackgroundVisibility()
        {
            if (_wheelTransform == null) return;
            if (_backgroundChunks.Count == 0) return;

            float wheelForward = GetSignedAxisValue(_wheelTransform.position);
            float moveThreshold = Mathf.Max(0.01f, cullUpdateMoveThreshold);
            if (!float.IsNaN(_lastWheelForwardForCulling) &&
                Mathf.Abs(wheelForward - _lastWheelForwardForCulling) < moveThreshold)
            {
                return;
            }

            _lastWheelForwardForCulling = wheelForward;
            // Keep enough look-ahead so front meshes don't pop in too close to camera.
            float behindDistance = Mathf.Max(0f, cullDistanceBehindWheel);
            float aheadDistance = GameplayManager.IsGameStarted
                ? Mathf.Max(0f, cullDistanceAheadWheel)
                : Mathf.Max(0f, startupCullDistanceAheadWheel);
            float padding = Mathf.Max(0f, cullVisibilityPadding);
            float visibleMin = wheelForward - behindDistance - padding;
            float visibleMax = wheelForward + aheadDistance + padding;

            for (int i = 0; i < _backgroundChunks.Count; i++)
            {
                var chunk = _backgroundChunks[i];
                if (chunk == null) continue;

                bool shouldBeVisible = chunk.MaxForward >= visibleMin && chunk.MinForward <= visibleMax;
                if (chunk.IsVisible == shouldBeVisible) continue;

                SetChunkVisible(chunk, shouldBeVisible);
                chunk.IsVisible = shouldBeVisible;
            }
        }

        private float GetSignedAxisValue(Vector3 worldPos)
        {
            float axisValue = mapForwardAxis == ForwardAxis.X ? worldPos.x : worldPos.z;
            return forwardAxisPositive ? axisValue : -axisValue;
        }

        private float GetSignedRendererMaxForward(Renderer renderer)
        {
            var bounds = renderer.bounds;

            if (mapForwardAxis == ForwardAxis.X)
            {
                return forwardAxisPositive ? bounds.max.x : -bounds.min.x;
            }

            return forwardAxisPositive ? bounds.max.z : -bounds.min.z;
        }

        private float GetSignedRendererMinForward(Renderer renderer)
        {
            var bounds = renderer.bounds;

            if (mapForwardAxis == ForwardAxis.X)
            {
                return forwardAxisPositive ? bounds.min.x : -bounds.max.x;
            }

            return forwardAxisPositive ? bounds.min.z : -bounds.max.z;
        }

        private void ClearBackgroundChunks()
        {
            for (int i = 0; i < _backgroundChunks.Count; i++)
            {
                var chunk = _backgroundChunks[i];
                if (chunk == null || chunk.IsVisible) continue;
                SetChunkVisible(chunk, true);
                chunk.IsVisible = true;
            }

            _backgroundChunks.Clear();
            _lastWheelForwardForCulling = float.NaN;
            StopBackgroundCullRoutine();
        }

        private void StopBackgroundCullRoutine()
        {
            if (_backgroundCullRoutine == null) return;

            StopCoroutine(_backgroundCullRoutine);
            _backgroundCullRoutine = null;
        }

        private void SetChunkVisible(BackgroundChunk chunk, bool visible)
        {
            if (chunk == null) return;

            if (disableWholeBackgroundChunk)
            {
                if (chunk.Root != null && chunk.Root.gameObject.activeSelf != visible)
                {
                    chunk.Root.gameObject.SetActive(visible);
                }
                return;
            }

            if (chunk.Renderers == null) return;
            for (int i = 0; i < chunk.Renderers.Length; i++)
            {
                var renderer = chunk.Renderers[i];
                if (renderer != null && renderer.enabled != visible)
                {
                    renderer.enabled = visible;
                }
            }
        }

        private RoadSegment SpawnSegment()
        {
            if (mapData == null || mapData.RoadSegment == null)
            {
                Debug.LogError("MapData is not set or RoadSegment is null.");
                return null;
            }

            RoadSegment segmentPrefab = mapData.RoadSegment;

            RoadSegment newSegment = Instantiate(segmentPrefab, transform);

            // Apply custom dimensions from MapDataSO
            newSegment.SetLength(mapData.ContentLength, mapData.FinishLength);

            // Connect with previous (ở đây chỉ 1 segment, nhưng vẫn để đúng logic nối)
            if (lastExitPoint != null && newSegment.EntryPoint != null)
            {
                Vector3 entryOffset = newSegment.Transform.position - newSegment.EntryPoint.position;
                newSegment.Transform.position = lastExitPoint.position + entryOffset;
            }

            lastExitPoint = newSegment.ExitPoint != null ? newSegment.ExitPoint : newSegment.Transform;
            activeSegments.Add(newSegment);

            return newSegment;
        }

        private static void SafeDestroy(Object obj)
        {
            if (obj == null) return;

            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
