using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Miniaudio;
using Verity.Core;

namespace Verity.Core.Audio;

internal unsafe sealed class MiniaudioAudioBackend : IAudioBackend
{
    private sealed class SoundInstance
    {
        public required ma_sound* Sound;
        public required string ClipPath;
        public bool OneShot;
        public string GroupName = "SFX";
    }

    private readonly Dictionary<AudioSource, SoundInstance> _activeSounds = new();
    private readonly List<SoundInstance> _transientSounds = new();
    private ma_engine* _engine;
    private bool _initialized;

    public string Name => "miniaudio";

    public bool SupportsPitch => true;

    public void Initialize()
    {
        if (_initialized)
            return;

        _engine = (ma_engine*)NativeMemory.Alloc((nuint)sizeof(ma_engine));
        ma_result result = ma.engine_init(null, _engine);
        if (result != ma_result.MA_SUCCESS)
        {
            NativeMemory.Free(_engine);
            _engine = null;
            throw new InvalidOperationException($"[AudioSystem] Failed to initialize miniaudio engine: {result}");
        }

        _initialized = true;
        Debug.Log("[AudioSystem] miniaudio backend initialized.");
    }

    public void Shutdown()
    {
        if (!_initialized)
            return;

        foreach ((AudioSource source, SoundInstance instance) in _activeSounds.ToArray())
        {
            DestroySound(instance);
            source.CurrentChannel = -1;
        }

        _activeSounds.Clear();

        foreach (SoundInstance instance in _transientSounds)
            DestroySound(instance);

        _transientSounds.Clear();

        ma.engine_uninit(_engine);
        NativeMemory.Free(_engine);
        _engine = null;
        _initialized = false;
    }

