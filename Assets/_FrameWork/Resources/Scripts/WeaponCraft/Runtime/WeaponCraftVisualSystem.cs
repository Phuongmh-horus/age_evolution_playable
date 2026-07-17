using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WeaponCraft
{
    /// <summary>
    /// Drives the weapon craft UI panel (Canvas/RectTransform based).
    ///
    /// Slot layout (assigned in Inspector):
    ///   Slot[0] = Equipped (top-tier weapon, always slot 0 in data)
    ///   Slot[1..N] = Queue slots
    ///
    /// Invariant: _slots[i].Instance is always a child of slots[i], EXCEPT during
    /// animation when it is temporarily lifted to the panel root.
    /// After every animation, SyncVisuals() re-establishes the invariant.
    /// </summary>
    public sealed class WeaponCraftVisualSystem : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────────
        [Header("Slots  (0 = Equipped, 1-N = queue)")]
        [SerializeField] private List<RectTransform> slots = new List<RectTransform>();

        [Header("Animation")]
        [SerializeField, Min(0.01f)] private float flyDuration = 0.25f;
        [SerializeField, Min(0.01f)] private float mergeDuration = 0.18f;
        [SerializeField, Min(0f)] private float mergeSpawnDelay = 0.05f;
        [SerializeField, Min(0f)] private float reflowDuration = 0.10f;

        [Header("Overflow — hides this GO when item count > slot count")]
        [SerializeField] private GameObject overflowHideTarget;

        // ── Runtime ───────────────────────────────────────────────────────────────
        private WeaponCraftConfigSO _config;

        // Slot data: exactly one entry per slot (same length as slots list).
        private SlotEntry[] _slotData;

        // Fast item → slot-index lookup.
        private readonly Dictionary<WeaponItem, int> _itemToSlot = new Dictionary<WeaponItem, int>(32);

        // Per-tier GO pool (inactive, parented to this transform's root slot).
        private readonly Dictionary<int, Queue<GameObject>> _pool = new Dictionary<int, Queue<GameObject>>(16);

        // Canvas/camera for world→UI coordinate conversion.
        private Canvas _canvas;
        private Camera _uiCam;

        // Guard: prevents DespawnGo from running during scene teardown.
        private bool _isDestroyed;

        // ── Events ────────────────────────────────────────────────────────────────
        public event System.Action<WeaponItem> OnMergeCompleted;

        // ── Properties ───────────────────────────────────────────────────────────
        public int SlotCount => slots.Count;

        // ── Setup ─────────────────────────────────────────────────────────────────

        public void Bind(WeaponCraftConfigSO config)
        {
            _config = config;
            EnsureSlotData();
            ResolveCanvas();
        }

        private void Awake()
        {
            EnsureSlotData();
            ResolveCanvas();
        }

        private void OnDestroy()
        {
            _isDestroyed = true;
            // Do NOT call DespawnGo here — Unity is tearing down the hierarchy.
            // Just null out references so GC can collect.
            if (_slotData != null)
                for (int i = 0; i < _slotData.Length; i++)
                    _slotData[i] = null;
            _itemToSlot.Clear();
        }

        private void EnsureSlotData()
        {
            int n = slots.Count;
            if (_slotData != null && _slotData.Length == n) return;
            _slotData = new SlotEntry[n];
            for (int i = 0; i < n; i++) _slotData[i] = new SlotEntry();
        }

        private void ResolveCanvas()
        {
            _canvas = GetComponentInParent<Canvas>();
            _uiCam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? (_canvas.worldCamera ?? Camera.main)
                    : null;
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Main entry point called by WeaponCraftSystem.ProcessLoop.
        /// Plays add animations then merge animations, then reflows.
        /// </summary>
        public IEnumerator PlayOps(List<CraftOp> addOps, List<CraftOp> mergeOps)
        {
            if (addOps != null && addOps.Count > 0)
                yield return CoPlayAdds(addOps);

            if (mergeOps != null)
                for (int i = 0; i < mergeOps.Count; i++)
                    yield return CoPlayMerge(mergeOps[i]);

            yield return CoReflow(reflowDuration);
        }

        /// <summary>Instant add — no animation. Used for starter item and SyncVisuals.</summary>
        public void AddInstant(WeaponItem item, int slotIndex)
        {
            if (item == null || _slotData == null) return;
            slotIndex = Mathf.Clamp(slotIndex, 0, _slotData.Length - 1);

            // Clear existing occupant if any.
            ClearSlot(slotIndex);

            var slot = GetSlotRT(slotIndex);
            var go = SpawnPrefab(item.Tier, slot != null ? slot : transform as RectTransform);
            if (go == null) return;

            PlaceInSlot(go, slotIndex, activate: true);
            _slotData[slotIndex].Item = item;
            _slotData[slotIndex].Instance = go;
            _itemToSlot[item] = slotIndex;

            RefreshOverflow();
        }

        /// <summary>
        /// Rebuilds all slot visuals from scratch to match item list.
        /// Called by WeaponCraftSystem as a safety net after every batch.
        /// </summary>
        public void SyncVisuals(List<WeaponItem> items)
        {
            EnsureSlotData();
            ClearAllSlots();
            if (items == null) return;

            int n = Mathf.Min(items.Count, _slotData.Length);
            for (int i = 0; i < n; i++)
                AddInstant(items[i], i);

            RefreshOverflow();
        }

        /// <summary>Pre-instantiates weapon prefabs into the pool. Call once on boot.</summary>
        public void PrewarmWeapons()
        {
            if (_config?.TierVisuals == null) return;
            var root = slots.Count > 0 ? (Transform)slots[0] : transform;

            for (int i = 0; i < _config.TierVisuals.Count; i++)
            {
                var ve = _config.TierVisuals[i];
                if (ve?.Prefab == null) continue;
                int tier = ve.Tier;
                int count = tier <= 1 ? 16 : tier <= 2 ? 12 : 8;
                if (!_pool.ContainsKey(tier)) _pool[tier] = new Queue<GameObject>(count);
                for (int j = 0; j < count; j++)
                {
                    var go = Instantiate(ve.Prefab, root);
                    go.SetActive(false);
                    EnsureTierTag(go, tier);
                    _pool[tier].Enqueue(go);
                }
            }
        }

        // ── Add animation ─────────────────────────────────────────────────────────

        private IEnumerator CoPlayAdds(List<CraftOp> ops)
        {
            // Spawn every icon at the fly-from world position, register in slot data.
            int n = ops.Count;
            var gos = new GameObject[n];
            var starts = new Vector2[n];

            for (int i = 0; i < n; i++)
            {
                var op = ops[i];
                int si = Mathf.Clamp(op.TargetSlot, 0, _slotData.Length - 1);
                var slotRT = GetSlotRT(si);
                var parent = slotRT != null ? (Transform)slotRT : transform;

                // If slot is already occupied by an older item, bump it — SyncVisuals will fix
                // everything properly at the end of the batch, so we just overwrite here.
                ClearSlot(si);

                var go = SpawnPrefab(op.Result.Tier, parent);
                if (go == null) { gos[i] = null; continue; }

                // Position at fly-from (world→slot-local).
                Vector2 flyLocal = WorldToRootLocal(op.FlyFrom);
                var rt = GetRT(go);
                if (rt != null)
                {
                    rt.SetParent(transform as RectTransform, false);
                    rt.anchoredPosition = flyLocal;
                }
                go.SetActive(true);

                gos[i] = go;
                starts[i] = flyLocal;

                // Register in slot data.
                _slotData[si].Item = op.Result;
                _slotData[si].Instance = go;
                _itemToSlot[op.Result] = si;
            }

            // Animate all concurrently to slot centres.
            var targets = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                int si = Mathf.Clamp(ops[i].TargetSlot, 0, _slotData.Length - 1);
                targets[i] = SlotCentreInRoot(GetSlotRT(si));
            }

            yield return CoAnimateMany(gos, starts, targets, flyDuration);

            // Attach each GO to its slot.
            for (int i = 0; i < n; i++)
            {
                if (gos[i] == null) continue;
                int si = Mathf.Clamp(ops[i].TargetSlot, 0, _slotData.Length - 1);
                PlaceInSlot(gos[i], si, activate: false);
            }
        }

        // ── Merge animation ───────────────────────────────────────────────────────

        private IEnumerator CoPlayMerge(CraftOp op)
        {
            if (op.Result == null) yield break;

            int resultSlot = Mathf.Clamp(op.TargetSlot, 0, _slotData.Length - 1);
            var resultSlotRT = GetSlotRT(resultSlot);

            // Collect source GOs; lift each to root for free movement.
            var srcGOs = new List<GameObject>(op.Sources?.Count ?? 0);
            var srcStarts = new List<Vector2>(srcGOs.Capacity);

            if (op.Sources != null)
            {
                for (int i = 0; i < op.Sources.Count; i++)
                {
                    var src = op.Sources[i];
                    if (src == null) continue;
                    if (!_itemToSlot.TryGetValue(src, out int si)) continue;
                    var go = _slotData[si].Instance;
                    if (go == null) continue;

                    var rt = GetRT(go);
                    Vector2 startAP = Vector2.zero;
                    if (rt != null)
                    {
                        // Lift: convert current world pos → root anchored position.
                        Vector3[] corners = new Vector3[4];
                        rt.GetWorldCorners(corners);
                        Vector3 worldCentre = (corners[0] + corners[2]) * 0.5f;
                        startAP = WorldToRootLocal(worldCentre);
                        rt.SetParent(transform as RectTransform, false);
                        rt.anchoredPosition = startAP;
                    }

                    srcGOs.Add(go);
                    srcStarts.Add(startAP);
                }
            }

            // Fly sources to result slot centre.
            Vector2 mergeTarget = SlotCentreInRoot(resultSlotRT);
            if (srcGOs.Count > 0)
                yield return CoAnimateMany(srcGOs.ToArray(), srcStarts.ToArray(),
                                           FillArray(mergeTarget, srcGOs.Count), mergeDuration);

            // Small delay before spawning result.
            if (mergeSpawnDelay > 0f)
            {
                float t = 0f;
                while (t < mergeSpawnDelay) { t += Time.deltaTime; yield return null; }
            }

            // Despawn source GOs + clear slot data for sources.
            if (op.Sources != null)
            {
                for (int i = 0; i < op.Sources.Count; i++)
                {
                    var src = op.Sources[i];
                    if (src == null) continue;
                    if (_itemToSlot.TryGetValue(src, out int si))
                    {
                        _slotData[si].Item = null;
                        _slotData[si].Instance = null;
                    }
                    _itemToSlot.Remove(src);
                }
                // Despawn GOs after clearing data (safe order).
                for (int i = 0; i < srcGOs.Count; i++) DespawnGo(srcGOs[i]);
            }

            // Clear result slot if it was occupied by something else.
            ClearSlot(resultSlot);

            // Spawn result prefab.
            var parent = resultSlotRT != null ? (Transform)resultSlotRT : transform;
            var resultGo = SpawnPrefab(op.Result.Tier, parent);
            if (resultGo != null)
            {
                PlaceInSlot(resultGo, resultSlot, activate: true);
                _slotData[resultSlot].Item = op.Result;
                _slotData[resultSlot].Instance = resultGo;
                _itemToSlot[op.Result] = resultSlot;
            }

            // Notify WeaponCraftSystem → triggers equip + effect.
            OnMergeCompleted?.Invoke(op.Result);
            RefreshOverflow();
        }

        // ── Reflow ────────────────────────────────────────────────────────────────

        /// <summary>Smoothly moves all slot GOs back to their correct slot centres.</summary>
        private IEnumerator CoReflow(float duration)
        {
            int n = _slotData.Length;
            if (n == 0) yield break;

            var gos = new GameObject[n];
            var starts = new Vector2[n];
            var targets = new Vector2[n];

            for (int i = 0; i < n; i++)
            {
                var go = _slotData[i].Instance;
                gos[i] = go;
                if (go == null) continue;
                var rt = GetRT(go);
                starts[i] = rt != null ? rt.anchoredPosition : Vector2.zero;
                targets[i] = SlotCentreInRoot(GetSlotRT(i));
            }

            if (duration <= 0f)
            {
                for (int i = 0; i < n; i++) PlaceInSlot(gos[i], i, activate: false);
                yield break;
            }

            yield return CoAnimateMany(gos, starts, targets, duration);

            for (int i = 0; i < n; i++) PlaceInSlot(gos[i], i, activate: false);
        }

        // ── Generic animation coroutine ───────────────────────────────────────────

        private IEnumerator CoAnimateMany(GameObject[] gos, Vector2[] starts, Vector2[] targets, float duration)
        {
            int n = gos.Length;
            duration = Mathf.Max(0.0001f, duration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                for (int i = 0; i < n; i++)
                {
                    if (gos[i] == null) continue;
                    var rt = GetRT(gos[i]);
                    if (rt != null) rt.anchoredPosition = Vector2.Lerp(starts[i], targets[i], t);
                }
                yield return null;
            }
            for (int i = 0; i < n; i++)
            {
                if (gos[i] == null) continue;
                var rt = GetRT(gos[i]);
                if (rt != null) rt.anchoredPosition = targets[i];
            }
        }

        // ── Pool helpers ──────────────────────────────────────────────────────────

        private GameObject SpawnPrefab(int tier, Transform parent)
        {
            if (!_pool.TryGetValue(tier, out var queue))
            {
                queue = new Queue<GameObject>(8);
                _pool[tier] = queue;
            }
            while (queue.Count > 0)
            {
                var pooled = queue.Dequeue();
                if (pooled == null) continue;
                pooled.transform.SetParent(parent, false);
                pooled.SetActive(false);
                return pooled;
            }
            // Pool miss → instantiate.
            var prefab = _config?.GetPrefabForTier(tier);
            if (prefab == null)
            {
                Debug.LogWarning($"[WeaponCraftVisualSystem] No prefab for tier {tier}");
                return null;
            }
            var go = Instantiate(prefab, parent);
            go.SetActive(false);
            EnsureTierTag(go, tier);
            return go;
        }

        private void DespawnGo(GameObject go)
        {
            if (go == null || _isDestroyed) return;
            go.SetActive(false);
            var tag = go.GetComponent<TierTag>();
            if (tag != null)
            {
                if (!_pool.ContainsKey(tag.Tier)) _pool[tag.Tier] = new Queue<GameObject>(8);
                // Re-parent to a safe root before pooling so it isn't a dangling child.
                var rt = GetRT(go);
                var root = slots.Count > 0 ? (Transform)slots[0] : transform;
                if (rt != null) rt.SetParent(root, false);
                else go.transform.SetParent(root, false);
                _pool[tag.Tier].Enqueue(go);
            }
            // If no TierTag: GO stays deactivated wherever it is (no pool return needed).
        }

        private static void EnsureTierTag(GameObject go, int tier)
        {
            var tag = go.GetComponent<TierTag>();
            if (tag == null) tag = go.AddComponent<TierTag>();
            tag.Tier = tier;
        }

        // ── Slot helpers ──────────────────────────────────────────────────────────

        private RectTransform GetSlotRT(int index)
            => (index >= 0 && index < slots.Count) ? slots[index] : null;

        /// <summary>
        /// Attaches go to the correct slot RectTransform and zeros its anchored position.
        /// If activate=true, also calls SetActive(true).
        /// </summary>
        private void PlaceInSlot(GameObject go, int slotIndex, bool activate)
        {
            if (go == null) return;
            var slotRT = GetSlotRT(slotIndex);
            if (slotRT == null) return;
            var rt = GetRT(go);
            if (rt != null)
            {
                rt.SetParent(slotRT, false);
                rt.anchoredPosition = Vector2.zero;
                rt.localScale = Vector3.one;
            }
            else
            {
                go.transform.SetParent(slotRT, false);
                go.transform.localPosition = Vector3.zero;
            }
            if (activate) go.SetActive(true);
        }

        private void ClearSlot(int index)
        {
            if (_slotData == null || index < 0 || index >= _slotData.Length) return;
            var entry = _slotData[index];
            if (entry.Item != null) _itemToSlot.Remove(entry.Item);
            if (entry.Instance != null) DespawnGo(entry.Instance);
            entry.Item = null;
            entry.Instance = null;
        }

        private void ClearAllSlots()
        {
            if (_slotData == null) return;
            for (int i = 0; i < _slotData.Length; i++) ClearSlot(i);
            _itemToSlot.Clear();
        }

        // ── Coordinate helpers ────────────────────────────────────────────────────

        /// <summary>Returns slot centre as anchoredPosition in this transform's rect space.</summary>
        private Vector2 SlotCentreInRoot(RectTransform slotRT)
        {
            if (slotRT == null) return Vector2.zero;
            var root = transform as RectTransform;
            if (root == null) return Vector2.zero;
            var corners = new Vector3[4];
            slotRT.GetWorldCorners(corners);
            return root.InverseTransformPoint((corners[0] + corners[2]) * 0.5f);
        }

        /// <summary>Converts a world position to anchoredPosition in this transform's rect.</summary>
        private Vector2 WorldToRootLocal(Vector3 worldPos)
        {
            var root = transform as RectTransform;
            if (root == null) return Vector2.zero;
            if (_canvas == null) ResolveCanvas();

            if (_canvas != null)
            {
                Camera eventCam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _uiCam;
                Vector2 screenPt = Camera.main != null
                    ? (Vector2)Camera.main.WorldToScreenPoint(worldPos)
                    : new Vector2(Screen.width * .5f, Screen.height * .5f);
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPt, eventCam, out Vector2 local))
                    return local;
            }
            return root.InverseTransformPoint(worldPos);
        }

        private static RectTransform GetRT(GameObject go)
            => go != null ? go.GetComponent<RectTransform>() : null;

        private static Vector2[] FillArray(Vector2 value, int count)
        {
            var arr = new Vector2[count];
            for (int i = 0; i < count; i++) arr[i] = value;
            return arr;
        }

        // ── Overflow ──────────────────────────────────────────────────────────────

        private void RefreshOverflow()
        {
            if (overflowHideTarget == null) return;
            int occupied = 0;
            if (_slotData != null)
                for (int i = 0; i < _slotData.Length; i++)
                    if (_slotData[i].Item != null) occupied++;
            // Hide when we have MORE items than slots (shouldn't happen often, but just in case).
            overflowHideTarget.SetActive(occupied <= _slotData.Length);
        }

        // ── Nested types ──────────────────────────────────────────────────────────

        private sealed class SlotEntry
        {
            public WeaponItem Item;
            public GameObject Instance;
        }
    }

    /// <summary>Tiny component attached to every weapon icon GO so DespawnGo can pool it correctly.</summary>
    [DisallowMultipleComponent]
    public sealed class TierTag : MonoBehaviour { public int Tier; }
}