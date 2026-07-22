using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

namespace WeaponCraft
{
    public sealed class WeaponCraftVisualSystem : MonoBehaviour
    {
        [Header("Slots  (0 = Equipped, 1-N = queue)")]
        [SerializeField] private List<RectTransform> slots = new List<RectTransform>();

        [Header("Animation")]
        [SerializeField, Min(0.01f)] private float flyDuration = 0.25f;
        [SerializeField, Min(0.01f)] private float mergeDuration = 0.18f;
        [SerializeField, Min(0.01f)] private float mergePopDuration = 0.2f;
        [SerializeField, Min(0f)] private float mergeSpawnDelay = 0.05f;

        [Header("Containers")]
        [SerializeField] private RectTransform weaponCraftItemsContainer;

        private WeaponCraftConfigSO _config;
        private SlotEntry[] _slotData;

        // Maps SlotIndex -> (Tier -> pre-instantiated GameObject)
        private Dictionary<int, GameObject>[] _slotVisuals;

        // Fast item -> slot-index lookup.
        private readonly Dictionary<WeaponItem, int> _itemToSlot = new Dictionary<WeaponItem, int>(32);

        // Fly animation pool (temporarily instantiated at root).
        private readonly Dictionary<int, Queue<GameObject>> _flyPool = new Dictionary<int, Queue<GameObject>>(16);

        private Canvas _canvas;
        private Camera _uiCam;
        private bool _isDestroyed;

        public event System.Action<WeaponItem> OnMergeCompleted;
        public int SlotCount => slots.Count;

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
            if (_slotData != null)
                for (int i = 0; i < _slotData.Length; i++) _slotData[i] = null;
            _itemToSlot.Clear();
        }

        private void EnsureSlotData()
        {
            int n = slots.Count;
            if (_slotData != null && _slotData.Length == n) return;
            _slotData = new SlotEntry[n];
            _slotVisuals = new Dictionary<int, GameObject>[n];
            for (int i = 0; i < n; i++)
            {
                _slotData[i] = new SlotEntry();
                _slotVisuals[i] = new Dictionary<int, GameObject>(8);
            }
        }

        private void ResolveCanvas()
        {
            _canvas = GetComponentInParent<Canvas>();
            _uiCam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? (_canvas.worldCamera ?? Camera.main) : null;
        }

        // ── Prewarm ───────────────────────────────────────────────────────────────

        public void PrewarmWeapons()
        {
            if (_config?.TierVisuals == null) return;

            // 1. Scan scene-placed objects in slots and register them
            for (int i = 0; i < slots.Count; i++)
            {
                var slotParent = slots[i];
                if (slotParent == null) continue;

                var tags = slotParent.GetComponentsInChildren<TierTag>(true);
                if (tags.Length > 0)
                {
                    foreach (var tag in tags)
                    {
                        _slotVisuals[i][tag.Tier] = tag.gameObject;
                        tag.gameObject.SetActive(false);
                    }
                }
                else
                {
                    // Fallback: assume children are ordered tier 1, tier 2, etc.
                    for (int childIndex = 0; childIndex < slotParent.childCount; childIndex++)
                    {
                        var child = slotParent.GetChild(childIndex);
                        int tier = childIndex + 1; // Assuming child 0 is tier 1
                        _slotVisuals[i][tier] = child.gameObject;
                        child.gameObject.SetActive(false);
                    }
                }
            }

            // 2. Prewarm exactly 5 items per tier for fly animations
            var flyRoot = weaponCraftItemsContainer != null ? weaponCraftItemsContainer : transform;
            foreach (var ve in _config.TierVisuals)
            {
                if (ve?.Prefab == null) continue;
                int tier = ve.Tier;
                int count = 5;
                if (!_flyPool.ContainsKey(tier)) _flyPool[tier] = new Queue<GameObject>(count);

                for (int j = 0; j < count; j++)
                {
                    var go = Instantiate(ve.Prefab, flyRoot, false);
                    go.SetActive(false);
                    var newRt = GetRT(go);
                    if (newRt != null) newRt.localScale = Vector3.one;
                    EnsureTierTag(go, tier);
                    _flyPool[tier].Enqueue(go);
                }
            }
        }

