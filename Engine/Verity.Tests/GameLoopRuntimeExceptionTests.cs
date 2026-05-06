using System.Collections;
using Verity.Core;
using Verity.Core.ECS;
using Verity.Core.Engine;
using Verity.Core.World;

namespace Verity.Tests;

public sealed class GameLoopRuntimeExceptionTests : IDisposable
{
    public GameLoopRuntimeExceptionTests()
    {
        WorldManager.Reset();
        Time.Reset();
    }

    public void Dispose()
    {
        WorldManager.Reset();
        Time.Reset();
    }

    [Fact]
    public void TickLogic_ContinuesToLaterScripts_WhenUpdateThrowsNullReferenceException()
    {
        var world = WorldManager.CreateWorld("NullReferenceContinuation");
        WorldManager.SetActiveWorld(world);
        world.CreateEntity("Thrower").AddComponent<NullReferenceUpdateScript>();
        var observer = world.CreateEntity("Observer").AddComponent<ObserverScript>();

        var loop = new GameLoop();

        int ticks = loop.TickLogic(1f / 60f);

        Assert.Equal(1, ticks);
        Assert.Equal(1, observer.Updates);
    }

    [Fact]
    public void TickLogic_ContinuesToLaterScripts_WhenRuntimeExceptionIsNotNullReference()
    {
        var world = WorldManager.CreateWorld("RuntimeFailure");
        WorldManager.SetActiveWorld(world);
        world.CreateEntity("Thrower").AddComponent<InvalidOperationUpdateScript>();
        var observer = world.CreateEntity("Observer").AddComponent<ObserverScript>();

        var loop = new GameLoop();

        int ticks = loop.TickLogic(1f / 60f);

        Assert.Equal(1, ticks);
        Assert.Equal(1, observer.Updates);
    }

    [Fact]
    public void TickLogic_ContinuesToLaterScripts_WhenCoroutineThrowsNullReferenceException()
    {
        var world = WorldManager.CreateWorld("CoroutineNullReferenceContinuation");
        WorldManager.SetActiveWorld(world);
        world.CreateEntity("Thrower").AddComponent<NullReferenceCoroutineScript>();
        var observer = world.CreateEntity("Observer").AddComponent<ObserverScript>();

        var loop = new GameLoop();

        int ticks = loop.TickLogic(1f / 60f);

        Assert.Equal(1, ticks);
        Assert.Equal(1, observer.Updates);
    }

    private sealed class NullReferenceUpdateScript : Script
    {
        private object? _missing;

        private void Update()
        {
            _ = _missing!.ToString();
        }
    }

    private sealed class InvalidOperationUpdateScript : Script
    {
        private void Update()
        {
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class NullReferenceCoroutineScript : Script
    {
        private IEnumerator Start()
        {
            object? missing = null;
            _ = missing!.ToString();
            yield break;
        }
    }

    private sealed class ObserverScript : Script
    {
        public int Updates { get; private set; }

        private void Update()
        {
            Updates++;
        }
    }
}
