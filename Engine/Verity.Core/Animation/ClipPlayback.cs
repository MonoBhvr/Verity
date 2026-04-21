using System.Reflection;
using Verity.Core.ECS;

namespace Verity.Core.Animation;

public static class ClipPlayback
{
    public static void Play(
        AnimationPlaybackState state,
        string stateName,
        AnimationClipBase? clip,
        bool restart,
        float fadeDuration)
    {
        if (clip == null)
        {
            state.Reset();
            return;
        }

        if (!restart && ReferenceEquals(state.CurrentClip, clip) && state.IsPlaying)
            return;

        state.PreviousClip = fadeDuration > 0f ? state.CurrentClip : null;
        state.PreviousTime = state.CurrentTime;
        state.CurrentStateName = stateName;
        state.CurrentClip = clip;
        state.CurrentTime = 0f;
        state.IsPlaying = true;
        state.FadeDuration = Math.Max(0f, fadeDuration);
        state.FadeTime = 0f;
        if (state.PreviousClip == null)
        {
            state.PreviousTime = 0f;
            state.FadeDuration = 0f;
        }
    }

    public static bool Update(
        AnimationPlaybackState state,
        Entity owner,
        Dictionary<string, (object? Target, MemberInfo? Member)> bindingCache,
        float deltaTime,
        float speed,
        NonInterpolatedSwitchMode switchMode,
        float threshold,
        Action<string>? animationEventFired)
    {
        if (!state.IsPlaying || state.CurrentClip == null)
            return false;

        var clip = state.CurrentClip;
        float previousRawTime = state.CurrentTime;

        if (clip.Duration <= 0f)
        {
            Sample(owner, bindingCache, state);
            FireEvents(clip, previousRawTime, previousRawTime, animationEventFired);
            state.IsPlaying = clip.Loop;
            return !state.IsPlaying;
        }

        float scaledDelta = deltaTime * speed;
        state.CurrentTime += scaledDelta;
        if (state.PreviousClip != null)
            state.PreviousTime += scaledDelta;
        if (state.PreviousClip != null && state.FadeDuration > 0f)
            state.FadeTime += Math.Abs(scaledDelta);

        FireEvents(clip, previousRawTime, state.CurrentTime, animationEventFired);
        Sample(owner, bindingCache, state, switchMode, threshold);

        if (state.PreviousClip != null && (!state.IsFading || state.FadeDuration <= 0f))
        {
            state.PreviousClip = null;
            state.PreviousTime = 0f;
            state.FadeDuration = 0f;
            state.FadeTime = 0f;
        }

        if (!clip.Loop && state.CurrentTime >= clip.Duration)
        {
            state.CurrentTime = clip.Duration;
            Sample(owner, bindingCache, state, switchMode, threshold);
            state.IsPlaying = false;
            return true;
        }

        return false;
    }

    public static void Sample(Entity owner, Dictionary<string, (object? Target, MemberInfo? Member)> bindingCache, AnimationPlaybackState state, NonInterpolatedSwitchMode switchMode = NonInterpolatedSwitchMode.ImmediateSwitch, float threshold = 0.5f)
    {
        if (state.CurrentClip == null)
            return;

        if (state.IsFading && state.PreviousClip != null)
        {
            float weight = Math.Clamp(state.FadeTime / Math.Max(0.0001f, state.FadeDuration), 0f, 1f);
            SampleBlend(owner, bindingCache, state.PreviousClip, state.PreviousTime, state.CurrentClip, state.CurrentTime, weight, switchMode, threshold);
            return;
        }

        SampleClip(owner, bindingCache, state.CurrentClip, state.CurrentTime);
    }

