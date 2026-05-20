using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class ButtonClickEffect : MonoBehaviour
{
    [Header("Button click particle effects")]
    public ParticleSystem[] particles;

    public void PlayParticles()
    {
        if (particles == null)
            return;

        for (int i = 0; i < particles.Length; i++)
        {
            ParticleSystem ps = particles[i];
            if (ps == null)
                continue;

            int count = 5;
            ParticleSystem.MainModule main = ps.main;
            if (main.maxParticles > 0)
                count = Mathf.Clamp(main.maxParticles, 1, 12);

            ps.Emit(count);
        }
    }
}
