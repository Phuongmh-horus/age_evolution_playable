using System.Collections.Generic;
using UnityEngine;

public class ReturnToMyPool : MonoBehaviour
{
    public MyPool pool;
    [HideInInspector] public bool isInPool;

    public void OnDisable()
    {
        if (pool == null) return;
        pool.AddToPool(gameObject);
    }
}

public class MyPool
{
    private Stack<GameObject> stack = new Stack<GameObject>();
    private readonly GameObject baseObj;
    private readonly Transform poolRoot;
    private GameObject tmp;
    private ReturnToMyPool returnPool;
    private readonly int _maxInactiveCount;

    public MyPool(GameObject baseObj, int maxInactiveCount, Transform rootParent)
    {
        this.baseObj = baseObj;
        _maxInactiveCount = Mathf.Max(1, maxInactiveCount);
        poolRoot = CreateOrGetPoolRoot(baseObj, rootParent);
    }

    public GameObject Get()
    {
        while (stack.Count > 0)
        {
            tmp = stack.Pop();
            if (tmp != null)
            {
                returnPool = tmp.GetComponent<ReturnToMyPool>();
                if (returnPool != null) returnPool.isInPool = false;
                tmp.SetActive(true);
                return tmp;
            }
            else
            {
                Debug.LogWarning($"game object with key {baseObj.name} has been destroyed!");
            }
        }
        tmp = Object.Instantiate(baseObj);
        returnPool = tmp.AddComponent<ReturnToMyPool>();
        returnPool.pool = this;
        returnPool.isInPool = false;
        if (poolRoot != null)
            tmp.transform.SetParent(poolRoot, false);
        return tmp;
    }

    public void AddToPool(GameObject obj)
    {
        if (obj == null) return;

        var tracker = obj.GetComponent<ReturnToMyPool>();
        if (tracker != null)
        {
            if (tracker.isInPool) return;
            tracker.isInPool = true;
        }

        // if (poolRoot != null)
            // obj.transform.SetParent(poolRoot, false);

        stack.Push(obj);
    }

    private static Transform CreateOrGetPoolRoot(GameObject prefab, Transform rootParent)
    {
        if (prefab == null || rootParent == null)
            return rootParent;

        string poolName = $"[Pool]_{prefab.name}";
        var existing = rootParent.Find(poolName);
        if (existing != null)
            return existing;

        var root = new GameObject(poolName).transform;
        root.SetParent(rootParent, false);
        return root;
    }
}

public class PoolManager : MonoBehaviour
{
    #region Singleton

    private static PoolManager _instance;

    public static PoolManager Instance
    {
        get
        {
            return _instance;
        }
    }
    #endregion

    #region Fields
    private Dictionary<GameObject, MyPool> dicPools = new Dictionary<GameObject, MyPool>();
    GameObject tmp;
    private const int HardMaxInactivePerPrefab = 96;
    [SerializeField, Min(1)] private int maxInactivePerPrefab = HardMaxInactivePerPrefab;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        if (_instance != null)
        {
            DestroyImmediate(this.gameObject);
        }
        else
        {
            _instance = this;
            DontDestroyOnLoad(this.gameObject);
        }

        // Enforce runtime hard cap even if old scene/prefab serialized a higher value.
        maxInactivePerPrefab = Mathf.Clamp(maxInactivePerPrefab, 1, HardMaxInactivePerPrefab);
    }
    #endregion

    public GameObject Get(GameObject obj)
    {
        if (dicPools.ContainsKey(obj) == false)
        {
            dicPools.Add(obj, new MyPool(obj, maxInactivePerPrefab, transform));
        }
        return dicPools[obj].Get();
    }

    public GameObject Get(GameObject obj, Vector3 position)
    {
        tmp = Get(obj);
        tmp.transform.position = position;
        return tmp;
    }

    public T Get<T>(T obj) where T : Component
    {
        tmp = Get(obj.gameObject);
        if (tmp == null) return default;
        return tmp.GetComponent<T>();
    }

    public T Get<T>(GameObject obj, Vector3 position) where T : Component
    {
        tmp = Get(obj);
        if (tmp == null) return default;
        tmp.transform.position = position;
        return tmp.GetComponent<T>();
    }
}
