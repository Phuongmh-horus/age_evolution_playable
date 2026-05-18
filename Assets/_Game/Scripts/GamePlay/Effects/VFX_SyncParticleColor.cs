using UnityEngine;

namespace GamePlay.Effects
{
    public partial class VFX_SyncParticleColor : MonoBehaviour
    {
        [Header("Target Particles")]
        [SerializeField] private ParticleSystem[] particles;

        [Header("Shader Settings")]
        [Tooltip("Tên biến màu của Particle đích (Legacy/Mobile thường là _TintColor, Standard là _Color)")]
        [SerializeField] protected string targetPropertyName = "_Color"; // Or _BaseColor

        private ParticleSystemRenderer[] _cachedRenderers;
        private ParticleSystem[] _cachedParticles;
        private MaterialPropertyBlock _propBlock;

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();

            // Cache renderers
            if (particles != null)
            {
                _cachedRenderers = new ParticleSystemRenderer[particles.Length];
                _cachedParticles = new ParticleSystem[particles.Length];
                for (int i = 0; i < particles.Length; i++)
                {
                    var ps = particles[i];
                    if (ps == null) continue;
                    _cachedParticles[i] = ps;
                    _cachedRenderers[i] = ps.GetComponent<ParticleSystemRenderer>();
                }
            }
            else
            {
                // Auto-find if empty
                var ps = GetComponentsInChildren<ParticleSystem>(true);
                _cachedParticles = ps;
                _cachedRenderers = new ParticleSystemRenderer[ps.Length];
                for(int i=0; i<ps.Length; i++) _cachedRenderers[i] = ps[i].GetComponent<ParticleSystemRenderer>();
            }
        }

        public void SyncColorFrom(Renderer sourceMesh)
        {
            if (sourceMesh == null) return;
            if (_cachedRenderers == null || _cachedRenderers.Length == 0) return;

            // Lấy màu nguồn
            Material sourceMat = sourceMesh.sharedMaterial;
            if (sourceMat == null) return;

            Color targetColor = Color.white;
            
            // Try common properties
            if (sourceMat.HasProperty(targetPropertyName))
                targetColor = sourceMat.GetColor(targetPropertyName);
            else if (sourceMat.HasProperty("_BaseColor"))
                targetColor = sourceMat.GetColor("_BaseColor");
            else if (sourceMat.HasProperty("_Color"))
                targetColor = sourceMat.GetColor("_Color");

            // Setup Block
            _propBlock.Clear();
            _propBlock.SetColor(targetPropertyName, targetColor);
            
            // Also try commonly used particle shader properties if default fails
            if (targetPropertyName != "_BaseColor")
                _propBlock.SetColor("_BaseColor", targetColor);
            if (targetPropertyName != "_TintColor")
                _propBlock.SetColor("_TintColor", targetColor);
            if (targetPropertyName != "_Color")
                 _propBlock.SetColor("_Color", targetColor);

            // Appy
            int count = _cachedRenderers.Length;
            for (int i = 0; i < count; i++)
            {
                var renderer = _cachedRenderers[i];
                if (renderer == null) continue;

                renderer.SetPropertyBlock(_propBlock);

                // Fallback: direct main module set (for particles not using PropertyBlock compatible shaders)
                ParticleSystem ps = null;
                if (_cachedParticles != null && i < _cachedParticles.Length)
                {
                    ps = _cachedParticles[i];
                }

                if (ps != null)
                {
                    var main = ps.main;
                    main.startColor = targetColor;
                }
            }
        }
    }
}
