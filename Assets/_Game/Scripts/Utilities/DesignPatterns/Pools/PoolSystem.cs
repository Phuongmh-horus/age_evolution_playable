using System;
using System.Collections.Generic;
using UnityEngine;
// [FIX] Đã xóa: using Unity.VisualScripting;

public static class PoolSystem
{
    private const string RootName = "[Pools]";
    private const int DefaultMaxInactivePerPool = 96;
    private static Transform _root;
    private class Pool
    {
        public readonly Stack<IPoolable> Inactive = new Stack<IPoolable>();
        public readonly List<IPoolable> Active = new List<IPoolable>();
        public Component PrefabComponent;
        public Transform Root;
    }

    private static readonly Dictionary<int, Pool> Pools = new Dictionary<int, Pool>();
    private static readonly Dictionary<IPoolable, Pool> PoolByInstance = new Dictionary<IPoolable, Pool>();

    public static void ClearAllPools()
    {
        Pools.Clear();
        PoolByInstance.Clear();
        if (_root != null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEngine.Object.DestroyImmediate(_root.gameObject);
            else
#endif
                UnityEngine.Object.Destroy(_root.gameObject);
            _root = null;
        }
    }

    public static T Spawn<T>(T prefab, Vector3 pos, Quaternion rot, Transform parent = null) where T : Component, IPoolable
    {
        if (prefab == null) return null;

        if (!Application.isPlaying)
        {
            var go = UnityEngine.Object.Instantiate(prefab.gameObject, parent);
            var comp = go.GetComponent<T>();
            var editTr = go.transform;
            editTr.SetPositionAndRotation(pos, rot);
            go.SetActive(true);
            comp?.New();
            return comp;
        }

        var obj = SpawnInternal(prefab, parent) as T;
        if (obj == null) return null;

        var runtimeTr = obj.transform;
        runtimeTr.SetParent(parent, false);
        runtimeTr.SetPositionAndRotation(pos, rot);

        obj.gameObject.SetActive(true);
        obj.New();

        return obj;
    }

    // [New] Overload for spawning as child (Zero Local Position)
    public static T Spawn<T>(T prefab, Transform parent) where T : Component, IPoolable
    {
        if (prefab == null) return null;

        if (!Application.isPlaying)
        {
            var go = UnityEngine.Object.Instantiate(prefab.gameObject, parent);
            var comp = go.GetComponent<T>();
            var editTr = go.transform;
            editTr.localPosition = Vector3.zero;
            editTr.localRotation = Quaternion.identity;
            editTr.localScale = Vector3.one;
            go.SetActive(true);
            comp?.New();
            return comp;
        }

        var obj = SpawnInternal(prefab, parent) as T;
        if (obj == null) return null;

        var runtimeTr = obj.transform;
        runtimeTr.SetParent(parent, false);
        runtimeTr.localPosition = Vector3.zero;
        runtimeTr.localRotation = Quaternion.identity;
        runtimeTr.localScale = Vector3.one;

        obj.gameObject.SetActive(true);
        obj.New();

        return obj;
    }

    private static Component SpawnInternal(Component prefab, Transform parent)
    {
        if (prefab == null) return null;

        int key = prefab.GetInstanceID();
        if (!Pools.TryGetValue(key, out var pool))
        {
            var root = GetOrCreateRoot();
            var poolName = $"[Pool]_{prefab.name}";
            Transform existing = null;
            if (root != null)
            {
                var existingGo = GameObject.Find(poolName);
                if (existingGo != null)
                {
                    existing = existingGo.transform;
                }
            }

            pool = new Pool
            {
                PrefabComponent = prefab,
                Root = existing != null ? existing : new GameObject(poolName).transform
            };
            if (root != null && pool.Root.parent != root)
            {
                pool.Root.SetParent(root, false);
            }
            Pools[key] = pool;
        }

        IPoolable instance = null;
        if (pool.Inactive.Count > 0)
        {
            instance = pool.Inactive.Pop();
        }
        else
        {
            var go = UnityEngine.Object.Instantiate(pool.PrefabComponent.gameObject, pool.Root);
            instance = go.GetComponent<IPoolable>();
        }

        if (instance == null)
        {
            Debug.LogError("[PoolSystem] Spawn failed: prefab does not implement IPoolable.");
            return null;
        }

        pool.Active.Add(instance);
        PoolByInstance[instance] = pool;
        return instance as Component;
    }

    public static void Despawn(IPoolable poolable)
    {
        if (poolable == null) return;

        if (!Application.isPlaying)
        {
            var c0 = poolable as Component;
            if (c0 != null) UnityEngine.Object.DestroyImmediate(c0.gameObject);
            return;
        }

        if (!PoolByInstance.TryGetValue(poolable, out var pool))
        {
            var c = poolable as Component;
            if (c != null) UnityEngine.Object.Destroy(c.gameObject);
            return;
        }

        poolable.Free();

        var comp = poolable as Component;
        if (comp == null)
        {
            Debug.LogWarning("[PoolSystem] Despawn skipped: poolable is not a Component.");
            PoolByInstance.Remove(poolable);
            return;
        }

        comp.gameObject.SetActive(false);
        comp.transform.SetParent(pool.Root, false);

        pool.Active.Remove(poolable);
        pool.Inactive.Push(poolable);
    }

    private static Transform GetOrCreateRoot()
    {
        if (_root != null) return _root;

        var rootGo = GameObject.Find(RootName);
        if (rootGo == null)
        {
            rootGo = new GameObject(RootName);
        }
        _root = rootGo.transform;
        return _root;
    }
}
