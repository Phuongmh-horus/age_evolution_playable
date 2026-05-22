using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

// Luna playable UI manager.
// [FIX] Removed ConvertLegacyTextToTmp() which called FontEngine.LoadFontFace(Font, int)
public class LunaUIManager : MonoBehaviour
{
    public static LunaUIManager Instance { get; private set; }
    public bool IsTutorialVisible => tutorialLayer != null && tutorialLayer.activeSelf;

    [Header("Tutorial Layer")]
    [SerializeField] private GameObject tutorialLayer;
    [SerializeField] private RectTransform tutorialHand;
    [SerializeField] private RectTransform tutorialHandTrack;
    [SerializeField] private TMP_Text tutorialTMPText;

    [SerializeField] private float handMoveDistance = 200f;
    [SerializeField] private float handMoveDuration = 1.2f;
    [SerializeField] private Vector2 handOffset = Vector2.zero;
    [SerializeField] private float textScaleUp = 1.08f;
    [SerializeField] private float textScaleDuration = 0.7f;

    [Header("Endcard")]
    [SerializeField] private GameObject endcardRoot;
    [SerializeField] private GameObject endcardSingle;
    [SerializeField] private float endcardDelay = 0.5f;
    [SerializeField] private float endcardFadeDuration = 0.4f;
    [SerializeField] private CanvasGroup endcardCanvasGroup;

    [Header("CTA Buttons")]
    [SerializeField] private List<Button> ctaButtons = new List<Button>();
    [SerializeField] private float buttonPulseScale = 1.08f;
    [SerializeField] private float buttonPulseDuration = 0.6f;
    [SerializeField] private float ctaOnlyPulseScale = 1.1f;
    [SerializeField] private float ctaOnlyStartScale = 0.9f;
    [SerializeField] private float ctaImpactStartScale = 1.7f;
    [SerializeField] private float ctaImpactStartAngle = -18f;
    [SerializeField] private float ctaImpactDuration = 0.18f;

    [Header("Behavior")]
    [SerializeField] private bool pauseOnTutorial = true;
    [SerializeField] private bool pauseOnEndcard = true;
    [SerializeField] private bool pauseTimeScaleOnEndcard = false;
    [SerializeField] private bool autoOpenStoreOnEndcard = true;

    private Vector2 _handBaseAnchoredPos;
    private Coroutine _handRoutine;
    private Coroutine _buttonPulseRoutine;
    private Coroutine _textRoutine;
    private Vector3 _textBaseScale = Vector3.one;
    private bool _endGameSent;
    private Coroutine _endcardRoutine;
    private bool _useCtaOnlyPulse;
    private bool _tutorialPauseApplied;

    private void OnEnable()
    {
        GameEventBus.OnGameEnd += HandleGameEnd;
        GameEventBus.OnShowCTA += HandleShowCTA;
    }

    private void OnDisable()
    {
        GameEventBus.OnGameEnd -= HandleGameEnd;
        GameEventBus.OnShowCTA -= HandleShowCTA;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (tutorialTMPText == null && tutorialLayer != null)
        {
            tutorialTMPText = tutorialLayer.GetComponentInChildren<TMP_Text>(true);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        EnsureEventSystem();
        if (tutorialLayer != null)
        {
            ShowTutorial(true);
        }

        if (endcardRoot != null)
        {
            endcardRoot.SetActive(false);
        }

        SetCTAButtonsVisible(false);

        EnsureEndcardCanvasGroup();
        WireCTAButtons();

        if (tutorialHand != null)
        {
            _handBaseAnchoredPos = tutorialHand.anchoredPosition;
        }

        if (tutorialTMPText != null)
        {
            _textBaseScale = tutorialTMPText.rectTransform.localScale;
        }
    }

    private void Update()
    {
        if (tutorialLayer != null && tutorialLayer.activeSelf)
        {
            if (pauseOnTutorial && !_tutorialPauseApplied)
            {
                ForcePauseGame();
                _tutorialPauseApplied = true;
            }

            if (Input.GetMouseButtonDown(0) || Input.touchCount > 0)
            {
                StartGameplayFromTutorial();
            }
        }
    }

    private void ShowTutorial(bool show)
    {
        if (tutorialLayer != null) tutorialLayer.SetActive(show);
        if (show) _tutorialPauseApplied = false;

        if (show)
        {
            StartHandAnimation();
            StartTextPulse();
        }
        else
        {
            StopHandAnimation();
            StopTextPulse();
        }
    }

    private void StartGameplayFromTutorial()
    {
        ShowTutorial(false);

        if (GameplayManager.Instance != null)
        {
            GameplayManager.IsGameStarted = true;
            GameplayManager.Instance.ContinueGame();
        }

        GameEventBus.OnGameStart?.Invoke();
    }

    private void HandleGameEnd(bool isWin)
    {
        ShowEndcard();
    }

    private void HandleShowCTA()
    {
        SetCTAButtonsVisible(true);
    }

    public void ShowCtaOnlyEndgame()
    {
        EnsureEventSystem();
        if (tutorialLayer != null) tutorialLayer.SetActive(false);
        if (endcardRoot != null) endcardRoot.SetActive(false);

        _useCtaOnlyPulse = true;
        SetCTAButtonsVisible(true);
    }

    private void ShowEndcard()
    {
        ShowTutorial(false);

        EnsureEventSystem();
        if (_endcardRoutine != null) StopCoroutine(_endcardRoutine);
        _endcardRoutine = StartCoroutine(EndcardRoutine());
    }

    private IEnumerator EndcardRoutine()
    {
        float delay = Mathf.Max(0f, endcardDelay);
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        EnsureEndcardCanvasGroup();
        if (endcardRoot != null) endcardRoot.SetActive(true);
        if (endcardSingle != null) endcardSingle.SetActive(true);

        if (endcardCanvasGroup != null)
        {
            endcardCanvasGroup.alpha = 0f;
            endcardCanvasGroup.interactable = false;
            endcardCanvasGroup.blocksRaycasts = false;
        }

        SetCTAButtonsVisible(true);
        yield return FadeInEndcard();

        if (endcardCanvasGroup != null)
        {
            endcardCanvasGroup.interactable = true;
            endcardCanvasGroup.blocksRaycasts = true;
        }

        SendEndGameIfNeeded();
        if (autoOpenStoreOnEndcard)
        {
            OpenStoreNow();
        }

        if (pauseOnEndcard)
        {
            ForcePauseGame();
            if (pauseTimeScaleOnEndcard) Time.timeScale = 0f;
        }

        _endcardRoutine = null;
    }

    private void EnsureEndcardCanvasGroup()
    {
        if (endcardCanvasGroup != null) return;
        if (endcardRoot == null) return;
        endcardCanvasGroup = endcardRoot.GetComponent<CanvasGroup>();
        if (endcardCanvasGroup == null)
        {
            endcardCanvasGroup = endcardRoot.AddComponent<CanvasGroup>();
        }
    }

    private IEnumerator FadeInEndcard()
    {
        if (endcardCanvasGroup == null) yield break;

        float duration = Mathf.Max(0.05f, endcardFadeDuration);
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            endcardCanvasGroup.alpha = k;
            yield return null;
        }

        endcardCanvasGroup.alpha = 1f;
    }

