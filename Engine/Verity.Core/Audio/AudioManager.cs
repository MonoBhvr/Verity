using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Verity.Core;
using Verity.Core.ECS;
using Verity.Core.Engine;

namespace Verity.Core.Audio;

[SingleInstancePerWorld]
public class AudioManager : Script
{
    private static AudioManager? _instance;
    private readonly Dictionary<string, AudioGroup> _groupMap = new();
    private readonly Random _random = new();
    private float _masterVolume = 1.0f;
    private AudioListener? _activeListener;
    private bool _loggedPitchSupport;

    public static AudioManager Instance
    {
        get
        {
            _instance ??= FindObjectOfType<AudioManager>();
            return _instance!;
        }
    }

    [SerializeField]
    public List<AudioGroup> Groups = new();

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

    public void SyncGroupMap()
    {
        EnsureDefaultGroups();
        _groupMap.Clear();

        var uniqueGroups = new List<AudioGroup>(Groups.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AudioGroup? group in Groups)
        {
            if (group == null)
                continue;

            group.Name = string.IsNullOrWhiteSpace(group.Name) ? $"Group_{uniqueGroups.Count}" : group.Name.Trim();
            if (!seen.Add(group.Name))
                continue;

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
        if (_groupMap.TryGetValue(name, out AudioGroup? group))
            return group;

        AudioGroup? existingInList = Groups.FirstOrDefault(g => g.Name == name);
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
        AudioGroup? group = Groups.FirstOrDefault(g => g.Name == name);
        if (group == null)
            return;

        StopGroup(name);
        Groups.Remove(group);
        _groupMap.Remove(name);
    }

    public void Play(AudioSource source)
    {
        if (source.Clip == null)
            return;

        if (!AudioSystem.IsClipLoaded(source.Clip))
            source.Clip.PostLoad();

        if (!AudioSystem.IsClipLoaded(source.Clip))
            return;

        AudioGroup group = GetGroup(source.GroupName);
        source.RuntimeVolumeScale = NextRange(source.MinVolume, source.MaxVolume);
        source.RuntimePitchScale = NextRange(source.MinPitch, source.MaxPitch);
        LogPitchSupportIfNeeded(source, group);
        AudioSystem.Backend.Play(source, group, _masterVolume, _activeListener);
    }

    public void Stop(AudioSource source)
    {
        AudioSystem.Backend.Stop(source);
    }

    public void StopGroup(string groupName)
    {
        if (Owner.World == null)
            return;

        var sources = Owner.World.GetAllComponents<AudioSource>().ToList();
        AudioSystem.Backend.StopGroup(groupName, sources);
    }

    public void PlayOneShot(AudioClip clip, string groupName = "SFX", float volumeScale = 1.0f, Vector2? position = null, float minDistance = 1.0f, float maxDistance = 10.0f)
    {
        if (!AudioSystem.IsClipLoaded(clip))
            clip.PostLoad();

        if (!AudioSystem.IsClipLoaded(clip))
            return;

        AudioSystem.Backend.PlayOneShot(clip, groupName, GetGroup(groupName), _masterVolume, volumeScale, 1.0f, position, minDistance, maxDistance, _activeListener);
    }

    public void PlayOneShot(AudioSource source, AudioClip clip, float volumeScale = 1.0f)
    {
        if (!AudioSystem.IsClipLoaded(clip))
            clip.PostLoad();

        if (!AudioSystem.IsClipLoaded(clip))
            return;

        AudioGroup group = GetGroup(source.GroupName);
        float runtimeVolumeScale = NextRange(source.MinVolume, source.MaxVolume);
        float runtimePitchScale = NextRange(source.MinPitch, source.MaxPitch);
        float finalVolumeScale = volumeScale * source.Volume * runtimeVolumeScale;
        float finalPitchScale = source.Pitch * runtimePitchScale;

        LogPitchSupportIfNeeded(source, group);
        AudioSystem.Backend.PlayOneShot(
            clip,
            source.GroupName,
            group,
            _masterVolume,
            finalVolumeScale,
            finalPitchScale,
            source.IsSpatial ? source.Transform.WorldPosition : null,
            source.MinDistance,
            source.MaxDistance,
            _activeListener);
    }

    public void Preview(AudioClip clip)
    {
        if (!AudioSystem.IsClipLoaded(clip))
            clip.PostLoad();

        if (!AudioSystem.IsClipLoaded(clip))
            return;

        string groupName = clip.Type == AudioType.Music ? "BGM" : "SFX";
        AudioGroup group = GetGroup(groupName);
        AudioSystem.Backend.PlayOneShot(clip, groupName, group, _masterVolume, 1.0f, 1.0f, null, 1.0f, 10.0f, null);
    }

    public override void Update()
    {
        if (Owner.World == null)
            return;

        _activeListener = Owner.World.GetAllComponents<AudioListener>().FirstOrDefault(l => l.Enabled && l.Owner.Active);
        IReadOnlyList<AudioSource> sources = Owner.World.GetAllComponents<AudioSource>().ToList();
        AudioSystem.Backend.Update(sources, Groups, _masterVolume, _activeListener);
    }

    public override void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    private float NextRange(float min, float max)
    {
        if (max < min)
            (min, max) = (max, min);

        return (float)(_random.NextDouble() * (max - min) + min);
    }

    private void LogPitchSupportIfNeeded(AudioSource source, AudioGroup group)
    {
        if (_loggedPitchSupport || AudioSystem.SupportsPitch)
            return;

        float requestedPitch = source.Pitch * (source.Clip?.DefaultPitch ?? 1.0f) * group.Pitch * source.RuntimePitchScale;
        if (Math.Abs(requestedPitch - 1.0f) <= 0.001f)
            return;

        Debug.LogWarning($"[AudioManager] The active audio backend '{AudioSystem.BackendName}' does not support pitch adjustment.");
        _loggedPitchSupport = true;
    }
}
