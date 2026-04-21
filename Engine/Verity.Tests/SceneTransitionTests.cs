using Verity.Core;

namespace Verity.Tests;

public sealed class SceneTransitionTests : IDisposable
{
    public SceneTransitionTests()
    {
        EventBus.Clear();
        Verity.Input.Input.Reset();
        Verity.Input.Input.Enabled = true;
    }

    public void Dispose()
    {
        EventBus.Clear();
        Verity.Input.Input.Reset();
        Verity.Input.Input.Enabled = true;
    }

    [Fact]
    public void TransitionTo_FadesOut_LoadsScene_AndFadesIn()
    {
        string? loadedScene = null;
        var transition = new SceneTransition(1f, sceneName => loadedScene = sceneName);

        transition.TransitionTo("Battlefield");

        Assert.True(transition.IsTransitioning);
        Assert.Equal(0f, transition.FadeAlpha);
        Assert.Null(loadedScene);

        transition.Update(0.5f);
        Assert.Equal(0.5f, transition.FadeAlpha, 3);
        Assert.Null(loadedScene);
        Assert.True(transition.IsTransitioning);

        transition.Update(0.5f);
        Assert.Equal(1f, transition.FadeAlpha, 3);
        Assert.Equal("Battlefield", loadedScene);
        Assert.True(transition.IsTransitioning);

        transition.Update(0.5f);
        Assert.Equal(0.5f, transition.FadeAlpha, 3);
        Assert.True(transition.IsTransitioning);

        transition.Update(0.5f);
        Assert.Equal(0f, transition.FadeAlpha, 3);
        Assert.False(transition.IsTransitioning);
    }

    [Fact]
    public void TransitionTo_BlocksInput_UntilTransitionCompletes()
    {
        var transition = new SceneTransition(0.25f, _ => { });

        transition.TransitionTo("Menu");

        Assert.False(Verity.Input.Input.Enabled);
        Assert.True(transition.IsTransitioning);

        transition.Update(0.25f);
        Assert.False(Verity.Input.Input.Enabled);

        transition.Update(0.25f);
        Assert.True(Verity.Input.Input.Enabled);
        Assert.False(transition.IsTransitioning);
    }

    [Fact]
    public void TransitionTo_PublishesCompletionEvent_WhenFadeInFinishes()
    {
        SceneTransitionCompletedEvent? completedEvent = null;
        EventBus.Subscribe<SceneTransitionCompletedEvent>(OnCompleted);

        var transition = new SceneTransition(0.5f, _ => { });
        transition.TransitionTo("Credits");

        transition.Update(0.5f);
        Assert.Null(completedEvent);

        transition.Update(0.5f);
        Assert.Equal(new SceneTransitionCompletedEvent("Credits"), completedEvent);
        Assert.False(transition.IsTransitioning);

        EventBus.Unsubscribe<SceneTransitionCompletedEvent>(OnCompleted);
        return;

        void OnCompleted(SceneTransitionCompletedEvent eventData)
        {
            completedEvent = eventData;
        }
    }
}
