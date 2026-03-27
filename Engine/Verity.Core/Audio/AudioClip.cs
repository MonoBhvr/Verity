using System;
using SDL2;

namespace Verity.Core.Audio;

public enum AudioType
{
    Effect,
    Music
}

/// <summary>
/// 오디오 클립은 로드된 사운드 데이터를 나타냅니다.
/// </summary>
public class AudioClip : IDisposable, IPathAsset
{
    public string Name { get; set; } = "New Audio Clip";
    public string Path { get; set; } = string.Empty;
    public string Guid { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore]
    public IntPtr Handle { get; private set; }
    public AudioType Type { get; set; } = AudioType.Effect;

    // [기획 보완] 클립별 기본 설정
    public float DefaultVolume { get; set; } = 1.0f;
    public float DefaultPitch { get; set; } = 1.0f;
    public bool IsLooping { get; set; } = false;

    public AudioClip() { }

    public AudioClip(string name, string path, AudioType type)
    {
        Name = name;
        Path = AssetPathUtility.Normalize(path);
        Guid = System.IO.Path.IsPathRooted(path) ? AssetPathUtility.EnsureMetaAndGetGuid(path) : string.Empty;
        Type = type;
        
        PostLoad();

        if (Handle == IntPtr.Zero)
        {
            Verity.Core.Debug.LogError($"오디오 파일을 로드할 수 없습니다: {path}. 에러: {SDL.SDL_GetError()}");
        }
    }

    public static AudioClip FromPath(string path, AudioType? type = null)
    {
        string normalizedPath = AssetPathUtility.Normalize(path);
        return new AudioClip(System.IO.Path.GetFileNameWithoutExtension(normalizedPath), path, type ?? GuessType(normalizedPath));
    }

    public static AudioType GuessType(string? path)
    {
        string ext = System.IO.Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
        return ext is ".mp3" or ".ogg" or ".flac" or ".mod" ? AudioType.Music : AudioType.Effect;
    }

    public void PostLoad(string? resolvedPath = null)
    {
        Dispose();

        string targetPath = resolvedPath ?? Path;
        if (string.IsNullOrWhiteSpace(targetPath))
            return;

        Handle = Type == AudioType.Music ? SDL_mixer.Mix_LoadMUS(targetPath) : SDL_mixer.Mix_LoadWAV(targetPath);

        if (Handle == IntPtr.Zero)
        {
            Verity.Core.Debug.LogError($"[AudioClip] Failed to load audio file: {targetPath}. Error: {SDL.SDL_GetError()}");
        }
    }

    public void Preview()
    {
        if (Handle == IntPtr.Zero)
            PostLoad();

        if (Handle == IntPtr.Zero) return;

        if (Type == AudioType.Effect)
        {
            SDL_mixer.Mix_PlayChannel(-1, Handle, 0);
        }
        else
        {
            SDL_mixer.Mix_PlayMusic(Handle, 0);
        }
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;

        if (Type == AudioType.Music)
        {
            SDL_mixer.Mix_FreeMusic(Handle);
        }
        else
        {
            SDL_mixer.Mix_FreeChunk(Handle);
        }
        Handle = IntPtr.Zero;
    }

    public override string ToString() => string.IsNullOrWhiteSpace(Path) ? Name : $"{Name} ({Path})";
}
