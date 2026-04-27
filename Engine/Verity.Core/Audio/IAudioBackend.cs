using System.Collections.Generic;
using System.Numerics;

namespace Verity.Core.Audio;

internal interface IAudioBackend
{
    string Name { get; }
    bool SupportsPitch { get; }

    void Initialize();
    void Shutdown();

    void LoadClip(AudioClip clip, string path);
    void UnloadClip(AudioClip clip);
    bool IsClipLoaded(AudioClip clip);
    void PreviewClip(AudioClip clip);

    void Play(AudioSource source, AudioGroup group, float masterVolume, AudioListener? listener);
    void Stop(AudioSource source);
    void StopGroup(string groupName, IReadOnlyList<AudioSource> sources);
    void PlayOneShot(
        AudioClip clip,
        string groupName,
        AudioGroup group,
        float masterVolume,
        float volumeScale,
        float pitchScale,
        Vector2? position,
        float minDistance,
        float maxDistance,
        AudioListener? listener);

    void Update(
        IReadOnlyList<AudioSource> sources,
        IReadOnlyList<AudioGroup> groups,
        float masterVolume,
        AudioListener? listener);
}