        // ── Slot Visual Management ────────────────────────────────────────────────

        private void TurnOnSlotVisual(int slotIndex, int tier)
        {
            if (slotIndex < 0 || slotIndex >= slots.Count) return;
            var visuals = _slotVisuals[slotIndex];

            // Turn off all
            foreach (var kvp in visuals)
                if (kvp.Value != null) kvp.Value.SetActive(false);

            if (visuals.TryGetValue(tier, out var go) && go != null)
            {
                go.SetActive(true);
            }
            else
            {
                // Fallback: instantiate if missing
                WeaponCraftConfigSO.TierVisualEntry configVis = null;
                if (_config != null && _config.TierVisuals != null)
                {
                    for (int i = 0; i < _config.TierVisuals.Count; i++)
                    {
                        if (_config.TierVisuals[i].Tier == tier)
                        {
                            configVis = _config.TierVisuals[i];
                            break;
                        }
                    }
                }

                if (configVis != null && configVis.Prefab != null)
                {
                    go = Instantiate(configVis.Prefab, slots[slotIndex], false);
                    EnsureTierTag(go, tier);
                    visuals[tier] = go;
                    go.SetActive(true);
                }
                else
                {
                    Debug.LogWarning($"[WeaponCraftVisualSystem] Slot {slotIndex} does not have a visual for tier {tier} pre-attached on the scene!");
                }
            }
        }

        private void EnsureTierTag(GameObject go, int tier)
        {
            var tierTag = go.GetComponent<TierTag>();
            if (tierTag == null) tierTag = go.AddComponent<TierTag>();
            tierTag.Tier = tier;
        }

        private void ClearSlot(int index)
        {
            if (_slotData == null || index < 0 || index >= _slotData.Length) return;
            var entry = _slotData[index];
            if (entry.Item != null) _itemToSlot.Remove(entry.Item);
            entry.Item = null;

            // Turn off all visuals in this slot
            var visuals = _slotVisuals[index];
            foreach (var kvp in visuals)
            {
                if (kvp.Value != null) kvp.Value.SetActive(false);
            }
        }

        private void ClearAllSlots()
        {
            if (_slotData == null) return;
            for (int i = 0; i < _slotData.Length; i++) ClearSlot(i);
            _itemToSlot.Clear();
        }

        public void AddInstant(WeaponItem item, int slotIndex)
        {
            if (item == null || _slotData == null) return;
            slotIndex = Mathf.Clamp(slotIndex, 0, _slotData.Length - 1);

            ClearSlot(slotIndex);

            _slotData[slotIndex].Item = item;
            _itemToSlot[item] = slotIndex;
            TurnOnSlotVisual(slotIndex, item.Tier);

        }

        public void SyncVisuals(List<WeaponItem> items)
        {
            EnsureSlotData();
            ClearAllSlots();
            if (items == null) return;

            int n = Mathf.Min(items.Count, _slotData.Length);
            for (int i = 0; i < n; i++) AddInstant(items[i], i);
        }

        // ── Main Animation Flow ───────────────────────────────────────────────────

