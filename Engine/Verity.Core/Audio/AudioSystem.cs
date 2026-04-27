namespace Verity.Core.Audio;

public static class AudioSystem
{
    private static readonly object Sync = new();
    private static IAudioBackend? _backend;

    public static bool IsInitialized { get; private set; }

    public static string BackendName => _backend?.Name ?? "Uninitialized";

    public static bool SupportsPitch => _backend?.SupportsPitch ?? false;

    internal static IAudioBackend Backend
    {
        get
        {
            lock (Sync)
            {
                _backend ??= new MiniaudioAudioBackend();
                return _backend;
            }
        }
    }

    public static void Initialize()
    {
        lock (Sync)
        {
            if (IsInitialized)
                return;

            Backend.Initialize();
            IsInitialized = true;
        }
    }

    public static void Shutdown()
    {
        lock (Sync)
        {
            if (!IsInitialized)
                return;

            Backend.Shutdown();
            IsInitialized = false;
        }
    }

    internal static void LoadClip(AudioClip clip, string path)
    {
        Initialize();
        Backend.LoadClip(clip, path);
    }

    internal static void UnloadClip(AudioClip clip)
    {
        if (!IsInitialized)
            return;

        Backend.UnloadClip(clip);
    }

    internal static bool IsClipLoaded(AudioClip clip)
    {
        return IsInitialized && Backend.IsClipLoaded(clip);
    }

    internal static void PreviewClip(AudioClip clip)
    {
        Initialize();
        Backend.PreviewClip(clip);
    }
}
