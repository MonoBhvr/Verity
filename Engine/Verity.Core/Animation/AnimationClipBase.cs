using Verity.Core;

namespace Verity.Core.Animation;

public enum NonInterpolatedSwitchMode
{
    ImmediateSwitch,
    Threshold
}

public sealed class AnimationEvent
{
    public string Name { get; set; } = string.Empty;
    public int Frame { get; set; }
}

public abstract class AnimationClipBase
{
    public string Name { get; set; } = "New Animation";
    public float FrameRate { get; set; } = 60.0f;
    public bool Loop { get; set; } = true;
    public float Duration { get; set; }
    public string AssetPath { get; set; } = string.Empty;
    public string AssetGuid { get; set; } = string.Empty;
    public List<AnimationTrack> Tracks { get; set; } = new();
    public List<AnimationEvent> Events { get; set; } = new();

    public void AddTrack(AnimationTrack track)
    {
        Tracks.Add(track);
        RecalculateDuration();
    }

    public void RecalculateDuration()
    {
        float maxTime = 0f;
        foreach (AnimationTrack track in Tracks)
        {
            track.SortKeyframes();
            if (track.Keyframes.Count == 0)
                continue;

            float time = track.Keyframes[^1].Time;
            if (time > maxTime)
                maxTime = time;
        }

        Duration = maxTime;
    }

    public virtual void PostLoad()
    {
        foreach (AnimationTrack track in Tracks)
        {
            track.SortKeyframes();
            Type? valueType = AnimationTypeUtility.ResolveType(track.TypeName);
            if (valueType == null)
                continue;

            for (int i = 0; i < track.Keyframes.Count; i++)
            {
                Keyframe keyframe = track.Keyframes[i];
                object? converted = AnimationTypeUtility.ConvertValue(keyframe.Value, valueType);
                if (converted != null)
                    keyframe.Value = converted;
            }
        }

        RecalculateDuration();
    }
}

public sealed class SpriteAnimationClip : AnimationClipBase
{
    public List<Sprite> Frames { get; set; } = new();

    public void SyncFramesFromTrack()
    {
    }
}
