using Verity.Core.Animation;

namespace Verity.Tests;

public class SanityTest
{
    [Fact]
    public void AnimationClip_CanBeConstructed()
    {
        var clip = new AnimationClip();

        Assert.NotNull(clip);
        Assert.True(true);
    }
}
