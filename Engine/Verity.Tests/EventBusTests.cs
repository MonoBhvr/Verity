using Verity.Core;

namespace Verity.Tests;

public sealed class EventBusTests : IDisposable
{
    public EventBusTests()
    {
        EventBus.Clear();
    }

    public void Dispose()
    {
        EventBus.Clear();
    }

    [Fact]
    public void Subscribe_And_Publish_ReceivesEvent()
    {
        TestEvent? received = null;

        EventBus.Subscribe<TestEvent>(OnEvent);

        var published = new TestEvent(7);
        EventBus.Publish(published);

        Assert.Equal(published, received);
        return;

        void OnEvent(TestEvent eventData)
        {
            received = eventData;
        }
    }

    [Fact]
    public void Unsubscribe_DoesNotReceiveEvent()
    {
        var wasCalled = false;

        EventBus.Subscribe<TestEvent>(OnEvent);
        EventBus.Unsubscribe<TestEvent>(OnEvent);

        EventBus.Publish(new TestEvent(7));

        Assert.False(wasCalled);
        return;

        void OnEvent(TestEvent _)
        {
            wasCalled = true;
        }
    }

    [Fact]
    public void MultipleSubscribers_AllReceive()
    {
        var firstCallCount = 0;
        var secondCallCount = 0;

        EventBus.Subscribe<TestEvent>(FirstHandler);
        EventBus.Subscribe<TestEvent>(SecondHandler);

        EventBus.Publish(new TestEvent(7));

        Assert.Equal(1, firstCallCount);
        Assert.Equal(1, secondCallCount);
        return;

        void FirstHandler(TestEvent _)
        {
            firstCallCount++;
        }

        void SecondHandler(TestEvent _)
        {
            secondCallCount++;
        }
    }

    [Fact]
    public void DifferentEventTypes_Isolated()
    {
        var testEventCalls = 0;
        var otherEventCalls = 0;

        EventBus.Subscribe<TestEvent>(OnTestEvent);
        EventBus.Subscribe<OtherEvent>(OnOtherEvent);

        EventBus.Publish(new TestEvent(7));

        Assert.Equal(1, testEventCalls);
        Assert.Equal(0, otherEventCalls);
        return;

        void OnTestEvent(TestEvent _)
        {
            testEventCalls++;
        }

        void OnOtherEvent(OtherEvent _)
        {
            otherEventCalls++;
        }
    }

    [Fact]
    public void Publish_NoSubscribers_NoException()
    {
        var exception = Record.Exception(() => EventBus.Publish(new TestEvent(7)));

        Assert.Null(exception);
    }

    private readonly record struct TestEvent(int Value);
    private readonly record struct OtherEvent(string Name);
}
