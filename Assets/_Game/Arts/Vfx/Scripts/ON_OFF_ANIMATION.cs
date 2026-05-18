using UnityEngine;

public class VFXManager : MonoBehaviour
{
    [Header("Danh sách các VFX (Kéo thả GameObject vào đây)")]
    public GameObject[] vfxList;

    // Hàm gọi để BẬT (Active) hiệu ứng
    public void PlayVFXByIndex(int index)
    {
        if (index >= 0 && index < vfxList.Length)
        {
            if (vfxList[index] != null)
            {
                vfxList[index].SetActive(true);
            }
        }
        else
        {
            Debug.LogError("Chỉ số VFX không tồn tại!");
        }
    }

    // Hàm gọi để TẮT (Deactive) hiệu ứng
    public void StopVFXByIndex(int index)
    {
        if (index >= 0 && index < vfxList.Length)
        {
            if (vfxList[index] != null)
            {
                vfxList[index].SetActive(false);
            }
        }
    }
}
