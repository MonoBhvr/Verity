namespace Verity.Core.Audio;

public sealed class SdlAudioRuntime : IAudioRuntime
{
    public void Initialize()
    {
        AudioSystem.Initialize();
    }

    public void Shutdown()
    {
        AudioSystem.Shutdown();
    }
}
