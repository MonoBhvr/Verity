using Verity.Core.Animation;
using Verity.Core.ECS;

namespace Verity.Tests;

public sealed class ClipAnimatorDiscoveryTests
{
    [Fact]
    public void Play_Stop_AndPauseSurface_AreDiscoverable()
    {
        Entity entity = CreateEntity(out AnimationTestProbe probe);
        ClipAnimator animator = entity.AddComponent<ClipAnimator>();
        animator.PlayOnEnable = false;
        animator.States.Add(new ClipAnimatorState
        {
            Name = "Idle",
            Clip = CreateClip($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 0f), (1f, 10f))
        });
        animator.DefaultStateName = "Idle";

        animator.Play("Idle");
        animator.UpdateAnimation(0.25f);

        Assert.True(animator.IsPlaying);
        Assert.Equal("Idle", animator.CurrentStateName);
        Assert.Equal(0.25f, animator.CurrentTime, 3);
        Assert.Equal(2.5f, probe.FloatValue, 3);

        animator.Pause();

        Assert.False(animator.IsPlaying);
        Assert.True(animator.IsPaused);
        Assert.Equal(0.25f, animator.CurrentTime, 3);

        animator.Resume();
        animator.UpdateAnimation(0.25f);

        Assert.True(animator.IsPlaying);
        Assert.False(animator.IsPaused);
        Assert.Equal(0.5f, animator.CurrentTime, 3);
        Assert.Equal(5f, probe.FloatValue, 3);

        animator.Stop();

