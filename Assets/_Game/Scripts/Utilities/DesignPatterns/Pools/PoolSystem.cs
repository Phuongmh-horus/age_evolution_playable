using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Pools
{
    public static class PoolSystem
    {
        private const string RootName = "[Pools]";
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
                    Object.DestroyImmediate(_root.gameObject);
                else
#endif
                    Object.Destroy(_root.gameObject);
                _root = null;
            }
        }

        // [FIX] Chỉ còn DUY NHẤT 1 method generic, không còn overload GameObject/Component song song
        public static void Prewarm<T>(T prefab, int count) where T : Component
        {
            if (prefab == null || count <= 0) return;
            if (!Application.isPlaying) return;

            var pool = GetOrCreatePool(prefab);

            for (int i = 0; i < count; i++)
            {
                PrewarmOne(pool);
            }
        }

        public static IEnumerator PrewarmAsync<T>(T prefab, int count, int maxPerFrame) where T : Component
        {
            if (prefab == null || count <= 0) yield break;
            if (!Application.isPlaying) yield break;

            var pool = GetOrCreatePool(prefab);
            int batchSize = Mathf.Max(1, maxPerFrame);

            for (int i = 0; i < count; i++)
            {
                PrewarmOne(pool);

                if ((i + 1) % batchSize == 0)
                    yield return null;
            }
        }

        public static T Spawn<T>(T prefab, Vector3 pos, Quaternion rot, Transform parent = null) where T : Component
        {
            if (prefab == null) return null;

            if (!Application.isPlaying)
            {
                var go = Object.Instantiate(prefab.gameObject, parent);
                var comp = go.GetComponent<T>();
                var editTr = go.transform;
                editTr.SetPositionAndRotation(pos, rot);
                go.SetActive(true);
                (comp as IPoolable)?.New();
                return comp;
            }

            var obj = SpawnInternal(prefab, parent) as T;
            if (obj == null) return null;

            var runtimeTr = obj.transform;
            runtimeTr.SetParent(parent, false);
            runtimeTr.SetPositionAndRotation(pos, rot);

            obj.gameObject.SetActive(true);
            (obj as IPoolable)?.New();

            return obj;
        }

        public static T Spawn<T>(T prefab, Transform parent) where T : Component
        {
            if (prefab == null) return null;

            if (!Application.isPlaying)
            {
                var go = Object.Instantiate(prefab.gameObject, parent);
                var comp = go.GetComponent<T>();
                var editTr = go.transform;
                editTr.localPosition = Vector3.zero;
                editTr.localRotation = Quaternion.identity;
                editTr.localScale = Vector3.one;
                go.SetActive(true);
                (comp as IPoolable)?.New();
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
            (obj as IPoolable)?.New();

            return obj;
        }

        private static Component SpawnInternal(Component prefab, Transform parent)
        {
            if (prefab == null) return null;

            var pool = GetOrCreatePool(prefab);

            IPoolable instance = null;
            if (pool.Inactive.Count > 0)
            {
                instance = pool.Inactive.Pop();
            }
            else
            {
                var go = Object.Instantiate(pool.PrefabComponent.gameObject, pool.Root);
                instance = go.GetComponent<IPoolable>();
                if (instance == null) instance = go.AddComponent<GenericPoolable>();
            }

            pool.Active.Add(instance);
            PoolByInstance[instance] = pool;
            return ((Component)instance).gameObject.GetComponent(prefab.GetType());
        }

        private static Pool GetOrCreatePool(Component prefab)
        {
            int key = prefab.gameObject.GetInstanceID();
            if (Pools.TryGetValue(key, out var pool))
                return pool;

            var root = GetOrCreateRoot();
            var poolName = $"[Pool]_{prefab.name}";

            pool = new Pool
            {
                PrefabComponent = prefab,
                Root = new GameObject(poolName).transform
            };

            if (root != null && pool.Root.parent != root)
                pool.Root.SetParent(root, false);

            Pools[key] = pool;
            return pool;
        }

        private static void PrewarmOne(Pool pool)
        {
            var go = Object.Instantiate(pool.PrefabComponent.gameObject, pool.Root);
            var instance = go.GetComponent<IPoolable>();
            if (instance == null) instance = go.AddComponent<GenericPoolable>();
            go.SetActive(false);
            pool.Inactive.Push(instance);
        }

        public static void Despawn(Component poolableComp)
        {
            if (poolableComp == null) return;

            IPoolable poolable = poolableComp as IPoolable;
            if (poolable == null)
                poolable = poolableComp.GetComponent<IPoolable>();

            if (poolable == null)
            {
                if (!Application.isPlaying) Object.DestroyImmediate(poolableComp.gameObject);
                else Object.Destroy(poolableComp.gameObject);
                return;
            }

            if (!Application.isPlaying)
            {
                Object.DestroyImmediate(poolableComp.gameObject);
                return;
            }

            if (!PoolByInstance.TryGetValue(poolable, out var pool))
            {
                Object.Destroy(poolableComp.gameObject);
                return;
            }

            poolable.Free();

            poolableComp.gameObject.SetActive(false);
            poolableComp.transform.SetParent(pool.Root, false);

            pool.Active.Remove(poolable);
            pool.Inactive.Push(poolable);
        }

        public static Transform GetRoot()
        {
            return GetOrCreateRoot();
        }

        private static Transform GetOrCreateRoot()
        {
            if (_root != null) return _root;

            var rootGo = new GameObject(RootName);
            _root = rootGo.transform;
            return _root;
        }
    }
}