    private void SetCTAButtonsVisible(bool visible)
    {
        if (ctaButtons == null) return;
        for (int i = 0; i < ctaButtons.Count; i++)
        {
            if (ctaButtons[i] != null)
            {
                ctaButtons[i].gameObject.SetActive(visible);
                ctaButtons[i].interactable = visible;

                var graphic = ctaButtons[i].targetGraphic;
                if (graphic != null) graphic.raycastTarget = true;
            }
        }

        if (visible)
        {
            WireCTAButtons();
            if (_useCtaOnlyPulse)
            {
                for (int i = 0; i < ctaButtons.Count; i++)
                {
                    var btn = ctaButtons[i];
                    if (btn == null) continue;
                    btn.transform.localScale = new Vector3(ctaOnlyStartScale, ctaOnlyStartScale, ctaOnlyStartScale);
                }
            }
            StartButtonPulse();
        }
        else
        {
            StopButtonPulse();
        }
    }

    public void OnCTAClicked()
    {
        GameEventBus.OnCTAClicked?.Invoke();
        OpenStoreNow();
    }

    private void ForcePauseGame()
    {
        if (GameplayManager.Instance == null) return;
        if (GameplayManager.IsGameStarted)
        {
            GameplayManager.IsGameStarted = false;
        }
        GameplayManager.Instance.PauseGame();
    }

    private void SendEndGameIfNeeded()
    {
        if (_endGameSent) return;
        _endGameSent = true;
        Luna.Unity.LifeCycle.GameEnded();
    }

    private void OpenStoreNow()
    {
        SendEndGameIfNeeded();
        Luna.Unity.Playable.InstallFullGame();
    }

    private void StartHandAnimation()
    {
        if (tutorialHand == null) return;
        if (_handRoutine != null) StopCoroutine(_handRoutine);
        _handRoutine = StartCoroutine(HandMoveRoutine());
    }

    private void StopHandAnimation()
    {
        if (_handRoutine != null)
        {
            StopCoroutine(_handRoutine);
            _handRoutine = null;
        }

        if (tutorialHand != null)
        {
            tutorialHand.anchoredPosition = _handBaseAnchoredPos;
        }
    }

    private void StartTextPulse()
    {
        if (tutorialTMPText == null) return;
        if (_textRoutine != null) StopCoroutine(_textRoutine);
        _textRoutine = StartCoroutine(TextPulseRoutine());
    }

    private void StopTextPulse()
    {
        if (_textRoutine != null)
        {
            StopCoroutine(_textRoutine);
            _textRoutine = null;
        }

        if (tutorialTMPText != null)
        {
            tutorialTMPText.rectTransform.localScale = _textBaseScale;
        }
    }

    private void StartButtonPulse()
    {
        if (_buttonPulseRoutine != null) StopCoroutine(_buttonPulseRoutine);
        _buttonPulseRoutine = StartCoroutine(ButtonImpactThenPulseRoutine());
    }

