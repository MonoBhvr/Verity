using Verity.Core.Animation;
using Verity.Core.ECS;

namespace Verity.Tests;

public sealed class AnimatorDiscoveryTests
{
    [Fact]
    public void Play_Stop_AndPauseSurface_AreDiscoverable()
    {
        Entity entity = CreateEntity(out AnimationTestProbe probe);
        Animator animator = entity.AddComponent<Animator>();
        animator.Controller = CreateController(
            CreateState("Idle", CreateClip($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 0f), (1f, 10f))));

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
        Assert.NotNull(typeof(Animator).GetMethod("Pause", Type.EmptyTypes));
    }

    [Fact]
    public void ConditionTransitions_UseFloatIntBoolAndTriggerParameters()
    {
        Entity entity = CreateEntity(out _);
        Animator animator = entity.AddComponent<Animator>();

        AnimatorState idle = CreateState(
            "Idle",
            CreateClip($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 0f), (1f, 1f)),
            Transition("Walk", new AnimatorCondition { Parameter = "speed", Mode = AnimatorConditionMode.Greater, Threshold = 0.5f }));
        AnimatorState walk = CreateState(
            "Walk",
            CreateClip($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 1f), (1f, 2f)),
            Transition("Attack", new AnimatorCondition { Parameter = "combo", Mode = AnimatorConditionMode.Equals, Threshold = 2f }));
        AnimatorState attack = CreateState(
            "Attack",
            CreateClip($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 2f), (1f, 3f)),
            Transition("Guard", new AnimatorCondition { Parameter = "guard", Mode = AnimatorConditionMode.If }));
        AnimatorState guard = CreateState(
            "Guard",
            CreateClip($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 3f), (1f, 4f)),
            Transition("Done", new AnimatorCondition { Parameter = "fire", Mode = AnimatorConditionMode.If }));
        AnimatorState done = CreateState("Done", CreateClip($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 4f), (1f, 5f)));

        animator.Controller = CreateController(idle, walk, attack, guard, done);

        animator.SetFloat("speed", 1f);
        animator.UpdateAnimation(0.1f);
        Assert.Equal("Walk", animator.CurrentStateName);
        Assert.Equal(1f, animator.Controller!.FloatParameters["speed"]);

        animator.SetInt("combo", 2);
        animator.UpdateAnimation(0.1f);
        Assert.Equal("Attack", animator.CurrentStateName);
        Assert.Equal(2, animator.Controller.IntParameters["combo"]);

        animator.SetBool("guard", true);
        animator.UpdateAnimation(0.1f);
        Assert.Equal("Guard", animator.CurrentStateName);
        Assert.True(animator.Controller.BoolParameters["guard"]);

        animator.SetTrigger("fire");
        animator.UpdateAnimation(0.1f);
        Assert.Equal("Done", animator.CurrentStateName);
        Assert.False(animator.Controller.TriggerParameters["fire"]);
    }

    [Fact]
    public void LoopPlayback_WrapsAndContinuesSampling()
    {
        Entity entity = CreateEntity(out AnimationTestProbe probe);
        Animator animator = entity.AddComponent<Animator>();
        AnimationClip clip = CreateClip($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 0f), (1f, 10f));
        clip.Loop = true;
        clip.PostLoad();
        animator.Controller = CreateController(CreateState("Loop", clip));

        animator.UpdateAnimation(1.25f);

        Assert.True(animator.IsPlaying);
        Assert.Equal(1.25f, animator.CurrentTime, 3);
        Assert.Equal(2.5f, probe.FloatValue, 3);
    }

    [Fact]
    public void KeyframeEvaluation_UsesLinearAndStepInterpolation()
    {
        Entity entity = CreateEntity(out AnimationTestProbe probe);
        Animator animator = entity.AddComponent<Animator>();
        AnimationClip clip = new();
        clip.AddTrack(CreateTrack($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 0f), (1f, 10f)));
        clip.AddTrack(CreateTrack($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.TextValue)}", (0f, "Idle"), (1f, "Run")));
        clip.PostLoad();
        animator.Controller = CreateController(CreateState("Blend", clip));

        animator.UpdateAnimation(0.75f);

        Assert.Equal(7.5f, probe.FloatValue, 3);
        Assert.Equal("Idle", probe.TextValue);
    }

    [Fact]
    public void BindingPathResolution_SupportsSimpleAndFullTypeNames()
    {
        Entity entity = CreateEntity(out AnimationTestProbe probe);
        Animator animator = entity.AddComponent<Animator>();
        AnimationClip clip = new();
        clip.AddTrack(CreateTrack($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FieldValue)}", (0f, 2f), (1f, 6f)));
        clip.AddTrack(CreateTrack($"{typeof(AnimationTestProbe).FullName}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 1f), (1f, 5f)));
        clip.AddTrack(CreateTrack("MissingProbe.FloatValue", (0f, 99f), (1f, 100f)));
        clip.PostLoad();
        animator.Controller = CreateController(CreateState("Bind", clip));

        animator.UpdateAnimation(0.5f);

        Assert.Equal(4f, probe.FieldValue, 3);
        Assert.Equal(3f, probe.FloatValue, 3);
    }

    [Fact]
    public void ReplacingController_SwitchesToTheNewDefaultState()
    {
        Entity entity = CreateEntity(out AnimationTestProbe probe);
        Animator animator = entity.AddComponent<Animator>();
        animator.Controller = CreateController(
            CreateState("Idle", CreateClip($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 1f), (1f, 2f))));

        Assert.Equal("Idle", animator.CurrentStateName);
        Assert.Equal(1f, probe.FloatValue, 3);

        animator.Controller = CreateController(
            CreateState("Run", CreateClip($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 10f), (1f, 20f))));

        Assert.Equal("Run", animator.CurrentStateName);
        Assert.True(animator.IsPlaying);
        Assert.Equal(0f, animator.CurrentTime, 3);
        Assert.Equal(10f, probe.FloatValue, 3);
    }

    [Fact]
    public void MultipleAnimators_UpdateIndependently()
    {
        Entity firstEntity = CreateEntity(out AnimationTestProbe firstProbe);
        Animator firstAnimator = firstEntity.AddComponent<Animator>();
        firstAnimator.Controller = CreateController(
            CreateState("First", CreateClip($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 0f), (1f, 10f))));

        Entity secondEntity = CreateEntity(out AnimationTestProbe secondProbe);
        Animator secondAnimator = secondEntity.AddComponent<Animator>();
        secondAnimator.Controller = CreateController(
            CreateState("Second", CreateClip($"{nameof(AnimationTestProbe)}.{nameof(AnimationTestProbe.FloatValue)}", (0f, 0f), (1f, 20f))));

        firstAnimator.UpdateAnimation(0.5f);
        secondAnimator.UpdateAnimation(0.25f);

        Assert.Equal(5f, firstProbe.FloatValue, 3);
        Assert.Equal(5f, secondProbe.FloatValue, 3);
        Assert.Equal(0.5f, firstAnimator.CurrentTime, 3);
        Assert.Equal(0.25f, secondAnimator.CurrentTime, 3);
    }

    private static Entity CreateEntity(out AnimationTestProbe probe)
    {
        Entity entity = new("Animation Test Entity");
        probe = entity.AddComponent<AnimationTestProbe>();
        return entity;
    }

    private static AnimatorController CreateController(params AnimatorState[] states)
    {
        AnimatorController controller = new();
        foreach (AnimatorState state in states)
            controller.AddState(state);
        return controller;
    }

    private static AnimatorState CreateState(string name, AnimationClip clip, params AnimatorTransition[] transitions)
    {
        AnimatorState state = new()
        {
            Name = name,
            Clip = clip
        };

        foreach (AnimatorTransition transition in transitions)
            state.Transitions.Add(transition);

        return state;
    }

    private static AnimatorTransition Transition(string toState, params AnimatorCondition[] conditions)
    {
        AnimatorTransition transition = new()
        {
            ToState = toState
        };

        foreach (AnimatorCondition condition in conditions)
            transition.Conditions.Add(condition);

        return transition;
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
