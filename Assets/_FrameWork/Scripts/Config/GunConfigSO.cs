using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "GunConfigSO", menuName = "Config/GunConfigSO")]
public class GunConfigSO : ScriptableObject
{
    public List<GunData> GunList = new List<GunData>();

    public GunData GetGunData(int id)
    {
        return GunList.Find(x => x.Id == id);
    }
}

[Serializable]
public struct GunData
{
    public int Id;
    public Transform GunPrefab;
}