    public void LoadClip(AudioClip clip, string path)
    {
        clip.BackendState = string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public void UnloadClip(AudioClip clip)
    {
        clip.BackendState = null;
    }

    public bool IsClipLoaded(AudioClip clip)
    {
        return clip.BackendState is string path && !string.IsNullOrWhiteSpace(path);
    }

    public void PreviewClip(AudioClip clip)
    {
        string? clipPath = GetLoadedClipPath(clip);
        if (_engine == null || string.IsNullOrWhiteSpace(clipPath))
            return;

        CleanupTransientSounds();

        if (!TryCreateSound(clipPath, clip.IsLooping, out SoundInstance? instance))
            return;

        SoundInstance readyInstance = instance!;
        readyInstance.GroupName = clip.Type == AudioType.Music ? "BGM" : "SFX";
        ApplySoundSettings(readyInstance.Sound, clip.DefaultVolume, clip.DefaultPitch, false, null, 1f, 10f, null);
        ma.sound_start(readyInstance.Sound);
        _transientSounds.Add(readyInstance);
    }

    public void Play(AudioSource source, AudioGroup group, float masterVolume, AudioListener? listener)
    {
        string? clipPath = source.Clip != null ? GetLoadedClipPath(source.Clip) : null;
        if (_engine == null || source.Clip == null || string.IsNullOrWhiteSpace(clipPath))
            return;

        CleanupTransientSounds();

        if (_activeSounds.TryGetValue(source, out SoundInstance? existing))
        {
            if (!string.Equals(existing.ClipPath, clipPath, StringComparison.OrdinalIgnoreCase))
            {
                DestroySound(existing);
                _activeSounds.Remove(source);
                source.CurrentChannel = -1;
                existing = null;
            }
        }

        if (existing == null)
        {
            if (!TryCreateSound(clipPath, source.Loop || source.Clip.IsLooping, out SoundInstance? created))
                return;

            SoundInstance readySound = created!;
            readySound.GroupName = source.GroupName;
            _activeSounds[source] = readySound;
            existing = readySound;
        }

        ApplySoundSettings(existing.Sound, GetFinalVolume(source, group, masterVolume), GetFinalPitch(source, group), source.IsSpatial, source.Transform.WorldPosition, source.MinDistance, source.MaxDistance, listener);
        ma.sound_set_looping(existing.Sound, source.Loop || source.Clip.IsLooping ? 1u : 0u);
        ma.sound_start(existing.Sound);
        source.CurrentChannel = 1;
    }

    public void Stop(AudioSource source)
    {
        if (!_activeSounds.TryGetValue(source, out SoundInstance? instance))
            return;

        ma.sound_stop(instance.Sound);
        DestroySound(instance);
        _activeSounds.Remove(source);
        source.CurrentChannel = -1;
    }

    public void StopGroup(string groupName, IReadOnlyList<AudioSource> sources)
    {
        foreach (AudioSource source in sources)
        {
            if (string.Equals(source.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
                Stop(source);
        }

        for (int i = _transientSounds.Count - 1; i >= 0; i--)
        {
            SoundInstance instance = _transientSounds[i];
            if (!string.Equals(instance.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
                continue;

            ma.sound_stop(instance.Sound);
            DestroySound(instance);
            _transientSounds.RemoveAt(i);
        }
    }

    public void PlayOneShot(AudioClip clip, string groupName, AudioGroup group, float masterVolume, float volumeScale, float pitchScale, Vector2? position, float minDistance, float maxDistance, AudioListener? listener)
    {
        string? clipPath = GetLoadedClipPath(clip);
        if (_engine == null || string.IsNullOrWhiteSpace(clipPath))
            return;

        CleanupTransientSounds();

        if (!TryCreateSound(clipPath, false, out SoundInstance? instance))
            return;

        SoundInstance readyInstance = instance!;
        readyInstance.OneShot = true;
        readyInstance.GroupName = groupName;
        ApplySoundSettings(
            readyInstance.Sound,
            Math.Clamp(volumeScale * clip.DefaultVolume * group.GetFinalVolume(masterVolume), 0f, 1f),
            Math.Max(0.01f, pitchScale * clip.DefaultPitch * group.Pitch),
            position.HasValue,
            position,
            minDistance,
            maxDistance,
            listener);
        ma.sound_start(readyInstance.Sound);
        _transientSounds.Add(readyInstance);
    }

    public void Update(IReadOnlyList<AudioSource> sources, IReadOnlyList<AudioGroup> groups, float masterVolume, AudioListener? listener)
    {
        if (_engine == null)
            return;

        if (listener != null)
        {
            Vector2 position = listener.Transform.WorldPosition;
            ma.engine_listener_set_position(_engine, 0, position.X, position.Y, 0f);
        }

        HashSet<AudioSource> liveSources = sources.ToHashSet();
        foreach ((AudioSource source, SoundInstance instance) in _activeSounds.ToArray())
        {
            if (!liveSources.Contains(source) || source.Clip == null)
            {
                DestroySound(instance);
                _activeSounds.Remove(source);
                source.CurrentChannel = -1;
                continue;
            }

            if (ma.sound_is_playing(instance.Sound) == 0 && !(source.Loop || source.Clip.IsLooping))
            {
                DestroySound(instance);
                _activeSounds.Remove(source);
                source.CurrentChannel = -1;
                continue;
            }

            AudioGroup group = groups.FirstOrDefault(g => string.Equals(g.Name, source.GroupName, StringComparison.OrdinalIgnoreCase))
                ?? new AudioGroup(source.GroupName);

            ApplySoundSettings(instance.Sound, GetFinalVolume(source, group, masterVolume), GetFinalPitch(source, group), source.IsSpatial, source.Transform.WorldPosition, source.MinDistance, source.MaxDistance, listener);
            instance.GroupName = source.GroupName;
            source.CurrentChannel = ma.sound_is_playing(instance.Sound) == 1 ? 1 : -1;
        }

        CleanupTransientSounds();
    }

    private bool TryCreateSound(string clipPath, bool looping, out SoundInstance? instance)
    {
        instance = null;
        if (_engine == null || string.IsNullOrWhiteSpace(clipPath))
            return false;

        ma_sound* sound = (ma_sound*)NativeMemory.Alloc((nuint)sizeof(ma_sound));
        fixed (char* pathPtr = clipPath)
        {
            uint flags = looping ? (uint)ma_sound_flags.MA_SOUND_FLAG_LOOPING : 0u;
            ma_result result = ma.sound_init_from_file_w(_engine, (ushort*)pathPtr, flags, null, null, sound);
            if (result != ma_result.MA_SUCCESS)
            {
                NativeMemory.Free(sound);
                Debug.LogError($"[AudioSystem] Failed to create miniaudio sound for '{clipPath}': {result}");
                return false;
            }
        }

        instance = new SoundInstance
        {
            Sound = sound,
            ClipPath = clipPath
        };
        return true;
    }

    private static string? GetLoadedClipPath(AudioClip clip)
    {
        string path = clip.GetRuntimePath();
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private void ApplySoundSettings(ma_sound* sound, float volume, float pitch, bool spatial, Vector2? position, float minDistance, float maxDistance, AudioListener? listener)
    {
        ma.sound_set_volume(sound, Math.Clamp(volume, 0f, 1f));
        ma.sound_set_pitch(sound, Math.Max(0.01f, pitch));

        if (!spatial || position == null || listener == null)
        {
            ma.sound_set_spatialization_enabled(sound, 0);
            ma.sound_set_pan_mode(sound, ma_pan_mode.ma_pan_mode_balance);
            ma.sound_set_pan(sound, 0f);
            return;
        }

        ma.sound_set_spatialization_enabled(sound, 1);
        ma.sound_set_positioning(sound, ma_positioning.ma_positioning_absolute);
        ma.sound_set_attenuation_model(sound, ma_attenuation_model.ma_attenuation_model_inverse);
        ma.sound_set_min_distance(sound, Math.Max(0.01f, minDistance));
        ma.sound_set_max_distance(sound, Math.Max(minDistance + 0.01f, maxDistance));
        ma.sound_set_position(sound, position.Value.X, position.Value.Y, 0f);
    }

    private void CleanupTransientSounds()
    {
        for (int i = _transientSounds.Count - 1; i >= 0; i--)
        {
            SoundInstance instance = _transientSounds[i];
            if (ma.sound_is_playing(instance.Sound) == 1 && ma.sound_at_end(instance.Sound) == 0)
                continue;

            DestroySound(instance);
            _transientSounds.RemoveAt(i);
        }
    }

    private static void DestroySound(SoundInstance instance)
    {
        ma.sound_uninit(instance.Sound);
        NativeMemory.Free(instance.Sound);
    }

    private static float GetFinalVolume(AudioSource source, AudioGroup group, float masterVolume)
    {
        float clipDefaultVolume = source.Clip?.DefaultVolume ?? 1f;
        return source.Volume * clipDefaultVolume * source.RuntimeVolumeScale * group.GetFinalVolume(masterVolume);
    }

    private static float GetFinalPitch(AudioSource source, AudioGroup group)
    {
        float clipDefaultPitch = source.Clip?.DefaultPitch ?? 1f;
        return source.Pitch * clipDefaultPitch * source.RuntimePitchScale * group.Pitch;
    }
}
