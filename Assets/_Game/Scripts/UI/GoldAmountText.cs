using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class CurrencyAmountText : MonoBehaviour
{
    [SerializeField] private TMP_Text goldText;
    [SerializeField] private Image goldIcon;
    [SerializeField] private string format = "{0}";
    [SerializeField] private bool useThousandsSeparator = true;
    [SerializeField] private CurrencyType currencyKind = CurrencyType.Gold;

    [Header("Gain Animation")]
    [SerializeField] private bool animateOnGoldGain = true;
    [SerializeField] private RectTransform gainIconPrefab;
    [SerializeField] private int gainBurstCount = 6;
    [SerializeField] private float gainBurstDuration = 0.5f;
    [SerializeField] private Vector2 gainBurstSpread = new Vector2(70f, 35f);
    [SerializeField] private Vector3 worldSourceOffset = new Vector3(0f, 0.6f, 0f);
    [SerializeField] private Canvas animationCanvas;
    [SerializeField] private RectTransform animationRoot;

    private readonly List<Image> iconPool = new List<Image>(8);
    private int lastAmount = int.MinValue;

    private void Awake()
    {
        if (goldText == null)
        {
            goldText = GetComponent<TMP_Text>();
        }

        if (goldText == null)
        {
            goldText = GetComponentInChildren<TMP_Text>(true);
        }
        if (goldIcon == null)
        {
            goldIcon = GetComponent<Image>();
        }

        if (goldIcon == null)
        {
            goldIcon = GetComponentInChildren<Image>(true);
        }
    }

    private Coroutine initializeRoutine;

    private void OnEnable()
    {
        initializeRoutine = StartCoroutine(InitializeAfterFrames());
    }

    private void OnDisable()
    {
        if (initializeRoutine != null)
        {
            StopCoroutine(initializeRoutine);
            initializeRoutine = null;
        }

        Unsubscribe();
    }

    private System.Collections.IEnumerator InitializeAfterFrames()
    {
        yield return null;
        yield return null;
        yield return null;

        Subscribe();
        RefreshFromManager();
        initializeRoutine = null;
    }

    private void Subscribe()
    {
        var manager = GameplayManager.Instance;
        if (manager == null)
        {
            return;
        }

        manager.OnCurrencyChanged -= HandleCurrencyChanged;
        manager.OnCurrencyChanged += HandleCurrencyChanged;
    }

    private void Unsubscribe()
    {
        var manager = GameplayManager.Instance;
        if (manager == null)
        {
            return;
        }

        manager.OnCurrencyChanged -= HandleCurrencyChanged;
    }

    private void RefreshFromManager()
    {
        var manager = GameplayManager.Instance;
        if (manager == null)
        {
            return;
        }

        HandleAmountChanged(manager.GetCurrency(currencyKind), Vector3.zero);
    }

    private void HandleCurrencyChanged(CurrencyType type, int amount, Vector3 worldPosition)
    {
        if (type != currencyKind) return;
        HandleAmountChanged(amount, worldPosition);
    }

    private void HandleAmountChanged(int amount)
    {
        HandleAmountChanged(amount, Vector3.zero);
    }

    private void HandleAmountChanged(int amount, Vector3 worldPosition)
    {
        if (goldText == null)
        {
            return;
        }

        bool canAnimateGain = animateOnGoldGain &&
                              gainIconPrefab != null &&
                              goldIcon != null &&
                              worldPosition != Vector3.zero &&
                              lastAmount != int.MinValue &&
                              amount > lastAmount;

        if (canAnimateGain)
        {
            StartCoroutine(PlayGainAnimation(amount, worldPosition, UpdateTextDisplay));
        }
        else
        {
            UpdateTextDisplay(amount);
        }
    }

    private void UpdateTextDisplay(int amount)
    {
        if (goldText == null)
        {
            return;
        }

        string value = useThousandsSeparator ? amount.ToString("N0") : amount.ToString();
        goldText.text = string.Format(format, value);
        lastAmount = amount;
    }

    private IEnumerator PlayGainAnimation(int newAmount, Vector3 worldPosition, Action<int> onComplete = null)
    {
        if (goldText == null || animationCanvas == null)
        {
            yield break;
        }

        Canvas canvas = animationCanvas;
        RectTransform root = animationRoot != null ? animationRoot : canvas.transform as RectTransform;
        if (root == null)
        {
            yield break;
        }

        Camera sourceCamera = GetMainCamera();
        if (sourceCamera == null)
        {
            yield break;
        }

        Vector3 sourceScreen = sourceCamera.WorldToScreenPoint(worldPosition + worldSourceOffset);
        if (sourceScreen.z <= 0f)
        {
            yield break;
        }

        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? GetMainCamera() : null;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sourceScreen, eventCamera, out Vector2 startLocal))
        {
            yield break;
        }

        Vector2 endLocal = root.InverseTransformPoint(goldIcon.rectTransform.position);
        int burstCount = Mathf.Max(1, gainBurstCount);
        var icons = new List<Image>(burstCount);
        for (int i = 0; i < burstCount; i++)
        {
            Image icon = GetOrCreateIcon(root);
            if (icon != null)
            {
                icons.Add(icon);
            }
        }

        if (icons.Count > 0)
        {
            yield return StartCoroutine(AnimateIconsBurst(icons, startLocal, endLocal));
        }

        onComplete?.Invoke(newAmount);
    }

    private IEnumerator AnimateIconsBurst(List<Image> icons, Vector2 startLocal, Vector2 endLocal)
    {
        if (icons == null || icons.Count == 0)
        {
            yield break;
        }

        int count = icons.Count;
        var controls = new Vector2[count];
        var rects = new RectTransform[count];

        for (int i = 0; i < count; i++)
        {
            Image icon = icons[i];
            if (icon == null)
            {
                continue;
            }

            RectTransform iconRect = icon.rectTransform;
            rects[i] = iconRect;
            iconRect.anchoredPosition = startLocal;
            iconRect.localScale = Vector3.one;

            Vector2 randomOffset = new Vector2(
                UnityEngine.Random.Range(-gainBurstSpread.x, gainBurstSpread.x),
                UnityEngine.Random.Range(0f, gainBurstSpread.y));
            controls[i] = Vector2.Lerp(startLocal, endLocal, 0.5f) + randomOffset;

            icon.gameObject.SetActive(true);
        }

        float duration = Mathf.Max(0.05f, gainBurstDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            for (int i = 0; i < count; i++)
            {
                RectTransform iconRect = rects[i];
                if (iconRect == null)
                {
                    continue;
                }

                Vector2 control = controls[i];
                Vector2 p0p1 = Vector2.Lerp(startLocal, control, t);
                Vector2 p1p2 = Vector2.Lerp(control, endLocal, t);
                iconRect.anchoredPosition = Vector2.Lerp(p0p1, p1p2, t);
            }

            yield return null;
        }

        for (int i = 0; i < count; i++)
        {
            RectTransform iconRect = rects[i];
            if (iconRect == null)
            {
                continue;
            }

            iconRect.anchoredPosition = endLocal;
            iconRect.gameObject.SetActive(false);
        }
    }

    private Camera GetMainCamera()
    {
        return Camera.main;
    }

    private Image GetOrCreateIcon(RectTransform parent)
    {
        for (int i = 0; i < iconPool.Count; i++)
        {
            Image pooled = iconPool[i];
            if (pooled == null || pooled.gameObject.activeSelf)
            {
                continue;
            }

            pooled.rectTransform.SetParent(parent, false);
            return pooled;
        }

        RectTransform iconRect = Instantiate(gainIconPrefab, parent, false);
        iconRect.name = "GoldGainIcon";

        Image icon = iconRect.GetComponent<Image>();
        if (icon == null)
        {
            icon = iconRect.gameObject.AddComponent<Image>();
        }

        icon.raycastTarget = false;
        icon.gameObject.SetActive(false);
        iconPool.Add(icon);
        return icon;
    }
}
