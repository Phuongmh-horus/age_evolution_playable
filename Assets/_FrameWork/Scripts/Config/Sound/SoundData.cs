using System;
using UnityEngine;

[Serializable]
public class SoundData
{
    public AudioClipName Name;
    public AudioClip Clip;
    public float VolumeDefault = 1f;
}

public enum AudioClipName
{
    None = 0,

    SFX_CharacterAttack,
    SFX_CharacterDie,
    SFX_ButtonClick,
    SFX_ButtonClose,
    SFX_CardUpgrade,
    SFX_Impact,
    SFX_Impact_1,
    SFX_Ingame_Hero_Upgrade,
    SFX_Ingame_Capacity_LevelUp,
    SFX_MoneyCollect,
    SFX_Level_Complete,
    SFX_FlyCoin,
    SFX_TriggerWheel,
    SFX_DropCard,
    SFX_Timeline_new,
    SFX_Firework,
    SFX_TowerDestroy,
    SFX_RateUs,
    SFX_Coin_Skill,
    SFX_Merge_Weapon,
}