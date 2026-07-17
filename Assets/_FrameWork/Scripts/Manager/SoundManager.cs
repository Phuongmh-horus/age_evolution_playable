using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Random = UnityEngine.Random;

public class SoundManager : MonoSingleton<SoundManager>
{
    [Serializable]
    private struct CachedSfxData
    {
        public AudioClip Clip;
        public float Volume;
    }

    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private AudioSource bgMusicSource;
    [SerializeField] private AudioSource fxMusicSource;
    [SerializeField] private Transform fallbackAudioAnchor;

    [SerializeField] public SoundDataSO soundDataSO;
    [SerializeField] public List<AudioClip> backgroundMusics;
    [SerializeField] private AudioClipName[] prewarmSfxClips = { AudioClipName.SFX_CharacterAttack };

    private readonly Dictionary<AudioClipName, CachedSfxData> _sfxCache = new Dictionary<AudioClipName, CachedSfxData>(32);
    private readonly Dictionary<AudioClip, float> _lastPlayedSfxTime = new Dictionary<AudioClip, float>(32);
    private const float SFX_SPAM_THRESHOLD = 0.05f;
    private bool _soundDataCacheWarmed;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (soundDataSO == null)
        {
            soundDataSO = Resources.Load<SoundDataSO>("Sound/SoundDataSO");
        }
    }
#endif

    protected override void Awake()
    {
        base.Awake();
        GameEventBus.OnChangeSoundFx = OnSoundFxChange;
        GameEventBus.OnGameStart += EnableBGM;

        if (soundDataSO == null)
        {
            soundDataSO = Resources.Load<SoundDataSO>("Sound/SoundDataSO");
        }

        WarmupSoundDataCache();
        if (bgMusicSource != null)
        {
            bgMusicSource.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {

        GameEventBus.OnChangeSoundFx -= OnSoundFxChange;
        GameEventBus.OnGameStart -= EnableBGM;
    }

    private void EnableBGM()
    {
        if (bgMusicSource != null)
        {
            bgMusicSource.gameObject.SetActive(true);
        }
    }

    private void Start()
    {
        OnSoundFxChange(1);
        PrewarmConfiguredSfx();
    }

    private void OnSoundFxChange(float currentValue)
    {
        if (audioMixer == null) return;

        currentValue *= 2; // sound fx có âm lượng gấp đôi
        var soundValue = currentValue == 0 ? -80f : Mathf.Log10(currentValue) * 20;

        var parameterName = Enum.GetName(typeof(SoundMixerGroup), SoundMixerGroup.SoundFx);
        audioMixer.SetFloat(parameterName, soundValue);
    }

    /// <summary>
    /// Phát một âm thanh với Mixer là Sound
    /// </summary>
    public void PlayOneShot(AudioClip clip, float volume = 1f)
    {
        if (clip == null) return;

        if (_lastPlayedSfxTime.TryGetValue(clip, out float lastTime))
        {
            if (Time.time - lastTime < SFX_SPAM_THRESHOLD) return;
        }
        _lastPlayedSfxTime[clip] = Time.time;

        if (fxMusicSource == null)
        {
            // [FIX] Last resort fallback for Luna - use PlayClipAtPoint
            AudioSource.PlayClipAtPoint(clip, GetFallbackAudioPosition(), volume);
            return;
        }

        fxMusicSource.PlayOneShot(clip, volume);
    }

    public void PlayOneShot(AudioClipName clipName)
    {
        if (clipName == AudioClipName.None) return;

        if (!TryResolveSfx(clipName, out var clipToPlay, out var volume)) return;
        PlayOneShot(clipToPlay, volume);
    }

    public bool TryPlayOneShot(AudioClipName clipName)
    {
        if (clipName != AudioClipName.None &&
            TryResolveSfx(clipName, out var primaryClip, out var primaryVolume))
        {
            PlayOneShot(primaryClip, primaryVolume);
            return true;
        }

        return false;
    }

    public void StopOneShot()
    {
        if (fxMusicSource == null) return;
        fxMusicSource.Stop();
    }

    private void WarmupSoundDataCache()
    {
        if (_soundDataCacheWarmed) return;
        _soundDataCacheWarmed = true;

        if (soundDataSO == null) return;
        soundDataSO.RebuildCache();
    }

    private void PrewarmConfiguredSfx()
    {
        if (prewarmSfxClips == null || prewarmSfxClips.Length == 0) return;

        for (int i = 0; i < prewarmSfxClips.Length; i++)
        {
            var clipName = prewarmSfxClips[i];
            if (clipName == AudioClipName.None) continue;
            TryResolveSfx(clipName, out _, out _);
        }
    }

    private bool TryResolveSfx(AudioClipName clipName, out AudioClip clipToPlay, out float volume)
    {
        if (_sfxCache.TryGetValue(clipName, out var cached) && cached.Clip != null)
        {
            clipToPlay = cached.Clip;
            volume = cached.Volume;
            return true;
        }

        clipToPlay = null;
        volume = 1f;

        if (soundDataSO == null)
            soundDataSO = Resources.Load<SoundDataSO>("Sound/SoundDataSO");

        WarmupSoundDataCache();

        if (soundDataSO != null)
        {
            var soundData = soundDataSO.GetSoundData(clipName);
            if (soundData != null && soundData.Clip != null)
            {
                clipToPlay = soundData.Clip;
                volume = soundData.VolumeDefault;
            }
        }

        if (clipToPlay == null)
        {
            string clipPath = "Sound/" + clipName;
            clipToPlay = Resources.Load<AudioClip>(clipPath);
        }

        if (clipToPlay == null) return false;

        _sfxCache[clipName] = new CachedSfxData
        {
            Clip = clipToPlay,
            Volume = volume
        };

        return true;
    }

    private Vector3 GetFallbackAudioPosition()
    {
        if (fallbackAudioAnchor != null)
            return fallbackAudioAnchor.position;

        if (CameraFollow.Instance != null)
        {
            var cam = CameraFollow.Instance.GetCamera();
            if (cam != null) return cam.transform.position;
        }

        if (CameraManager.Instance != null)
        {
            var follow = CameraManager.Instance.GetCameraFollow();
            if (follow != null)
            {
                var cam = follow.GetCamera();
                if (cam != null) return cam.transform.position;
            }
        }

        return Vector3.zero;
    }

    /// <summary>
    /// Simple remap utility
    /// </summary>
    private static float Remap(float value, float fromMin, float fromMax, float toMin, float toMax)
    {
        float t = Mathf.InverseLerp(fromMin, fromMax, value);
        return Mathf.Lerp(toMin, toMax, t);
    }
}

public enum SoundMixerGroup
{
    BGMusic,
    SoundFx,
}

