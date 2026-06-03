using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Makes the endcard root clickable and routes clicks to the CTA handler.
public class EndcardClickHandler : MonoBehaviour, IPointerClickHandler
{
    private void Awake()
    {
        // Ensure this GameObject has a Graphic so UI raycasts hit it.
        var img = GetComponent<Image>();
        if (img == null)
        {
            img = gameObject.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0f);
        }
        img.raycastTarget = true;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (LunaUIManager.Instance != null)
        {
            LunaUIManager.Instance.OnCTAClicked();
        }
        else
        {
            // Fallback: directly call playable install if manager missing
            Luna.Unity.Playable.InstallFullGame();
        }
    }
}
