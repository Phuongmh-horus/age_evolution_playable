using UnityEngine;

[DisallowMultipleComponent]
public class GamePlayTutElement : MonoBehaviour
{
    [SerializeField] private bool isShowTut = true;

    public bool IsShowTut
    {
        get => isShowTut;
        set => isShowTut = value;
    }

    public void Initialize()
    {
        ApplyVisibility();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying) return;
        ApplyVisibility();
    }
#endif

    public void ApplyVisibility()
    {
        gameObject.SetActive(isShowTut);
    }
}