        Assert.False(animator.IsPlaying);
        Assert.False(animator.IsPaused);
        Assert.Equal(0f, animator.CurrentTime, 3);
        Assert.NotNull(typeof(ClipAnimator).GetMethod("Pause", Type.EmptyTypes));
    }

    [Fact]
    public void DiscoverySurface_HasManualStateSwitchingButNoControllerOrParameterApi()
    {
        Assert.NotNull(typeof(ClipAnimator).GetMethod(nameof(ClipAnimator.PlayIfChanged), new[] { typeof(string) }));
        Assert.NotNull(typeof(ClipAnimator).GetProperty(nameof(ClipAnimator.States)));

        Assert.Null(typeof(ClipAnimator).GetProperty("Controller"));
        Assert.Null(typeof(ClipAnimator).GetMethod("SetFloat", new[] { typeof(string), typeof(float) }));
        Assert.Null(typeof(ClipAnimator).GetMethod("SetInt", new[] { typeof(string), typeof(int) }));
        Assert.Null(typeof(ClipAnimator).GetMethod("SetBool", new[] { typeof(string), typeof(bool) }));
        Assert.Null(typeof(ClipAnimator).GetMethod("SetTrigger", new[] { typeof(string) }));
        Assert.DoesNotContain(typeof(ClipAnimatorState).GetProperties(), property => property.Name.Contains("Transition", StringComparison.Ordinal));
    }

    [Fact]
    public void LoopPlayback_WrapsAndContinuesSampling()
    {
        Entity entity = CreateEntity(out AnimationTestProbe probe);
        ClipAnimator animator = entity.AddComponent<ClipAnimator>();
        animator.PlayOnEnable = false;

        AnimationClip clip = CreateClip($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 0f), (1f, 10f));
        clip.Loop = true;
        clip.PostLoad();

        animator.States.Add(new ClipAnimatorState { Name = "Loop", Clip = clip });

        animator.Play("Loop");
        animator.UpdateAnimation(1.25f);

        Assert.True(animator.IsPlaying);
        Assert.Equal(1.25f, animator.CurrentTime, 3);
        Assert.Equal(2.5f, probe.FloatValue, 3);
    }

    [Fact]
    public void KeyframeEvaluation_UsesLinearAndStepInterpolation()
    {
        Entity entity = CreateEntity(out AnimationTestProbe probe);
        ClipAnimator animator = entity.AddComponent<ClipAnimator>();
        animator.PlayOnEnable = false;

        AnimationClip clip = new();
        clip.AddTrack(CreateTrack($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 0f), (1f, 10f)));
        clip.AddTrack(CreateTrack($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.BoolValue)}", (0f, false), (1f, true)));
        clip.PostLoad();

        animator.States.Add(new ClipAnimatorState { Name = "Blend", Clip = clip });

        animator.Play("Blend");
        animator.UpdateAnimation(0.75f);

        Assert.Equal(7.5f, probe.FloatValue, 3);
        Assert.False(probe.BoolValue);
    }

    [Fact]
    public void BindingPathResolution_SupportsSimpleAndFullTypeNames()
    {
        Entity entity = CreateEntity(out AnimationTestProbe probe);
        ClipAnimator animator = entity.AddComponent<ClipAnimator>();
        animator.PlayOnEnable = false;

        AnimationClip clip = new();
        clip.AddTrack(CreateTrack($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FieldValue)}", (0f, 2f), (1f, 6f)));
        clip.AddTrack(CreateTrack($"{typeof(AnimationTestProbe).FullName}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 1f), (1f, 5f)));
        clip.AddTrack(CreateTrack("MissingProbe.FloatValue", (0f, 99f), (1f, 100f)));
        clip.PostLoad();

        animator.States.Add(new ClipAnimatorState { Name = "Bind", Clip = clip });

        animator.Play("Bind");
        animator.UpdateAnimation(0.5f);

        Assert.Equal(4f, probe.FieldValue, 3);
        Assert.Equal(3f, probe.FloatValue, 3);
    }

    [Fact]
    public void ReplacingDefaultClip_ResetsPlaybackAndSamplesNewClip()
    {
        Entity entity = CreateEntity(out AnimationTestProbe probe);
        ClipAnimator animator = entity.AddComponent<ClipAnimator>();
        animator.PlayOnEnable = true;

        animator.Clip = CreateClip($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 1f), (1f, 2f));
        animator.UpdateAnimation(0.5f);
        Assert.Equal(1.5f, probe.FloatValue, 3);

        animator.Clip = CreateClip($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 10f), (1f, 20f));

        Assert.Equal("Default", animator.CurrentStateName);
        Assert.True(animator.IsPlaying);
        Assert.Equal(0f, animator.CurrentTime, 3);
        Assert.Equal(10f, probe.FloatValue, 3);
    }

    [Fact]
    public void MultipleAnimators_UpdateIndependently()
    {
        Entity firstEntity = CreateEntity(out AnimationTestProbe firstProbe);
        ClipAnimator firstAnimator = firstEntity.AddComponent<ClipAnimator>();
        firstAnimator.PlayOnEnable = false;
        firstAnimator.States.Add(new ClipAnimatorState
        {
            Name = "First",
            Clip = CreateClip($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 0f), (1f, 10f))
        });

        Entity secondEntity = CreateEntity(out AnimationTestProbe secondProbe);
        ClipAnimator secondAnimator = secondEntity.AddComponent<ClipAnimator>();
        secondAnimator.PlayOnEnable = false;
        secondAnimator.States.Add(new ClipAnimatorState
        {
            Name = "Second",
            Clip = CreateClip($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 0f), (1f, 20f))
        });

        firstAnimator.Play("First");
        secondAnimator.Play("Second");
        firstAnimator.UpdateAnimation(0.5f);
        secondAnimator.UpdateAnimation(0.25f);

        Assert.Equal(5f, firstProbe.FloatValue, 3);
        Assert.Equal(5f, secondProbe.FloatValue, 3);
        Assert.Equal(0.5f, firstAnimator.CurrentTime, 3);
        Assert.Equal(0.25f, secondAnimator.CurrentTime, 3);
    }

    private static Entity CreateEntity(out AnimationTestProbe probe)
    {
        Entity entity = new("Clip Animation Test Entity");
        probe = entity.AddComponent<AnimationTestProbe>();
        return entity;
    }

    private static AnimationClip CreateClip(string path, params (float Time, object Value)[] keyframes)
    {
        AnimationClip clip = new();
        clip.AddTrack(CreateTrack(path, keyframes));
        clip.PostLoad();
        return clip;
    }

    private static AnimationTrack CreateTrack(string path, params (float Time, object Value)[] keyframes)
    {
        Type valueType = keyframes[0].Value.GetType();
        AnimationTrack track = new()
        {
            Path = path,
            TypeName = AnimationTypeUtility.GetTypeName(valueType)
        };

        foreach ((float time, object value) in keyframes)
            track.Keyframes.Add(new Keyframe(time, value));

        return track;
    }
}
