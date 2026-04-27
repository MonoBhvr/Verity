using System;
using Verity.Core.ECS;
using Verity.Core.Serialization;

namespace Verity.Core.Audio;

public enum AudioType
{
    Effect,
    Music
}

public class AudioClip : IDisposable, IPathAsset
{
    public string Name { get; set; } = "New Audio Clip";
    public string Path { get; set; } = string.Empty;
    public string Guid { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore]
    public IntPtr Handle => IntPtr.Zero;
    [System.Text.Json.Serialization.JsonIgnore]
    internal object? BackendState { get; set; }
    public AudioType Type { get; set; } = AudioType.Effect;
    public float DefaultVolume { get; set; } = 1.0f;
    public float DefaultPitch { get; set; } = 1.0f;
    public bool IsLooping { get; set; } = false;

    public AudioClip()
    {
    }

    public AudioClip(string name, string path, AudioType type)
    {
        Name = name;
        Path = AssetPathUtility.Normalize(path);
        Guid = System.IO.Path.IsPathRooted(path) ? AssetPathUtility.EnsureMetaAndGetGuid(path) : string.Empty;
        Type = type;
        PostLoad();
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
        string targetPath = ResolveRuntimePath(resolvedPath);
        if (string.IsNullOrWhiteSpace(targetPath))
            return;

        Dispose();
        AudioSystem.LoadClip(this, targetPath);

        if (!AudioSystem.IsClipLoaded(this))
            Verity.Core.Debug.LogError($"[AudioClip] Failed to prepare audio file: {targetPath}.");
    }

    public void Preview()
    {
        if (!AudioSystem.IsClipLoaded(this))
            PostLoad();

        if (!AudioSystem.IsClipLoaded(this))
            return;

        AudioManager? manager = Entity.FindObjectOfType<AudioManager>();
        if (manager != null)
        {
            manager.Preview(this);
            return;
        }

        AudioSystem.PreviewClip(this);
    }

    public void Dispose()
    {
        AudioSystem.UnloadClip(this);
    }

    internal string GetRuntimePath()
    {
        string? loadedPath = BackendState as string;
        string resolved = ResolveRuntimePath(loadedPath);
        if (!string.IsNullOrWhiteSpace(resolved))
            BackendState = resolved;

        return resolved;
    }

    private string ResolveRuntimePath(string? resolvedPath)
    {
        if (!string.IsNullOrWhiteSpace(resolvedPath))
        {
            if (System.IO.Path.IsPathRooted(resolvedPath))
                return System.IO.Path.GetFullPath(resolvedPath);

            string? assetRootFromResolved = SceneSerializer.AssetRootPath;
            if (!string.IsNullOrWhiteSpace(assetRootFromResolved))
                return AssetPathUtility.ResolvePath(assetRootFromResolved, resolvedPath, Guid);

            return resolvedPath;
        }

        if (string.IsNullOrWhiteSpace(Path))
            return string.Empty;

        if (System.IO.Path.IsPathRooted(Path))
            return System.IO.Path.GetFullPath(Path);

        string? assetRoot = SceneSerializer.AssetRootPath;
        return string.IsNullOrWhiteSpace(assetRoot)
            ? Path
            : AssetPathUtility.ResolvePath(assetRoot, Path, Guid);
    }

    public override string ToString() => string.IsNullOrWhiteSpace(Path) ? Name : $"{Name} ({Path})";
}
