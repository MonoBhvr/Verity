using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Verity.Core.ECS;
using Verity.Core.Engine;
using Verity.Core;
using SDL2;

namespace Verity.Core.Audio;

/// <summary>
/// 오디오 매니저는 씬 내의 오디오 설정을 총괄하는 스크립트입니다.
/// </summary>
[SingleInstancePerWorld]
public class AudioManager : Script
{
    private const int MusicChannelMarker = -2;

    private static AudioManager? _instance;
    public static AudioManager Instance 
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<AudioManager>();
            }
            return _instance!;
        }
    }

    // 인스펙터에 노출할 그룹 리스트
    [SerializeField]
    public List<AudioGroup> Groups = new();

    // 런타임 빠른 검색을 위한 딕셔너리
    private readonly Dictionary<string, AudioGroup> _groupMap = new();

    private float _masterVolume = 1.0f;
    private readonly Random _random = new();
    private AudioListener? _activeListener;
    private AudioSource? _activeMusicSource;
    private bool _loggedPitchUnsupported;

    public AudioManager()
    {
        EnsureDefaultGroups();
    }

    public float MasterVolume
    {
        get => _masterVolume;
        set => _masterVolume = Math.Clamp(value, 0f, 1f);
    }

    public override void Awake()
    {
        if (_instance != null && _instance != this && _instance.Owner.World == Owner.World)
        {
            Debug.LogWarning("[AudioManager] Only one AudioManager is allowed per world. Destroying duplicate.");
            Entity.Destroy(this);
            return;
        }

        _instance = this;
        AudioSystem.Initialize();
        SyncGroupMap();
    }

    /// <summary>
    /// 리스트의 그룹 정보들을 런타임 맵으로 동기화합니다.
    /// </summary>
    public void SyncGroupMap()
    {
        EnsureDefaultGroups();
        _groupMap.Clear();
        
        // 기본 그룹이 없으면 생성
        var uniqueGroups = new List<AudioGroup>(Groups.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in Groups)
        {
            if (group == null) continue;

            group.Name = string.IsNullOrWhiteSpace(group.Name) ? $"Group_{uniqueGroups.Count}" : group.Name.Trim();
            if (!seen.Add(group.Name)) continue;

            uniqueGroups.Add(group);
            _groupMap[group.Name] = group;
        }

        Groups = uniqueGroups;
    }

    public void EnsureDefaultGroups()
    {
        Groups ??= new List<AudioGroup>();
        EnsureGroupExists("Master", 64);
        EnsureGroupExists("BGM", 2);
        EnsureGroupExists("SFX", 32);
        EnsureGroupExists("UI", 8);
    }

    private void EnsureGroupExists(string name, int maxVoices)
    {
        if (Groups.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
            return;

        Groups.Add(new AudioGroup(name, maxVoices));
    }

    public AudioGroup GetGroup(string name)
    {
        // 맵에 없으면 리스트에서 찾거나 새로 생성
        if (_groupMap.TryGetValue(name, out var group)) return group;
        
        var existingInList = Groups.FirstOrDefault(g => g.Name == name);
        if (existingInList != null)
        {
            _groupMap[name] = existingInList;
            return existingInList;
        }

        var newGroup = new AudioGroup(name, 32);
        Groups.Add(newGroup);
        _groupMap[name] = newGroup;
        return newGroup;
    }

    public void RemoveGroup(string name)
    {
        var group = Groups.FirstOrDefault(g => g.Name == name);
        if (group != null)
        {
            StopGroup(name);
            Groups.Remove(group);
            _groupMap.Remove(name);
        }
    }

    public void Play(AudioSource source)
    {
        if (source.Clip == null || source.Clip.Handle == IntPtr.Zero) return;

        var group = GetGroup(source.GroupName);

        source.RuntimeVolumeScale = NextRange(source.MinVolume, source.MaxVolume);
        source.RuntimePitchScale = NextRange(source.MinPitch, source.MaxPitch);
        LogUnsupportedPitchIfNeeded(source, group);

        if (source.Clip.Type == AudioType.Music)
        {
            if (_activeMusicSource != null && !ReferenceEquals(_activeMusicSource, source))
                _activeMusicSource.CurrentChannel = -1;

            _activeMusicSource = source;
            source.CurrentChannel = MusicChannelMarker;
            ApplyMusicVolume(source, group);
            SDL_mixer.Mix_PlayMusic(source.Clip.Handle, source.Loop ? -1 : 0);
        }
        else
        {
            if (group.ActiveChannels.Count >= group.MaxVoices)
            {
                int oldest = group.ActiveChannels.Dequeue();
                SDL_mixer.Mix_HaltChannel(oldest);
            }

            int channel = SDL_mixer.Mix_PlayChannel(-1, source.Clip.Handle, source.Loop ? -1 : 0);
            if (channel != -1)
            {
                source.CurrentChannel = channel;
                group.ActiveChannels.Enqueue(channel);
                ApplyChannelVolume(source, group, channel);

                if (source.IsSpatial)
                {
                    ApplySpatialEffect(channel, source.Transform.WorldPosition, source.MinDistance, source.MaxDistance);
                }
            }
        }
    }

    public void Stop(AudioSource source)
    {
        if (source.Clip == null) return;
        if (source.Clip.Type == AudioType.Music)
        {
            SDL_mixer.Mix_HaltMusic();
            if (ReferenceEquals(_activeMusicSource, source))
                _activeMusicSource = null;
        }
        else if (source.CurrentChannel != -1)
        {
            SDL_mixer.Mix_HaltChannel(source.CurrentChannel);
            source.CurrentChannel = -1;
        }
    }

    public void StopGroup(string groupName)
    {
        var group = GetGroup(groupName);
        if (group != null)
        {
            while (group.ActiveChannels.Count > 0)
            {
                int ch = group.ActiveChannels.Dequeue();
                SDL_mixer.Mix_HaltChannel(ch);
            }
            if (groupName == "BGM")
            {
                SDL_mixer.Mix_HaltMusic();
                if (_activeMusicSource != null)
                {
                    _activeMusicSource.CurrentChannel = -1;
                    _activeMusicSource = null;
                }
            }
        }
    }

    public void PlayOneShot(AudioClip clip, string groupName = "SFX", float volumeScale = 1.0f, Vector2? position = null, float minDistance = 1.0f, float maxDistance = 10.0f)
    {
        if (clip == null || clip.Handle == IntPtr.Zero) return;

        var group = GetGroup(groupName);
        if (group.ActiveChannels.Count >= group.MaxVoices)
        {
            int oldest = group.ActiveChannels.Dequeue();
            SDL_mixer.Mix_HaltChannel(oldest);
        }

        int channel = SDL_mixer.Mix_PlayChannel(-1, clip.Handle, 0);
        if (channel != -1)
        {
            group.ActiveChannels.Enqueue(channel);
            float finalVol = volumeScale * clip.DefaultVolume * group.GetFinalVolume(_masterVolume);
            SDL_mixer.Mix_Volume(channel, (int)(finalVol * 128));

            if (position.HasValue)
            {
                ApplySpatialEffect(channel, position.Value, minDistance, maxDistance);
            }
        }
    }

    public override void Update()
    {
        if (Owner.World == null)
            return;

        _activeListener = Owner.World.GetAllComponents<AudioListener>().FirstOrDefault(l => l.Enabled && l.Owner.Active);

        var sources = Owner.World.GetAllComponents<AudioSource>();
        foreach (var source in sources)
        {
            if (source.Clip?.Type == AudioType.Music)
            {
                if (ReferenceEquals(_activeMusicSource, source) && SDL_mixer.Mix_PlayingMusic() == 1)
                {
                    ApplyMusicVolume(source, GetGroup(source.GroupName));
                }
                else
                {
                    source.CurrentChannel = -1;
                    if (ReferenceEquals(_activeMusicSource, source))
                        _activeMusicSource = null;
                }

                continue;
            }

            if (source.CurrentChannel != -1 && SDL_mixer.Mix_Playing(source.CurrentChannel) == 1)
            {
                if (source.IsSpatial)
                    ApplySpatialEffect(source.CurrentChannel, source.Transform.WorldPosition, source.MinDistance, source.MaxDistance);

                ApplyChannelVolume(source, GetGroup(source.GroupName), source.CurrentChannel);
            }
            else
            {
                source.CurrentChannel = -1;
            }
        }

        foreach (var group in Groups)
        {
            int count = group.ActiveChannels.Count;
            for (int i = 0; i < count; i++)
            {
                int ch = group.ActiveChannels.Dequeue();
                if (SDL_mixer.Mix_Playing(ch) == 1) group.ActiveChannels.Enqueue(ch);
            }
        }
    }

    public override void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    private void ApplySpatialEffect(int channel, Vector2 sourcePos, float min, float max)
    {
        if (_activeListener == null)
        {
            SDL_mixer.Mix_SetDistance(channel, 0);
            SDL_mixer.Mix_SetPanning(channel, 255, 255);
            return;
        }

        Vector2 listenerPos = _activeListener.Transform.WorldPosition;
        float distance = Vector2.Distance(sourcePos, listenerPos);

        float distFactor = Math.Clamp((distance - min) / (max - min), 0f, 1f);
        byte sdlDistance = (byte)(distFactor * 255);
        SDL_mixer.Mix_SetDistance(channel, sdlDistance);

        float diffX = sourcePos.X - listenerPos.X;
        float panFactor = Math.Clamp(diffX / max, -1f, 1f);

        byte left = (byte)((1.0f - Math.Clamp(panFactor, 0f, 1f)) * 255);
        byte right = (byte)((1.0f + Math.Clamp(panFactor, -1f, 0f)) * 255);
        
        SDL_mixer.Mix_SetPanning(channel, left, right);
    }

    private float NextRange(float min, float max)
    {
        if (max < min)
            (min, max) = (max, min);

        return (float)(_random.NextDouble() * (max - min) + min);
    }

    private void ApplyChannelVolume(AudioSource source, AudioGroup group, int channel)
    {
        float finalVolume = GetSourceFinalVolume(source, group);
        SDL_mixer.Mix_Volume(channel, ToSdlVolume(finalVolume));
    }

    private void ApplyMusicVolume(AudioSource source, AudioGroup group)
    {
        float finalVolume = GetSourceFinalVolume(source, group);
        SDL_mixer.Mix_VolumeMusic(ToSdlVolume(finalVolume));
    }

    private float GetSourceFinalVolume(AudioSource source, AudioGroup group)
    {
        float clipDefaultVolume = source.Clip?.DefaultVolume ?? 1.0f;
        return source.Volume * clipDefaultVolume * source.RuntimeVolumeScale * group.GetFinalVolume(_masterVolume);
    }

    private static int ToSdlVolume(float volume)
    {
        return (int)(Math.Clamp(volume, 0f, 1f) * 128);
    }

    private void LogUnsupportedPitchIfNeeded(AudioSource source, AudioGroup group)
    {
        if (_loggedPitchUnsupported)
            return;

        float requestedPitch = source.Pitch * (source.Clip?.DefaultPitch ?? 1.0f) * group.Pitch * source.RuntimePitchScale;
        if (Math.Abs(requestedPitch - 1.0f) <= 0.001f)
            return;

        Debug.LogWarning("[AudioManager] Pitch adjustment is not supported by the current SDL2_mixer backend. Volume changes will apply, but pitch values are ignored.");
        _loggedPitchUnsupported = true;
    }
}
