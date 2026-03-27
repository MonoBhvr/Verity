namespace Verity.Core.Animation;

public class AnimationClip
{
    public string Name { get; set; } = "New Animation";
    public float FrameRate { get; set; } = 60.0f;
    public bool Loop { get; set; } = true;
    public float Duration { get; set; } = 0.0f;
    
    public List<AnimationTrack> Tracks { get; set; } = new();

    public void AddTrack(AnimationTrack track)
    {
        Tracks.Add(track);
        RecalculateDuration();
    }

    public void RecalculateDuration()
    {
        float maxTime = 0;
        foreach (var track in Tracks)
        {
            track.SortKeyframes();
            if (track.Keyframes.Count > 0)
            {
                float t = track.Keyframes[^1].Time;
                if (t > maxTime) maxTime = t;
            }
        }
        Duration = maxTime;
    }

    public void PostLoad()
    {
        foreach (var track in Tracks)
        {
            track.SortKeyframes();
            Type? valueType = AnimationTypeUtility.ResolveType(track.TypeName);
            if (valueType == null) continue;

            for (int i = 0; i < track.Keyframes.Count; i++)
            {
                var kf = track.Keyframes[i];
                object? converted = AnimationTypeUtility.ConvertValue(kf.Value, valueType);
                if (converted != null)
                    kf.Value = converted;
            }
        }
        RecalculateDuration();
    }
}
