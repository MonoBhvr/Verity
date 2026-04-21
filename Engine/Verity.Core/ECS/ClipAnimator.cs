using Verity.Core.Animation;
using Verity.Core.Serialization;

namespace Verity.Core.ECS;

[Obsolete("Use Animator for full animation support. ClipAnimator is for simple clip playback only.")]
public class ClipAnimator : Component
{
    [SerializeField, AssetReference(".animclip;.spriteanimclip")]
    public string ClipPath { get; set; } = string.Empty;

    [SerializeField, HideInInspector]
    public string ClipGuid { get; set; } = string.Empty;

    [SerializeField]
    public List<ClipAnimatorState> States { get; set; } = new();

    [SerializeField]
    public string DefaultStateName { get; set; } = string.Empty;

    [SerializeField]
    public bool PlayOnEnable { get; set; } = true;

    [SerializeField]
    public float Speed { get; set; } = 1f;

    [SerializeField]
    public float DefaultFadeDuration { get; set; }

    [SerializeField]
    public NonInterpolatedSwitchMode NonInterpolatedSwitchMode { get; set; } = NonInterpolatedSwitchMode.ImmediateSwitch;

    [SerializeField]
    public float NonInterpolatedSwitchThreshold { get; set; } = 0.5f;

    private readonly Dictionary<string, (object? Target, System.Reflection.MemberInfo? Member)> _bindingCache = new();
    private readonly AnimationPlaybackState _playbackState = new();
    private bool _isPaused;

    [HideInInspector]
    public AnimationClipBase? Clip
    {
        get => GetDefaultState()?.Clip;
        set
        {
            ClipAnimatorState state = EnsureDefaultState();
            state.Clip = value;
            if (value != null)
                state.SetClipReference(value.AssetPath, value.AssetGuid);
            SyncLegacyClipReferenceFromDefaultState();
            _playbackState.Reset();
            _isPaused = false;
            _bindingCache.Clear();
            if (Enabled && PlayOnEnable)
                Play(GetDefaultPlayableStateName(), 0f, NonInterpolatedSwitchMode, NonInterpolatedSwitchThreshold);
        }
    }

    public bool IsPlaying => _playbackState.IsPlaying;
    public bool IsPaused => _isPaused;
    public bool IsFading => _playbackState.IsFading;
    public float CurrentTime => _playbackState.CurrentTime;
    public string CurrentStateName => _playbackState.CurrentStateName;

    public event Action<string>? StateFinished;
    public event Action<string, string>? TransitionCompleted;
    public event Action<string>? AnimationEventFired;

    protected override void OnEnable()
    {
        base.OnEnable();
        _isPaused = false;
        PostLoadStates();
        AnimationSystem.Register(this);
        if (PlayOnEnable)
            Play(GetDefaultPlayableStateName());
        else if (TryGetCurrentOrDefaultClip(out string _, out AnimationClipBase? clip) && clip != null)
            SampleClip(clip, 0f);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        AnimationSystem.Unregister(this);
    }

    public bool HasState(string stateName) => FindState(stateName) != null;

    public void Play(string stateName) => Play(stateName, DefaultFadeDuration, NonInterpolatedSwitchMode, NonInterpolatedSwitchThreshold);

    public void Play(string stateName, float fadeDuration) => Play(stateName, fadeDuration, NonInterpolatedSwitchMode, NonInterpolatedSwitchThreshold);

    public void Play(string stateName, float fadeDuration, NonInterpolatedSwitchMode switchMode, float threshold = 0.5f)
    {
        ClipAnimatorState? state = FindState(stateName);
        if (state?.Clip == null)
            return;

        string previousState = CurrentStateName;
        ClipPlayback.Play(_playbackState, state.Name, state.Clip, restart: true, fadeDuration);
        _isPaused = false;
        ClipPlayback.Sample(Owner, _bindingCache, _playbackState, switchMode, threshold);
        if (!string.IsNullOrWhiteSpace(previousState) && !string.Equals(previousState, state.Name, StringComparison.Ordinal) && !_playbackState.IsFading)
            TransitionCompleted?.Invoke(previousState, state.Name);
    }

    public void PlayIfChanged(string stateName)
    {
        if (!string.Equals(CurrentStateName, stateName, StringComparison.Ordinal))
            Play(stateName);
    }

    public void PlayIfChanged(string stateName, float fadeDuration)
    {
        if (!string.Equals(CurrentStateName, stateName, StringComparison.Ordinal))
            Play(stateName, fadeDuration);
    }

    public void PlayIfChanged(string stateName, float fadeDuration, NonInterpolatedSwitchMode switchMode, float threshold = 0.5f)
    {
        if (!string.Equals(CurrentStateName, stateName, StringComparison.Ordinal))
            Play(stateName, fadeDuration, switchMode, threshold);
    }

    public void Stop()
    {
        _playbackState.IsPlaying = false;
        _playbackState.CurrentTime = 0f;
        _isPaused = false;
    }

