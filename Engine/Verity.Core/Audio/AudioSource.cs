using System.Numerics;
using Verity.Core.ECS;

namespace Verity.Core.Audio;

/// <summary>
/// 오디오 소스는 월드 내에서 소리를 출력하는 컴포넌트입니다.
/// </summary>
public class AudioSource : Component
{
    public AudioClip? Clip { get; set; }
    public string GroupName { get; set; } = "SFX";
    public bool Loop { get; set; } = false;
    public bool PlayOnStart { get; set; } = true;
    public bool IsSpatial { get; set; } = true;
    public bool Mute { get; set; } = false;

    private float _volume = 1.0f;
    public float Volume 
    { 
        get => Mute ? 0f : _volume; 
        set => _volume = value; 
    }
    
    public float Pitch { get; set; } = 1.0f;

    // 랜덤 변조 범위
    public float MinPitch { get; set; } = 1.0f;
    public float MaxPitch { get; set; } = 1.0f;
    public float MinVolume { get; set; } = 1.0f;
    public float MaxVolume { get; set; } = 1.0f;

    // 공간 음향 설정
    public float MinDistance { get; set; } = 1.0f;
    public float MaxDistance { get; set; } = 10.0f;

    public int CurrentChannel { get; internal set; } = -1;
    internal float RuntimeVolumeScale { get; set; } = 1.0f;
    internal float RuntimePitchScale { get; set; } = 1.0f;

    protected override void OnEnable()
    {
        Clip?.PostLoad();
        if (PlayOnStart)
        {
            Play();
        }
    }

    protected override void OnDisable()
    {
        Stop();
    }

    public void Play()
    {
        if (Clip == null) return;
        AudioManager.Instance?.Play(this);
    }

    public void Stop()
    {
        AudioManager.Instance?.Stop(this);
    }

    /// <summary>
    /// 지정된 클립을 현재 소스의 위치에서 일회성으로 중첩 재생합니다.
    /// </summary>
    public void PlayOneShot(AudioClip clip, float volumeScale = 1.0f)
    {
        if (clip == null) return;
        AudioManager.Instance?.PlayOneShot(clip, GroupName, volumeScale, IsSpatial ? Transform.WorldPosition : null, MinDistance, MaxDistance);
    }

    public override void OnDestroy()
    {
        Stop();
        base.OnDestroy();
        Clip?.Dispose();
    }
}
