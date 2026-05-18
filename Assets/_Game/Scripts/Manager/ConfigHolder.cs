using GamePlay.Enemies;
using UnityEngine;

/// <summary>
/// ConfigHolder cho playable:
/// - Giữ các config cần cho flow gameplay (Campaign/Era/Item/Evolution + GamePlayVariable).
/// - Không phụ thuộc menu/shop/reward...
/// </summary>
public class ConfigHolder : MonoSingleton<ConfigHolder>
{
    [Header("Gameplay Configs")]
    public CampaignConfigSO CampaignConfigSO;
    public GamePlayVariable GamePlayVariable;
    public EnemyVariable EnemyVariable;
    public ItemConfigSO ItemConfigSO;

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Auto bind trong Editor để đỡ phải kéo tay.
        if (GamePlayVariable == null)
            GamePlayVariable = Resources.Load<GamePlayVariable>("Variables/GamePlay/GamePlayVariable");

        if (EnemyVariable == null)
            EnemyVariable = Resources.Load<EnemyVariable>("Variables/Enemies/EnemyVariable");
    }
#endif

    public EraDataSO GetCurrentEraConfig()
    {
        if (DataManager.PlayerData == null) 
        {
             // Fallback for Playable Build where DataManager might be empty/null
             if (GameplayManager.Instance != null && GameplayManager.Instance.PlayableEra != null)
             {
                 return GameplayManager.Instance.PlayableEra;
             }
             return null;
        }
        
        var playerData = DataManager.PlayerData.LevelSaveData;
        
        return CampaignConfigSO != null
            ? CampaignConfigSO.GetEraDataById(playerData.TimeLineId, playerData.EraId)
            : null;
    }

    public EvolutionConfigSO GetCurrentEvolutionConfig()
    {
        var era = GetCurrentEraConfig();
        return era != null ? era.EvolutionConfig : null;
    }

    public ItemDataSO GetCurrentItemConfig()
    {
        var era = GetCurrentEraConfig();
        return era != null ? era.ItemConfig : null;
    }

    public string GetEraName(int timelineId, int eraId)
    {
        if (CampaignConfigSO == null) return string.Empty;
        var eraData = CampaignConfigSO.GetEraDataById(timelineId, eraId);
        return eraData != null ? eraData.EraName : string.Empty;
    }
}
