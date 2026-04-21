namespace Verity.Core.Animation;

public sealed class ClipAnimatorState
{
    public string Name { get; set; } = "New State";
    public string ClipPath { get; set; } = string.Empty;
    public string ClipGuid { get; set; } = string.Empty;
    public AnimationClipBase? Clip { get; set; }

    public void PostLoad(string? assetRoot)
    {
        if (!string.IsNullOrWhiteSpace(ClipPath))
        {
            string clipFullPath = AssetPathUtility.ResolvePath(assetRoot, ClipPath, ClipGuid);
            Clip = AnimationClipAsset.LoadFromFile(clipFullPath) ?? Clip;
        }

        if (Clip != null)
        {
            Clip.AssetPath = string.IsNullOrWhiteSpace(Clip.AssetPath) ? AssetPathUtility.Normalize(ClipPath) : Clip.AssetPath;
            Clip.AssetGuid = string.IsNullOrWhiteSpace(Clip.AssetGuid) ? ClipGuid : Clip.AssetGuid;
            Clip.PostLoad();
        }
    }

    public void SetClipReference(string path, string guid)
    {
        ClipPath = AssetPathUtility.Normalize(path);
        ClipGuid = guid ?? string.Empty;
        if (Clip != null)
        {
            Clip.AssetPath = ClipPath;
            Clip.AssetGuid = ClipGuid;
        }
    }
}

public sealed class AnimationPlaybackState
{
    public string CurrentStateName { get; internal set; } = string.Empty;
    public AnimationClipBase? CurrentClip { get; internal set; }
    public AnimationClipBase? PreviousClip { get; internal set; }
    public float CurrentTime { get; internal set; }
    public float PreviousTime { get; internal set; }
    public bool IsPlaying { get; internal set; }
    public float FadeDuration { get; internal set; }
    public float FadeTime { get; internal set; }
    public bool IsFading => PreviousClip != null && FadeDuration > 0f && FadeTime < FadeDuration;

    internal void Reset()
    {
        CurrentStateName = string.Empty;
        CurrentClip = null;
        PreviousClip = null;
        CurrentTime = 0f;
        PreviousTime = 0f;
        FadeDuration = 0f;
        FadeTime = 0f;
        IsPlaying = false;
    }
}
