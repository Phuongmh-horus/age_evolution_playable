using UnityEngine;
using TMPro;

/// <summary>
/// Warp TextMeshProUGUI along a user-defined AnimationCurve.
/// X (0..1) maps across the line width; Y from the curve is scaled by Amplitude.
/// Characters are oriented by the curve tangent (finite difference).
/// Works in Edit & Play mode via OnPreRenderText (no per-frame ForceMeshUpdate loop).
/// </summary>
[ExecuteAlways]
[AddComponentMenu("UI/TextMeshPro - Curved by AnimationCurve")]
[RequireComponent(typeof(TextMeshProUGUI))]
public class CurvedTMP_ByAnimationCurve : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TextMeshProUGUI m_Text;

    [Header("Curve")]
    [Tooltip("X domain is 0..1 across each line. Y is multiplied by Amplitude.")]
    [SerializeField] private AnimationCurve m_Curve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.25f, 0.2f),
        new Keyframe(0.5f, 0f),
        new Keyframe(0.75f, -0.2f),
        new Keyframe(1f, 0f)
    );

    [Tooltip("Scales curve's Y output (in TMP local units, e.g. pixels in Screen Space Canvas).")]
    [SerializeField] private float m_Amplitude = 60f;

    [Tooltip("Additional vertical offset after warping (local units).")]
    [SerializeField] private float m_VerticalOffset = 0f;

    [Header("Mapping")]
    [Tooltip("Normalize X across the line extents (true) or the whole text bounds (false).")]
    [SerializeField] private bool m_PerLineNormalize = true;

    [Tooltip("If true, curve direction is flipped horizontally.")]
    [SerializeField] private bool m_FlipX = false;

    [Tooltip("Rotates glyphs to follow the curve tangent.")]
    [SerializeField] private bool m_OrientToTangent = true;

    [Tooltip("Degrees added after tangent-based rotation.")]
    [SerializeField] private float m_ExtraRotation = 0f;

    [Tooltip("Finite-difference step on curve domain (0..1) to estimate tangent.")]
    [SerializeField] [Range(0.0001f, 0.05f)] private float m_DeltaT = 0.01f;

    // ---- Lifecycle ----
    private void Awake()
    {
        EnsureText();
    }

    private void OnEnable()
    {
        EnsureText();
        m_Text.OnPreRenderText -= HandlePreRenderText; // avoid double-subscribe
        m_Text.OnPreRenderText += HandlePreRenderText;
        MarkDirty();
    }

    private void OnDisable()
    {
        if (m_Text != null) m_Text.OnPreRenderText -= HandlePreRenderText;
    }

    private void OnDestroy()
    {
        if (m_Text != null) m_Text.OnPreRenderText -= HandlePreRenderText;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        EnsureText();
        m_DeltaT = Mathf.Clamp(m_DeltaT, 0.0001f, 0.05f);
        MarkDirty();
    }
#endif

    private void EnsureText()
    {
        if (m_Text == null) m_Text = GetComponent<TextMeshProUGUI>();
    }

    private void MarkDirty()
    {
        if (m_Text == null) return;
        m_Text.havePropertiesChanged = true;
        m_Text.SetVerticesDirty();
    }

    // ---- Core Warp ----
    private void HandlePreRenderText(TMP_TextInfo textInfo)
    {
        if (textInfo == null || textInfo.characterCount == 0) return;

        // Precompute global bounds if needed
        float globalMinX = float.PositiveInfinity, globalMaxX = float.NegativeInfinity;
        if (!m_PerLineNormalize)
        {
            for (int li = 0; li < textInfo.lineCount; li++)
            {
                var ext = textInfo.lineInfo[li].lineExtents;
                globalMinX = Mathf.Min(globalMinX, ext.min.x);
                globalMaxX = Mathf.Max(globalMaxX, ext.max.x);
            }
            if (Mathf.Approximately(globalMaxX, globalMinX))
                globalMaxX = globalMinX + 0.0001f;
        }

        // Per-character warp
        for (int i = 0; i < textInfo.characterCount; i++)
        {
            var ch = textInfo.characterInfo[i];
            if (!ch.isVisible) continue;

            int matIndex    = ch.materialReferenceIndex;
            int vertexIndex = ch.vertexIndex;
            var verts       = textInfo.meshInfo[matIndex].vertices;

            // Mid baseline position of this char (use average of quad 0 & 2 X, and baseline Y)
            Vector3 mid = new Vector3(
                (verts[vertexIndex + 0].x + verts[vertexIndex + 2].x) * 0.5f,
                ch.baseLine,
                0f
            );

            // Make local to midpoint
            verts[vertexIndex + 0] -= mid;
            verts[vertexIndex + 1] -= mid;
            verts[vertexIndex + 2] -= mid;
            verts[vertexIndex + 3] -= mid;

            // Normalize X across chosen bounds → t in [0..1]
            float t;
            if (m_PerLineNormalize)
            {
                int line = ch.lineNumber;
                var ext = textInfo.lineInfo[line].lineExtents;
                float minX = ext.min.x;
                float maxX = ext.max.x;
                if (Mathf.Approximately(maxX, minX)) maxX = minX + 0.0001f;
                t = (mid.x - minX) / (maxX - minX);
            }
            else
            {
                t = (mid.x - globalMinX) / (globalMaxX - globalMinX);
            }

            if (m_FlipX) t = 1f - t;
            t = Mathf.Clamp01(t);

            // Evaluate curve & tangent
            float y = m_Curve.Evaluate(t) * m_Amplitude; // vertical displacement
            float t0 = Mathf.Clamp01(t - m_DeltaT);
            float t1 = Mathf.Clamp01(t + m_DeltaT);
            float y0 = m_Curve.Evaluate(t0) * m_Amplitude;
            float y1 = m_Curve.Evaluate(t1) * m_Amplitude;

            // Derivative dy/dx in local UI units:
            // dx in UI units equals (domain span)/N; here we map domain directly across line width,
            // so use finite difference w.r.t. normalized t then scale by actual width.
            float widthUnits;
            if (m_PerLineNormalize)
            {
                var ext = textInfo.lineInfo[ch.lineNumber].lineExtents;
                widthUnits = Mathf.Max(0.0001f, ext.max.x - ext.min.x);
            }
            else
            {
                widthUnits = Mathf.Max(0.0001f, globalMaxX - globalMinX);
            }
            float dy = (y1 - y0);
            float dx = (t1 - t0) * widthUnits;
            float slope = dy / Mathf.Max(0.0001f, dx); // rise over run

            // Position & rotation
            float angleDeg = m_OrientToTangent ? Mathf.Atan(slope) * Mathf.Rad2Deg : 0f;
            angleDeg += m_ExtraRotation;

            Vector3 targetCenter = new Vector3(mid.x, ch.baseLine + y + m_VerticalOffset, 0f);
            Matrix4x4 M = Matrix4x4.TRS(targetCenter, Quaternion.Euler(0, 0, angleDeg), Vector3.one);

            // Apply transform
            verts[vertexIndex + 0] = M.MultiplyPoint3x4(verts[vertexIndex + 0]);
            verts[vertexIndex + 1] = M.MultiplyPoint3x4(verts[vertexIndex + 1]);
            verts[vertexIndex + 2] = M.MultiplyPoint3x4(verts[vertexIndex + 2]);
            verts[vertexIndex + 3] = M.MultiplyPoint3x4(verts[vertexIndex + 3]);
        }

        // Upload vertices back
        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            var mi = textInfo.meshInfo[i];
            mi.mesh.vertices = mi.vertices;
            m_Text.UpdateGeometry(mi.mesh, i);
        }
    }
}