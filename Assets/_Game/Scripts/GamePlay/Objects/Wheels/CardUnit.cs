using GamePlay.Entities;
using System.Collections.Generic;
using UnityEngine;

namespace GamePlay.Crushers
{
    public class CardUnit : PoolEntity
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly Dictionary<int, Material> RuntimeCardMaterials = new Dictionary<int, Material>();

        [SerializeField] protected MeshRenderer meshRenderer;
        [SerializeField] protected MeshRenderer outlineRenderer;
        [SerializeField] protected SpriteRenderer spriteRenderer;
        [Header("Luna Color Stabilization")]
        [SerializeField] private bool forceDiffuseLightingForCard = true;

        private readonly List<MeshRenderer> _resolvedOutlineRenderers = new List<MeshRenderer>(4);
        private MaterialPropertyBlock _mpb;
        private int _meshColorPropertyId = -1;
        private bool _hasEmissionProperty;
        private bool _fallbackVisualCached;
        private Color _defaultMeshColor = Color.white;
        private Color _defaultSpriteColor = Color.white;

        public CardType Type { get; private set; } = CardType.Character;
        public int CardId { get; private set; }
        public int CardLevel { get; private set; } = 1;

        public void Initialize(Material material, Sprite sprite)
        {
            Type = CardType.Character;
            CardLevel = Mathf.Max(1, CardLevel);
            CardId = CardLevel;
            if (meshRenderer != null)
            {
                meshRenderer.sharedMaterial = ResolveRuntimeCardMaterial(material);
            }
            if (spriteRenderer != null) spriteRenderer.sprite = sprite;
            CacheFallbackVisualState();
            SetEnableOutline(false);
        }

        public void Initialize(CardType type, int id, int level, Material material, Sprite sprite)
        {
            Type = type;
            CardId = id;
            CardLevel = Mathf.Max(1, level);
            if (meshRenderer != null)
            {
                meshRenderer.sharedMaterial = ResolveRuntimeCardMaterial(material);
            }
            if (spriteRenderer != null) spriteRenderer.sprite = sprite;
            CacheFallbackVisualState();
            SetEnableOutline(false);
        }

        private Material ResolveRuntimeCardMaterial(Material sourceMaterial)
        {
            if (sourceMaterial == null) return null;
            if (!forceDiffuseLightingForCard) return sourceMaterial;

            int key = sourceMaterial.GetInstanceID();
            if (RuntimeCardMaterials.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            // Clone once per source material to avoid per-card material instancing.
            var runtimeMaterial = new Material(sourceMaterial)
            {
                name = sourceMaterial.name + " [CardRuntime]"
            };
            ApplyLunaCardColorFix(runtimeMaterial);
            RuntimeCardMaterials[key] = runtimeMaterial;
            return runtimeMaterial;
        }

        private void ApplyLunaCardColorFix(Material runtimeMaterial)
        {
            if (!forceDiffuseLightingForCard) return;
            if (runtimeMaterial == null) return;

            // Card textures are grayscale and depend on material tint.
            // Keeping DIFFUSE off makes them overly sensitive to ambient sky tint on Luna.
            if (runtimeMaterial.HasProperty("_Diffuse"))
            {
                runtimeMaterial.SetFloat("_Diffuse", 1f);
                runtimeMaterial.EnableKeyword("DIFFUSE");
            }

            if (runtimeMaterial.HasProperty("_DiffuseWrap")) runtimeMaterial.SetFloat("_DiffuseWrap", 0.07f);
            if (runtimeMaterial.HasProperty("_DiffuseBrightness")) runtimeMaterial.SetFloat("_DiffuseBrightness", 1f);
            if (runtimeMaterial.HasProperty("_DiffuseContrast")) runtimeMaterial.SetFloat("_DiffuseContrast", 1f);
        }

        public void SetEnableOutline(bool enable)
        {
            var renderers = GetOutlineRenderers();
            if (renderers.Count > 0)
            {
                for (int i = 0; i < renderers.Count; i++)
                {
                    if (renderers[i] != null) renderers[i].enabled = enable;
                }

                // If real outline renderers exist, keep body color unchanged.
                ApplyFallbackGlow(false);
                return;
            }

            ApplyFallbackGlow(enable);
        }

        private List<MeshRenderer> GetOutlineRenderers()
        {
            _resolvedOutlineRenderers.Clear();

            if (outlineRenderer != null && outlineRenderer != meshRenderer)
            {
                _resolvedOutlineRenderers.Add(outlineRenderer);
            }

            var renderers = GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer == meshRenderer) continue;
                if (_resolvedOutlineRenderers.Contains(renderer)) continue;
                _resolvedOutlineRenderers.Add(renderer);
            }

            if (outlineRenderer == null && _resolvedOutlineRenderers.Count > 0)
            {
                outlineRenderer = _resolvedOutlineRenderers[0];
            }

            return _resolvedOutlineRenderers;
        }

        private void CacheFallbackVisualState()
        {
            _fallbackVisualCached = true;
            _meshColorPropertyId = -1;
            _hasEmissionProperty = false;
            _defaultMeshColor = Color.white;
            _defaultSpriteColor = spriteRenderer != null ? spriteRenderer.color : Color.white;

            if (meshRenderer == null) return;

            var mat = meshRenderer.sharedMaterial;
            if (mat == null) return;

            if (mat.HasProperty(BaseColorId)) _meshColorPropertyId = BaseColorId;
            else if (mat.HasProperty(ColorId)) _meshColorPropertyId = ColorId;

            if (_meshColorPropertyId != -1)
            {
                _defaultMeshColor = mat.GetColor(_meshColorPropertyId);
            }

            _hasEmissionProperty = mat.HasProperty(EmissionColorId);
        }

        private void ApplyFallbackGlow(bool enable)
        {
            if (!_fallbackVisualCached)
            {
                CacheFallbackVisualState();
            }

            if (spriteRenderer != null)
            {
                spriteRenderer.color = enable
                    ? new Color(1f, 0.95f, 0.35f, _defaultSpriteColor.a)
                    : _defaultSpriteColor;
            }

            if (meshRenderer == null) return;

            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(_mpb);

            if (_meshColorPropertyId != -1)
            {
                Color target = enable
                    ? Color.Lerp(_defaultMeshColor, new Color(1f, 0.92f, 0.25f, _defaultMeshColor.a), 0.75f)
                    : _defaultMeshColor;
                target.a = _defaultMeshColor.a;
                _mpb.SetColor(_meshColorPropertyId, target);
            }

            if (_hasEmissionProperty)
            {
                _mpb.SetColor(EmissionColorId, enable ? new Color(0.9f, 0.8f, 0.15f) * 2.5f : Color.black);
            }

            meshRenderer.SetPropertyBlock(_mpb);
        }
    }

    public enum CardType
    {
        Hero = 0,
        Character = 1,
        Solider = Character,
    }
}