        public IEnumerator PlayMilestones(Vector3 gateFlyFrom, List<List<int>> milestones)
        {
            if (milestones == null || milestones.Count == 0) yield break;

            Vector2 uiCenterLocal = Vector2.zero;
            if (weaponCraftItemsContainer != null)
                uiCenterLocal = SlotCentreInRoot(weaponCraftItemsContainer);

            // -- Step 1: Gate -> Container -> Slots (Initial Fill) --
            var firstMilestone = milestones[0];

            // Gate to Container
            Vector2 gatePosLocal = WorldToRootLocal(gateFlyFrom);
            var gateFlyGo = SpawnFlyPrefab(1);
            if (gateFlyGo != null)
            {
                var rt = GetRT(gateFlyGo);
                if (rt != null)
                {
                    rt.anchoredPosition = gatePosLocal;
                    rt.localScale = Vector3.one;
                }
                gateFlyGo.SetActive(true);
                yield return DOTween.To(() => rt.anchoredPosition, x => rt.anchoredPosition = x, uiCenterLocal, flyDuration).SetEase(Ease.OutQuad).WaitForCompletion();
                DespawnFlyPrefab(gateFlyGo);
            }

            // Container to Slots (Simulate filling up to the first milestone)
            var flyGos = new List<GameObject>();
            Sequence initSeq = DOTween.Sequence();

            for (int i = 0; i < firstMilestone.Count; i++)
            {
                int tier = firstMilestone[i];
                if (tier <= 0) continue;

                Vector2 targetPos = SlotCentreInRoot(GetSlotRT(i));
                var go = SpawnFlyPrefab(tier);
                if (go != null)
                {
                    var rt = GetRT(go);
                    if (rt != null)
                    {
                        rt.anchoredPosition = uiCenterLocal;
                        rt.localScale = Vector3.one;
                    }
                    go.SetActive(true);
                    flyGos.Add(go);

                    float delay = i * 0.05f;
                    initSeq.Insert(delay, DOTween.To(() => rt.anchoredPosition, x => rt.anchoredPosition = x, targetPos, flyDuration).SetEase(Ease.OutQuad));
                }
            }

            if (flyGos.Count > 0)
                yield return initSeq.WaitForCompletion();

            for (int i = 0; i < flyGos.Count; i++) DespawnFlyPrefab(flyGos[i]);

            // Turn on real visuals for first milestone
            for (int i = 0; i < firstMilestone.Count; i++)
            {
                ClearSlot(i);
                if (firstMilestone[i] > 0) TurnOnSlotVisual(i, firstMilestone[i]);
            }
            if (firstMilestone[0] > 0) OnMergeCompleted?.Invoke(new WeaponItem(firstMilestone[0])); // Sync top weapon initially

            // -- Step 2: Chain Reaction Milestones (Slide-Up) --
            for (int m = 1; m < milestones.Count; m++)
            {
                var nextState = milestones[m];
                var prevState = milestones[m - 1];

                if (mergeSpawnDelay > 0f) yield return new WaitForSeconds(mergeSpawnDelay);

                flyGos.Clear();
                Sequence slideSeq = DOTween.Sequence();

                // Slide Up Anim (Slot i moves to Slot i-1)
                for (int i = 1; i < slots.Count; i++)
                {
                    if (prevState[i] <= 0) continue;

                    Vector2 startPos = SlotCentreInRoot(GetSlotRT(i));
                    Vector2 endPos = SlotCentreInRoot(GetSlotRT(i - 1));

                    var go = SpawnFlyPrefab(prevState[i]);
                    if (go != null)
                    {
                        var rt = GetRT(go);
                        if (rt != null)
                        {
                            rt.anchoredPosition = startPos;
                            rt.localScale = Vector3.one;
                        }
                        go.SetActive(true);
                        flyGos.Add(go);

                        slideSeq.Insert(0, DOTween.To(() => rt.anchoredPosition, x => rt.anchoredPosition = x, endPos, flyDuration).SetEase(Ease.InOutQuad));
                    }
                }

                // Removed the fly animation from container to bottom slot during chain reaction as requested.

                // Hide real visuals while sliding
                for (int i = 0; i < slots.Count; i++) ClearSlot(i);

                if (flyGos.Count > 0)
                    yield return slideSeq.WaitForCompletion();

                for (int i = 0; i < flyGos.Count; i++) DespawnFlyPrefab(flyGos[i]);

                // Turn on real visuals for next milestone
                for (int i = 0; i < nextState.Count; i++)
                {
                    if (nextState[i] > 0) TurnOnSlotVisual(i, nextState[i]);
                }

                // Pop effect for Slot 0
                if (nextState[0] > 0)
                {
                    if (_slotVisuals[0].TryGetValue(nextState[0], out var resultGo) && resultGo != null)
                    {
                        var rt = GetRT(resultGo);
                        if (rt != null)
                        {
                            rt.localScale = Vector3.zero;
                            rt.DOScale(Vector3.one, mergePopDuration).SetEase(Ease.OutBack);
                        }
                    }
                    OnMergeCompleted?.Invoke(new WeaponItem(nextState[0]));
                }

                // Add delay so player can see the intermediate tier before the next merge clears it
                yield return new WaitForSeconds(0.3f);
            }
        }

