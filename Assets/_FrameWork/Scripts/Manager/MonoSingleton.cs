using UnityEngine;

/// <summary>
/// Playable/Luna-safe MonoSingleton:
/// - No external deps (remove KBCore)
/// - Safe auto-create if missing
/// - Per-type cached instance, no shared static Transform/GameObject across types
/// </summary>
public class MonoSingleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;
    private static bool _isQuitting;

    /// <summary> Optional: access cached transform of this singleton instance. </summary>
    public static Transform CachedTransform => Instance != null ? Instance.transform : null;

    /// <summary> Optional: access cached gameObject of this singleton instance. </summary>
    public static GameObject CachedGameObject => Instance != null ? Instance.gameObject : null;

    public static Vector3 Position => CachedTransform != null ? CachedTransform.position : Vector3.zero;
    public static Quaternion Rotation => CachedTransform != null ? CachedTransform.rotation : Quaternion.identity;

    public static T Instance
    {
        get
        {
            if (_instance != null) return _instance;
            if (_isQuitting) return null;

            // Do not auto-create in edit mode to avoid leaking objects on scene close
            if (!Application.isPlaying) return null;

            // Auto-create if missing (playable friendly)
            var go = new GameObject($"{typeof(T).Name}(Singleton)");
            _instance = go.AddComponent<T>();

            return _instance;
        }
    }

    protected virtual void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this as T;
    }

    protected virtual void OnApplicationQuit()
    {
        _isQuitting = true;
        _instance = null;
    }
}
