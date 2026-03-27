using System.Reflection;
using Verity.Core.Animation;

namespace Verity.Core.ECS;

public class Animator : Component
{
    [SerializeField, AssetReference(".controller")]
    public string ControllerPath { get; set; } = "";

    [SerializeField, HideInInspector]
    public string ControllerGuid { get; set; } = "";

    private AnimatorController? _controller;
    private AnimatorState? _currentState;
    private AnimationClip? _currentClip;
    private float _time;
    private bool _isPlaying;
    private readonly Dictionary<string, (object? Target, MemberInfo? Member)> _bindingCache = new();

    [HideInInspector]
    public AnimatorController? Controller
    {
        get => _controller;
        set
        {
            _controller = value;
            _controller?.PostLoad();
            _currentState = null;
            _currentClip = null;
            _time = 0f;
            _isPlaying = false;
            _bindingCache.Clear();

            if (Enabled)
            {
                AnimationSystem.Register(this);
                TryPlayDefaultState();
            }
        }
    }

    public float Speed { get; set; } = 1.0f;
    public bool IsPlaying => _isPlaying;
    public float CurrentTime => _time;
    public string CurrentStateName => _currentState?.Name ?? string.Empty;

    protected override void OnEnable()
    {
        base.OnEnable();
        AnimationSystem.Register(this);
        TryPlayDefaultState();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        AnimationSystem.Unregister(this);
    }

    public void Play(string stateName, bool restart = true)
    {
        if (Controller == null)
            return;

        AnimationSystem.Register(this);
        var state = Controller.FindState(stateName);
        if (state == null)
            return;

        if (!restart && _currentState == state && _isPlaying)
            return;

        _currentState = state;
        _currentClip = state.Clip;
        _time = 0f;
        _isPlaying = _currentClip != null;

        if (_currentClip != null)
            SampleClip(_currentClip, 0f);
    }

    public void Stop()
    {
        _isPlaying = false;
        _time = 0f;
    }

    public void SetFloat(string name, float value)
    {
        if (Controller != null)
            Controller.FloatParameters[name] = value;
    }

    public void SetInt(string name, int value)
    {
        if (Controller != null)
            Controller.IntParameters[name] = value;
    }

    public void SetBool(string name, bool value)
    {
        if (Controller != null)
            Controller.BoolParameters[name] = value;
    }

    public void SetTrigger(string name)
    {
        if (Controller != null)
            Controller.TriggerParameters[name] = true;
    }

    public void ResetTrigger(string name)
    {
        if (Controller != null)
            Controller.TriggerParameters[name] = false;
    }

    public void UpdateAnimation(float deltaTime)
    {
        if (Controller == null)
            return;

        if (_currentState == null || _currentClip == null)
            TryPlayDefaultState();

        if (!_isPlaying || _currentClip == null)
            return;

        if (_currentClip.Duration <= 0f)
        {
            SampleClip(_currentClip, 0f);
            TryTransition();
            return;
        }

        _time += deltaTime * Speed;

        if (TryTransition())
            return;

        SampleClip(_currentClip, GetSampleTime(_currentClip, _time));

        if (!_currentClip.Loop && _time >= _currentClip.Duration)
        {
            _time = _currentClip.Duration;
            _isPlaying = false;
        }
    }

    public void SampleCurrentState(float time)
    {
        if (_currentClip != null)
            SampleClip(_currentClip, time);
    }

    public void SampleClip(AnimationClip? clip, float time)
    {
        if (clip == null)
            return;

        float sampleTime = GetSampleTime(clip, time);

        foreach (var track in clip.Tracks)
        {
            if (string.IsNullOrEmpty(track.Path))
                continue;

            if (!_bindingCache.TryGetValue(track.Path, out var binding))
            {
                ResolveBinding(track.Path, out var target, out var member);
                binding = (target, member);
                _bindingCache[track.Path] = binding;
            }

            if (binding.Target == null || binding.Member == null)
                continue;

            object val = track.Evaluate(sampleTime);
            ApplyValue(binding.Target, binding.Member, val);
        }
    }

    private void ResolveBinding(string path, out object? target, out MemberInfo? member)
    {
        target = null;
        member = null;

        var parts = path.Split('.');
        if (parts.Length < 2)
            return;

        string typeName = parts[0];
        string memberName = parts[1];

        foreach (var component in Owner.GetAllComponents())
        {
            if (component.GetType().Name == typeName || component.GetType().FullName == typeName)
            {
                target = component;
                break;
            }
        }

        if (target == null)
            return;

        var type = target.GetType();
        member = (MemberInfo?)type.GetProperty(memberName) ?? type.GetField(memberName);
    }