        // ── Fly Pool Helpers ──────────────────────────────────────────────────────

        private GameObject SpawnFlyPrefab(int tier)
        {
            if (_flyPool.TryGetValue(tier, out var queue) && queue.Count > 0)
            {
                var pooled = queue.Dequeue();
                if (pooled != null)
                {
                    pooled.transform.SetParent(weaponCraftItemsContainer != null ? weaponCraftItemsContainer : transform, false);
                    var pooledRt = GetRT(pooled);
                    if (pooledRt != null) pooledRt.localScale = Vector3.one;
                    return pooled;
                }
            }
            return null; // Do not instantiate at runtime to prevent lag
        }

        private void DespawnFlyPrefab(GameObject go)
        {
            if (go == null || _isDestroyed) return;
            go.SetActive(false);
            var tierTag = go.GetComponent<TierTag>();
            if (tierTag != null)
            {
                if (!_flyPool.ContainsKey(tierTag.Tier)) _flyPool[tierTag.Tier] = new Queue<GameObject>(8);
                var rt = GetRT(go);
                if (rt != null) rt.SetParent(transform, false);
                _flyPool[tierTag.Tier].Enqueue(go);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private RectTransform GetSlotRT(int index) => (index >= 0 && index < slots.Count) ? slots[index] : null;

        private RectTransform GetFlyRoot()
        {
            return (weaponCraftItemsContainer != null ? weaponCraftItemsContainer : transform) as RectTransform;
        }

        private Vector2 SlotCentreInRoot(RectTransform slotRT)
        {
            if (slotRT == null) return Vector2.zero;
            var root = GetFlyRoot();
            if (root == null) return Vector2.zero;
            var corners = new Vector3[4];
            slotRT.GetWorldCorners(corners);
            return root.InverseTransformPoint((corners[0] + corners[2]) * 0.5f);
        }

        private Vector2 WorldToRootLocal(Vector3 worldPos)
        {
            var root = GetFlyRoot();
            if (root == null) return Vector2.zero;
            if (_canvas == null) ResolveCanvas();

            if (_canvas != null)
            {
                Camera eventCam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _uiCam;
                Vector2 screenPt = Camera.main != null ? (Vector2)Camera.main.WorldToScreenPoint(worldPos)
                                                       : new Vector2(Screen.width * .5f, Screen.height * .5f);
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPt, eventCam, out Vector2 local))
                    return local;
            }
            return root.InverseTransformPoint(worldPos);
        }

        private static RectTransform GetRT(GameObject go) => go != null ? go.GetComponent<RectTransform>() : null;

        private static Vector2[] FillArray(Vector2 value, int count)
        {
            var arr = new Vector2[count];
            for (int i = 0; i < count; i++) arr[i] = value;
            return arr;
        }

        private sealed class SlotEntry
        {
            public WeaponItem Item;
        }

        [DisallowMultipleComponent]
        public sealed class TierTag : MonoBehaviour { public int Tier; }
    }
}
