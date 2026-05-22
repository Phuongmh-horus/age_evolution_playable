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

    // PlayerPrefs keys
    private const string IsPlayMusicKey = "IsPlayMusic";
    private const string IsPlaySoundKey = "IsPlaySound";

    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private AudioSource bgMusicSource;
    [SerializeField] private AudioSource fxMusicSource;
    [SerializeField] private Transform fallbackAudioAnchor;

    [SerializeField] public SoundDataSO soundDataSO;
    [SerializeField] public List<AudioClip> backgroundMusics;
    [SerializeField] private AudioClipName[] prewarmSfxClips = { AudioClipName.SFX_CharacterAttack, AudioClipName.SFX_DropCard };

    private readonly Dictionary<AudioClipName, CachedSfxData> _sfxCache = new Dictionary<AudioClipName, CachedSfxData>(32);
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
        GameEventBus.OnChangeSound = OnSoundChange;
        GameEventBus.OnChangeSoundFx = OnSoundFxChange;

        if (soundDataSO == null)
        {
            soundDataSO = Resources.Load<SoundDataSO>("Sound/SoundDataSO");
        }

        WarmupSoundDataCache();

        EnsureAudioSources();
    }

    private void Start()
    {
        var playMusic = PlayerPrefs.GetInt(IsPlayMusicKey, 1);
        var playSound = PlayerPrefs.GetInt(IsPlaySoundKey, 1);
        OnSoundChange(playMusic);
        OnSoundFxChange(playSound);

        EnsureBgmList();
        PrewarmConfiguredSfx();
        if (bgMusicSource != null && backgroundMusics != null && backgroundMusics.Count > 0)
        {
            PlayBackGroundMusic();
        }
    }

    private bool _isLoopRandomBGM;

    private void Update()
    {
        if (backgroundMusics == null || backgroundMusics.Count <= 0)
        {
            return;
        }
        if (_isLoopRandomBGM && !bgMusicSource.isPlaying)
        {
            AudioClip clip = RandomBGM();
            bgMusicSource.clip = clip;
            bgMusicSource.Play();
        }
    }

    public void StopBackGroundMusic()
    {
        _isLoopRandomBGM = false;
        bgMusicSource.Stop();
    }

    public void PlayBackGroundMusic()
    {
        _isLoopRandomBGM = true;
    }

    private List<AudioClip> _backgroundMusicTemp = new List<AudioClip>();

    private AudioClip RandomBGM()
    {
        if (_backgroundMusicTemp.Count == 0)
        {
            _backgroundMusicTemp.AddRange(backgroundMusics);
        }
        var result = _backgroundMusicTemp[Random.Range(0, _backgroundMusicTemp.Count)];
        _backgroundMusicTemp.Remove(result);
        return result;
    }

    private void EnsureBgmList()
    {
        if (backgroundMusics == null) backgroundMusics = new List<AudioClip>();
        if (backgroundMusics.Count > 0) return;

        // Fallback: load default BGM from Resources/Sound/BGM
        var clip = Resources.Load<AudioClip>("Sound/BGM");
        if (clip != null) backgroundMusics.Add(clip);
    }

    private void OnSoundChange(float currentValue)
    {
        if (audioMixer == null) return;

        // Remap 0-1 to dB scale
        float maxRangeDesign = 1f;
        currentValue = Remap(currentValue, 0, 1, 0, maxRangeDesign);
        var soundValue = currentValue == 0 ? -80f : Mathf.Log10(currentValue) * 20;

        var parameterName = Enum.GetName(typeof(SoundMixerGroup), SoundMixerGroup.BGMusic);
        audioMixer.SetFloat(parameterName, soundValue);
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

        // [FIX] Ensure AudioSource exists for Luna
        if (fxMusicSource == null)
        {
            EnsureAudioSources();
        }

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

    public void StopOneShot()
    {
        if (fxMusicSource == null) return;
        fxMusicSource.Stop();
    }

    private void EnsureAudioSources()
    {
        if (bgMusicSource == null)
        {
            bgMusicSource = gameObject.AddComponent<AudioSource>();
            bgMusicSource.playOnAwake = false;
            bgMusicSource.loop = false;
        }

        if (fxMusicSource == null)
        {
            fxMusicSource = gameObject.AddComponent<AudioSource>();
            fxMusicSource.playOnAwake = false;
            fxMusicSource.loop = false;
        }
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
