using Verity.Core;

namespace Verity.Tests;

public sealed class ObjectPoolTests
{
    [Fact]
    public void Get_ReturnsNewInstance_WhenEmpty()
    {
        var pool = new ObjectPool<PooledObject>();

        var item = pool.Get();

        Assert.NotNull(item);
        Assert.Equal(0, pool.Count);
    }

    [Fact]
    public void Return_And_Get_ReusesInstance()
    {
        var pool = new ObjectPool<PooledObject>();
        var item = pool.Get();

        pool.Return(item);
        var reused = pool.Get();

        Assert.Same(item, reused);
        Assert.Equal(0, pool.Count);
    }

    [Fact]
    public void Multiple_Get_Return_Cycle()
    {
        var pool = new ObjectPool<PooledObject>();
        var first = pool.Get();
        var second = pool.Get();

        pool.Return(first);
        pool.Return(second);

        var reusedFirst = pool.Get();
        var reusedSecond = pool.Get();

        Assert.Contains(reusedFirst, new[] { first, second });
        Assert.Contains(reusedSecond, new[] { first, second });
        Assert.NotSame(reusedFirst, reusedSecond);
        Assert.Equal(0, pool.Count);
    }

    [Fact]
    public void PreallocatedCapacity()
    {
        var pool = new ObjectPool<PooledObject>(initialCapacity: 3);

        Assert.Equal(3, pool.Count);

        var first = pool.Get();
        var second = pool.Get();
        var third = pool.Get();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(third);
        Assert.Equal(0, pool.Count);
    }

    [Fact]
    public void Get_AutoExpands()
    {
        var pool = new ObjectPool<PooledObject>(initialCapacity: 1);
        var first = pool.Get();
        var second = pool.Get();

        pool.Return(first);
        pool.Return(second);

        Assert.NotSame(first, second);
        Assert.Equal(2, pool.Count);
    }

    [Fact]
    public void Clear_RemovesAllInstances()
    {
        var pool = new ObjectPool<PooledObject>(initialCapacity: 1);
        var item = pool.Get();

        pool.Return(item);
        pool.Clear();

        var next = pool.Get();

        Assert.Equal(0, pool.Count);
        Assert.NotSame(item, next);
    }

    private sealed class PooledObject
    {
    }
}