    public void Pause()
    {
        if (!_playbackState.IsPlaying || _playbackState.CurrentClip == null)
            return;

        _playbackState.IsPlaying = false;
        _isPaused = true;
    }

    public void Resume()
    {
        if (!_isPaused)
            return;

        if (_playbackState.CurrentClip == null)
        {
            if (PlayOnEnable)
                Play(GetDefaultPlayableStateName());
            return;
        }

        AnimationSystem.Register(this);
        _playbackState.IsPlaying = true;
        _isPaused = false;
    }

    public void SetTime(float time, bool sample = true)
    {
        if (!TryGetCurrentOrDefaultClip(out string stateName, out AnimationClipBase? clip) || clip == null)
            return;

        _playbackState.CurrentStateName = stateName;
        _playbackState.CurrentClip = clip;
        _playbackState.CurrentTime = time;
        if (sample)
            ClipPlayback.Sample(Owner, _bindingCache, _playbackState, NonInterpolatedSwitchMode, NonInterpolatedSwitchThreshold);
    }

    public float GetStateProgress()
    {
        if (_playbackState.CurrentClip == null || _playbackState.CurrentClip.Duration <= 0f)
            return 1f;

        return ClipPlayback.GetSampleTime(_playbackState.CurrentClip, _playbackState.CurrentTime) / _playbackState.CurrentClip.Duration;
    }

    public void UpdateAnimation(float deltaTime)
    {
        string previousState = CurrentStateName;
        bool wasFading = _playbackState.IsFading;
        bool finished = ClipPlayback.Update(_playbackState, Owner, _bindingCache, deltaTime, Speed, NonInterpolatedSwitchMode, NonInterpolatedSwitchThreshold, eventName => AnimationEventFired?.Invoke(eventName));
        if (wasFading && !_playbackState.IsFading && !string.IsNullOrWhiteSpace(previousState) && !string.IsNullOrWhiteSpace(CurrentStateName))
            TransitionCompleted?.Invoke(previousState, CurrentStateName);
        if (finished && !string.IsNullOrWhiteSpace(CurrentStateName))
            StateFinished?.Invoke(CurrentStateName);
    }

    public void SampleClip(AnimationClipBase? clip, float time)
    {
        if (clip == null)
            return;

        var previewState = new AnimationPlaybackState
        {
            CurrentClip = clip,
            CurrentTime = time,
            IsPlaying = false
        };
        ClipPlayback.Sample(Owner, _bindingCache, previewState, NonInterpolatedSwitchMode, NonInterpolatedSwitchThreshold);
    }

    public void ReloadClipAsset()
    {
        PostLoadStates();
    }

    private void PostLoadStates()
    {
        string assetRoot = SceneSerializer.AssetRootPath ?? AppContext.BaseDirectory;
        if (States.Count == 0 && !string.IsNullOrWhiteSpace(ClipPath))
        {
            string stateName = string.IsNullOrWhiteSpace(DefaultStateName) ? "Default" : DefaultStateName;
            States.Add(new ClipAnimatorState
            {
                Name = stateName,
                ClipPath = ClipPath,
                ClipGuid = ClipGuid
            });
        }

        foreach (ClipAnimatorState state in States)
            state.PostLoad(assetRoot);

        if (string.IsNullOrWhiteSpace(DefaultStateName) && States.Count > 0)
            DefaultStateName = States[0].Name;

        SyncLegacyClipReferenceFromDefaultState();
    }

    private ClipAnimatorState? FindState(string stateName)
    {
        return States.FirstOrDefault(state => string.Equals(state.Name, stateName, StringComparison.Ordinal));
    }

    private ClipAnimatorState? GetDefaultState()
    {
        return FindState(DefaultStateName) ?? States.FirstOrDefault();
    }

    private ClipAnimatorState EnsureDefaultState()
    {
        ClipAnimatorState? state = GetDefaultState();
        if (state != null)
            return state;

        state = new ClipAnimatorState { Name = string.IsNullOrWhiteSpace(DefaultStateName) ? "Default" : DefaultStateName };
        States.Add(state);
        if (string.IsNullOrWhiteSpace(DefaultStateName))
            DefaultStateName = state.Name;
        return state;
    }

    private string GetDefaultPlayableStateName()
    {
        return GetDefaultState()?.Name ?? string.Empty;
    }

    private void SyncLegacyClipReferenceFromDefaultState()
    {
        ClipAnimatorState? state = GetDefaultState();
        ClipPath = state?.ClipPath ?? string.Empty;
        ClipGuid = state?.ClipGuid ?? string.Empty;
    }

    private bool TryGetCurrentOrDefaultClip(out string stateName, out AnimationClipBase? clip)
    {
        if (!string.IsNullOrWhiteSpace(CurrentStateName))
        {
            ClipAnimatorState? current = FindState(CurrentStateName);
            if (current?.Clip != null)
            {
                stateName = current.Name;
                clip = current.Clip;
                return true;
            }
        }

        ClipAnimatorState? fallback = GetDefaultState();
        stateName = fallback?.Name ?? string.Empty;
        clip = fallback?.Clip;
        return clip != null;
    }
}
