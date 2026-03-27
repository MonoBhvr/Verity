using System.Collections.Generic;

namespace Verity.Core.Audio;

/// <summary>
/// 오디오 그룹은 여러 오디오 소스를 논리적으로 묶어 한꺼번에 제어하기 위해 사용됩니다. (Master, SFX, BGM 등)
/// </summary>
public class AudioGroup
{
    public string Name { get; set; }
    public float Volume { get; set; } = 1.0f;
    public float Pitch { get; set; } = 1.0f;
    public bool IsMuted { get; set; } = false;
    
    /// <summary>
    /// 해당 그룹에서 동시에 재생 가능한 최대 보이스(채널) 수입니다.
    /// </summary>
    public int MaxVoices { get; set; } = 32;

    /// <summary>
    /// 현재 재생 중인 채널들을 추적하기 위한 큐입니다. (직렬화 제외)
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Queue<int> ActiveChannels { get; } = new Queue<int>();

    public AudioGroup(string name, int maxVoices = 32)
    {
        Name = name;
        MaxVoices = maxVoices;
    }

    /// <summary>
    /// 마스터 볼륨과 그룹 자체 볼륨을 합산한 최종 출력 볼륨을 반환합니다.
    /// </summary>
    public float GetFinalVolume(float masterVolume)
    {
        return IsMuted ? 0f : Volume * masterVolume;
    }
}
