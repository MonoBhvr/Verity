namespace Verity.Core.Audio;

public sealed class MiniaudioAudioRuntime : IAudioRuntime
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