    private void ApplyValue(object target, MemberInfo member, object value)
    {
        try
        {
            Type targetType = member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;
            object? converted = AnimationTypeUtility.ConvertValue(value, targetType);
            if (converted == null)
                return;

            if (member is PropertyInfo writableProperty)
                writableProperty.SetValue(target, converted);
            else if (member is FieldInfo field)
                field.SetValue(target, converted);
        }
        catch (Exception e)
        {
            Verity.Core.Debug.LogError($"Animation Error applying {member.Name}: {e.Message}");
        }
    }

    private void TryPlayDefaultState()
    {
        string? defaultStateName = Controller?.DefaultState?.Name;
        if (!string.IsNullOrWhiteSpace(defaultStateName))
            Play(defaultStateName, restart: false);
    }

    private bool TryTransition()
    {
        if (Controller == null || _currentState == null)
            return false;

        foreach (var transition in _currentState.Transitions)
        {
            if (!CanTransition(transition))
                continue;

            ConsumeTriggers(transition);
            Play(transition.ToState);
            return true;
        }

        return false;
    }

    private bool CanTransition(AnimatorTransition transition)
    {
        if (Controller == null || string.IsNullOrWhiteSpace(transition.ToState))
            return false;

        if (Controller.FindState(transition.ToState) == null)
            return false;

        if (transition.HasExitTime && GetStateProgress() < transition.ExitTime)
            return false;

        foreach (var condition in transition.Conditions)
        {
            if (!MatchesCondition(condition))
                return false;
        }

        return true;
    }

    private bool MatchesCondition(AnimatorCondition condition)
    {
        if (Controller == null || string.IsNullOrWhiteSpace(condition.Parameter))
            return false;

        if (Controller.TriggerParameters.TryGetValue(condition.Parameter, out bool triggerValue))
        {
            return condition.Mode switch
            {
                AnimatorConditionMode.If => triggerValue,
                AnimatorConditionMode.IfNot => !triggerValue,
                AnimatorConditionMode.Equals => triggerValue == (condition.Threshold != 0f),
                AnimatorConditionMode.NotEqual => triggerValue != (condition.Threshold != 0f),
                _ => false
            };
        }

        if (Controller.BoolParameters.TryGetValue(condition.Parameter, out bool boolValue))
        {
            return condition.Mode switch
            {
                AnimatorConditionMode.If => boolValue,
                AnimatorConditionMode.IfNot => !boolValue,
                AnimatorConditionMode.Equals => boolValue == (condition.Threshold != 0f),
                AnimatorConditionMode.NotEqual => boolValue != (condition.Threshold != 0f),
                _ => false
            };
        }

        if (Controller.IntParameters.TryGetValue(condition.Parameter, out int intValue))
            return CompareNumeric(intValue, condition);

        if (Controller.FloatParameters.TryGetValue(condition.Parameter, out float floatValue))
            return CompareNumeric(floatValue, condition);

        return false;
    }

    private static bool CompareNumeric(float value, AnimatorCondition condition)
    {
        return condition.Mode switch
        {
            AnimatorConditionMode.Greater => value > condition.Threshold,
            AnimatorConditionMode.Less => value < condition.Threshold,
            AnimatorConditionMode.Equals => MathF.Abs(value - condition.Threshold) < 0.0001f,
            AnimatorConditionMode.NotEqual => MathF.Abs(value - condition.Threshold) >= 0.0001f,
            AnimatorConditionMode.If => MathF.Abs(value) > 0.0001f,
            AnimatorConditionMode.IfNot => MathF.Abs(value) <= 0.0001f,
            _ => false
        };
    }

    private void ConsumeTriggers(AnimatorTransition transition)
    {
        if (Controller == null)
            return;

        foreach (var condition in transition.Conditions)
        {
            if (Controller.TriggerParameters.ContainsKey(condition.Parameter))
                Controller.TriggerParameters[condition.Parameter] = false;
        }
    }

    private float GetStateProgress()
    {
        if (_currentClip == null || _currentClip.Duration <= 0f)
            return 1f;

        return GetSampleTime(_currentClip, _time) / _currentClip.Duration;
    }

    private static float GetSampleTime(AnimationClip clip, float time)
    {
        if (clip.Duration <= 0f)
            return 0f;

        if (clip.Loop)
        {
            float wrapped = time % clip.Duration;
            return wrapped < 0f ? wrapped + clip.Duration : wrapped;
        }

        return Math.Clamp(time, 0f, clip.Duration);
    }
}
