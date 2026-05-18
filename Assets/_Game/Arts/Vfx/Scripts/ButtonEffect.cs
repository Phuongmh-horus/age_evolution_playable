using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.Serialization;

[RequireComponent(typeof(Button))]
public class ButtonClickEffect : MonoBehaviour
{
    [Header("Danh sách Effects (Kéo thả hàng loạt vào đây)")]
    public ParticleSystem[] particles; // Trở về dạng mảng đơn giản để dễ kéo thả
    
    public void PlayParticles()
    {
        // 1. XỬ LÝ PARTICLE - TỰ ĐỘNG THÔNG MINH
        if (particles != null)
        {
            foreach (var ps in particles)
            {
                if (ps == null) continue;

                int count = 5; // Mặc định là 5

                // Kiểm tra xem Particle này có cài đặt Burst trong Inspector không
                var emission = ps.emission;
                if (emission.burstCount > 0)
                {
                    // Lấy số lượng hạt từ dòng Burst đầu tiên của Particle
                    // (Giúp bạn chỉnh 1 hay 10 ngay trên prefab của effect)
                    count = (int)emission.GetBurst(0).count.constantMax;
                }

                ps.Emit(count);
            }
        }
    }
    
}
