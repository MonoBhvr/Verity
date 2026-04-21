namespace Verity.Core;

public readonly record struct SceneTransitionCompletedEvent(string SceneName);

public sealed class SceneTransition
{
    private readonly Action<string> _sceneLoader;
    private readonly float _fadeDuration;
    private string? _pendingSceneName;
    private TransitionPhase _phase;
    private float _phaseElapsed;
    private bool _loadedScene;
    private bool _previousInputEnabled;

    public SceneTransition(float fadeDuration = 0.25f, Action<string>? sceneLoader = null)
    {
        if (fadeDuration < 0f)
            throw new ArgumentOutOfRangeException(nameof(fadeDuration));

        _fadeDuration = fadeDuration;
        _sceneLoader = sceneLoader ?? Verity.Core.Engine.WorldLoader.LoadWorldByName;
    }

    public bool IsTransitioning => _phase != TransitionPhase.Idle;

    public float FadeAlpha { get; private set; }

    public void TransitionTo(string sceneName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneName);

        _pendingSceneName = sceneName;
        _phase = TransitionPhase.FadingOut;
        _phaseElapsed = 0f;
        _loadedScene = false;
        FadeAlpha = 0f;
        _previousInputEnabled = Verity.Input.Input.Enabled;
        Verity.Input.Input.Enabled = false;

        if (_fadeDuration == 0f)
            Update(0f);
    }

    public void Update(float deltaTime)
    {
        if (!IsTransitioning)
            return;

        if (deltaTime < 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));

        if (_fadeDuration == 0f)
        {
            CompleteFadeOut();
            FinishTransition();
            return;
        }

        var remainingDelta = deltaTime;

        while (remainingDelta >= 0f && IsTransitioning)
        {
            var remainingPhaseTime = _fadeDuration - _phaseElapsed;
            var step = Math.Min(remainingDelta, remainingPhaseTime);

            _phaseElapsed += step;
            remainingDelta -= step;
            UpdateFadeAlpha();

            if (_phaseElapsed < _fadeDuration)
                break;

            if (_phase == TransitionPhase.FadingOut)
            {
                CompleteFadeOut();
            }
            else
            {
                FinishTransition();
            }

            if (remainingDelta == 0f)
                break;
        }
    }

    private void CompleteFadeOut()
    {
        if (_loadedScene || _pendingSceneName is null)
            return;

        FadeAlpha = 1f;
        _sceneLoader(_pendingSceneName);
        _loadedScene = true;
        _phase = TransitionPhase.FadingIn;
        _phaseElapsed = 0f;
    }

    private void FinishTransition()
    {
        if (_pendingSceneName is null)
            return;

        FadeAlpha = 0f;
        var completedSceneName = _pendingSceneName;
        _pendingSceneName = null;
        _phase = TransitionPhase.Idle;
        _phaseElapsed = 0f;
        _loadedScene = false;
        Verity.Input.Input.Enabled = _previousInputEnabled;
        EventBus.Publish(new SceneTransitionCompletedEvent(completedSceneName));
    }

    private void UpdateFadeAlpha()
    {
        var progress = Math.Clamp(_phaseElapsed / _fadeDuration, 0f, 1f);
        FadeAlpha = _phase == TransitionPhase.FadingOut ? progress : 1f - progress;
    }

    private enum TransitionPhase
    {
        Idle,
        FadingOut,
        FadingIn
    }
}