    public static void SampleClip(Entity owner, Dictionary<string, (object? Target, MemberInfo? Member)> bindingCache, AnimationClipBase clip, float time)
    {
        if (clip is SpriteAnimationClip && !owner.GetAllComponents().Any(component => string.Equals(component.GetType().Name, "SpriteRenderer", StringComparison.Ordinal)))
        {
            Verity.Core.Debug.LogWarning($"SpriteAnimationClip '{clip.Name}' requires a SpriteRenderer on entity '{owner.Name}'.");
            return;
        }

        float sampleTime = GetSampleTime(clip, time);
        foreach (var track in clip.Tracks)
        {
            if (string.IsNullOrWhiteSpace(track.Path))
                continue;

            if (!bindingCache.TryGetValue(track.Path, out var binding))
            {
                ResolveBinding(owner, track.Path, out var target, out var member);
                binding = (target, member);
                bindingCache[track.Path] = binding;
            }

            if (binding.Target == null || binding.Member == null)
                continue;

            object? value = track.Evaluate(sampleTime);
            if (value != null)
                ApplyValue(binding.Target, binding.Member, value);
        }
    }

    private static void SampleBlend(
        Entity owner,
        Dictionary<string, (object? Target, MemberInfo? Member)> bindingCache,
        AnimationClipBase fromClip,
        float fromTime,
        AnimationClipBase toClip,
        float toTime,
        float weight,
        NonInterpolatedSwitchMode switchMode,
        float threshold)
    {
        float fromSampleTime = GetSampleTime(fromClip, fromTime);
        float toSampleTime = GetSampleTime(toClip, toTime);

        var paths = new HashSet<string>(fromClip.Tracks.Select(track => track.Path), StringComparer.Ordinal);
        paths.UnionWith(toClip.Tracks.Select(track => track.Path));

        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            if (!bindingCache.TryGetValue(path, out var binding))
            {
                ResolveBinding(owner, path, out var target, out var member);
                binding = (target, member);
                bindingCache[path] = binding;
            }

            if (binding.Target == null || binding.Member == null)
                continue;

            AnimationTrack? fromTrack = fromClip.Tracks.FirstOrDefault(track => string.Equals(track.Path, path, StringComparison.Ordinal));
            AnimationTrack? toTrack = toClip.Tracks.FirstOrDefault(track => string.Equals(track.Path, path, StringComparison.Ordinal));

            object? fromValue = fromTrack?.Evaluate(fromSampleTime);
            object? toValue = toTrack?.Evaluate(toSampleTime);
            object? result = BlendValues(fromValue, toValue, weight, switchMode, threshold);
            if (result != null)
                ApplyValue(binding.Target, binding.Member, result);
        }
    }

    public static float GetSampleTime(AnimationClipBase clip, float time)
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

    private static object? BlendValues(object? fromValue, object? toValue, float weight, NonInterpolatedSwitchMode switchMode, float threshold)
    {
        if (fromValue == null)
            return toValue;
        if (toValue == null)
            return fromValue;

        Type fromType = fromValue.GetType();
        if (fromType != toValue.GetType())
            return weight >= 1f ? toValue : fromValue;

        if (!AnimationTypeUtility.IsInterpolatedType(fromType))
        {
            return switchMode switch
            {
                NonInterpolatedSwitchMode.ImmediateSwitch => toValue,
                NonInterpolatedSwitchMode.Threshold => weight >= threshold ? toValue : fromValue,
                _ => weight >= 1f ? toValue : fromValue
            };
        }

        if (fromValue is float fromFloat && toValue is float toFloat)
            return fromFloat + (toFloat - fromFloat) * weight;
        if (fromValue is int fromInt && toValue is int toInt)
            return (int)MathF.Round(fromInt + (toInt - fromInt) * weight);
        if (fromValue is Verity.Core.Vector2 fromCoreVec2 && toValue is Verity.Core.Vector2 toCoreVec2)
            return Verity.Core.Vector2.Lerp(fromCoreVec2, toCoreVec2, weight);
        if (fromValue is System.Numerics.Vector2 fromVec2 && toValue is System.Numerics.Vector2 toVec2)
            return System.Numerics.Vector2.Lerp(fromVec2, toVec2, weight);
        if (fromValue is Verity.Core.Vector3 fromCoreVec3 && toValue is Verity.Core.Vector3 toCoreVec3)
            return Verity.Core.Vector3.Lerp(fromCoreVec3, toCoreVec3, weight);
        if (fromValue is System.Numerics.Vector3 fromVec3 && toValue is System.Numerics.Vector3 toVec3)
            return System.Numerics.Vector3.Lerp(fromVec3, toVec3, weight);
        if (fromValue is System.Numerics.Vector4 fromVec4 && toValue is System.Numerics.Vector4 toVec4)
            return System.Numerics.Vector4.Lerp(fromVec4, toVec4, weight);
        if (fromValue is Color fromColor && toValue is Color toColor)
        {
            return new Color(
                fromColor.R + (toColor.R - fromColor.R) * weight,
                fromColor.G + (toColor.G - fromColor.G) * weight,
                fromColor.B + (toColor.B - fromColor.B) * weight,
                fromColor.A + (toColor.A - fromColor.A) * weight);
        }

        return switchMode switch
        {
            NonInterpolatedSwitchMode.ImmediateSwitch => toValue,
            NonInterpolatedSwitchMode.Threshold => weight >= threshold ? toValue : fromValue,
            _ => weight >= 1f ? toValue : fromValue
        };
    }

    private static void FireEvents(AnimationClipBase clip, float previousRawTime, float currentRawTime, Action<string>? animationEventFired)
    {
        if (animationEventFired == null || clip.Events.Count == 0)
            return;

        int previousFrame = ToEventFrame(clip, previousRawTime);
        int currentFrame = ToEventFrame(clip, currentRawTime);

        if (clip.Duration <= 0f || (!clip.Loop && currentFrame == previousFrame))
        {
            foreach (var animationEvent in clip.Events.Where(animationEvent => animationEvent.Frame == 0))
                animationEventFired(animationEvent.Name);
            return;
        }

        if (!clip.Loop)
        {
            int from = Math.Max(0, previousFrame);
            int to = Math.Max(0, currentFrame);
            foreach (var animationEvent in clip.Events)
            {
                if (animationEvent.Frame > from && animationEvent.Frame <= to)
                    animationEventFired(animationEvent.Name);
            }
            return;
        }

        int totalFrames = Math.Max(1, ToEventFrame(clip, clip.Duration));
        int fromWrapped = Mod(previousFrame, totalFrames);
        int toWrapped = Mod(currentFrame, totalFrames);
        bool wrapped = currentRawTime - previousRawTime >= clip.Duration || toWrapped < fromWrapped;

        foreach (var animationEvent in clip.Events)
        {
            if (!wrapped)
            {
                if (animationEvent.Frame > fromWrapped && animationEvent.Frame <= toWrapped)
                    animationEventFired(animationEvent.Name);
            }
            else if (animationEvent.Frame > fromWrapped || animationEvent.Frame <= toWrapped)
            {
                animationEventFired(animationEvent.Name);
            }
        }
    }

    private static int ToEventFrame(AnimationClipBase clip, float time)
    {
        float safeRate = Math.Max(1f, clip.FrameRate);
        return (int)MathF.Floor(GetSampleTime(clip, time) * safeRate + 0.0001f);
    }

    private static int Mod(int value, int divisor)
    {
        int result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private static void ResolveBinding(Entity owner, string path, out object? target, out MemberInfo? member)
    {
        target = null;
        member = null;

        int separatorIndex = path.LastIndexOf('.');
        if (separatorIndex <= 0 || separatorIndex >= path.Length - 1)
            return;

        string typeName = path[..separatorIndex];
        string memberName = path[(separatorIndex + 1)..];

        foreach (var component in owner.GetAllComponents())
        {
            if (component.GetType().Name == typeName || component.GetType().FullName == typeName)
            {
                target = component;
                break;
            }
        }

        if (target == null)
            return;

        Type type = target.GetType();
        member = (MemberInfo?)type.GetProperty(memberName) ?? type.GetField(memberName);
    }

    private static void ApplyValue(object target, MemberInfo member, object value)
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
}