    private void StopButtonPulse()
    {
        if (_buttonPulseRoutine != null)
        {
            StopCoroutine(_buttonPulseRoutine);
            _buttonPulseRoutine = null;
        }

        if (ctaButtons == null) return;
        for (int i = 0; i < ctaButtons.Count; i++)
        {
            if (ctaButtons[i] != null)
            {
                ctaButtons[i].transform.localScale = Vector3.one;
            }
        }
    }

    private IEnumerator ButtonImpactThenPulseRoutine()
    {
        float impactDuration = Mathf.Max(0.05f, ctaImpactDuration);
        float impactT = 0f;
        while (impactT < impactDuration)
        {
            impactT += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(impactT / impactDuration);
            float scale = Mathf.Lerp(ctaImpactStartScale, 1f, k);
            float angle = Mathf.Lerp(ctaImpactStartAngle, 0f, k);

            for (int i = 0; i < ctaButtons.Count; i++)
            {
                var btn = ctaButtons[i];
                if (btn == null || !btn.gameObject.activeInHierarchy) continue;
                var rt = btn.transform as RectTransform;
                if (rt != null)
                {
                    rt.localScale = new Vector3(scale, scale, scale);
                    rt.localRotation = Quaternion.Euler(0f, 0f, angle);
                }
                else
                {
                    btn.transform.localScale = new Vector3(scale, scale, scale);
                    btn.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                }
            }

            yield return null;
        }

        for (int i = 0; i < ctaButtons.Count; i++)
        {
            var btn = ctaButtons[i];
            if (btn == null) continue;
            btn.transform.localRotation = Quaternion.identity;
        }

        float duration = Mathf.Max(0.05f, buttonPulseDuration);
        while (ctaButtons != null && ctaButtons.Count > 0)
        {
            float t = Mathf.PingPong(Time.unscaledTime / duration, 1f);
            float scale;
            if (_useCtaOnlyPulse)
            {
                scale = Mathf.Lerp(ctaOnlyStartScale, ctaOnlyPulseScale, t);
            }
            else
            {
                float targetScale = buttonPulseScale;
                scale = Mathf.Lerp(1f, targetScale, t);
            }
            for (int i = 0; i < ctaButtons.Count; i++)
            {
                var btn = ctaButtons[i];
                if (btn == null || !btn.gameObject.activeInHierarchy) continue;
                btn.transform.localScale = new Vector3(scale, scale, scale);
            }
            yield return null;
        }

        _buttonPulseRoutine = null;
    }

    private void WireCTAButtons()
    {
        if (ctaButtons == null) return;
        for (int i = 0; i < ctaButtons.Count; i++)
        {
            var btn = ctaButtons[i];
            if (btn == null) continue;
            btn.onClick.RemoveListener(OnCTAClicked);
            btn.onClick.AddListener(OnCTAClicked);
        }
    }

    private void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    private IEnumerator TextPulseRoutine()
    {
        if (tutorialTMPText == null) yield break;

        var rect = tutorialTMPText.rectTransform;
        var from = _textBaseScale;
        var to = _textBaseScale * textScaleUp;
        float duration = Mathf.Max(0.05f, textScaleDuration);

        while (tutorialLayer != null && tutorialLayer.activeSelf)
        {
            float t = Mathf.PingPong(Time.unscaledTime / duration, 1f);
            rect.localScale = Vector3.Lerp(from, to, t);
            yield return null;
        }

        rect.localScale = _textBaseScale;
        _textRoutine = null;
    }

    private IEnumerator HandMoveRoutine()
    {
        if (tutorialHand == null) yield break;

        var basePos = GetTrackAnchoredPosition() + handOffset;

        float duration = Mathf.Max(0.1f, handMoveDuration);
        while (tutorialLayer != null && tutorialLayer.activeSelf)
        {
            float t = Mathf.PingPong(Time.unscaledTime / duration, 1f);
            float x = Mathf.Lerp(-handMoveDistance, handMoveDistance, t);
            tutorialHand.anchoredPosition = basePos + new Vector2(x, 0f);
            yield return null;
        }

        tutorialHand.anchoredPosition = basePos;
        _handRoutine = null;
    }

    private Vector2 GetTrackAnchoredPosition()
    {
        if (tutorialHand == null) return Vector2.zero;
        if (tutorialHandTrack == null) return _handBaseAnchoredPos;

        var parentRect = tutorialHand.parent as RectTransform;
        if (parentRect == null) return tutorialHandTrack.anchoredPosition;

        var canvas = tutorialHand.GetComponentInParent<Canvas>();
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            cam = canvas.worldCamera;
            if (cam == null && CameraFollow.Instance != null)
                cam = CameraFollow.Instance.GetCamera();
            if (cam == null && CameraManager.Instance != null)
            {
                var follow = CameraManager.Instance.GetCameraFollow();
                if (follow != null) cam = follow.GetCamera();
            }
        }

        var screenPos = RectTransformUtility.WorldToScreenPoint(cam, tutorialHandTrack.position);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPos, cam, out var localPos))
        {
            return localPos;
        }

        return tutorialHandTrack.anchoredPosition;
    }
}