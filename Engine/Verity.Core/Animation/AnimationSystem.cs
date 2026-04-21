using Verity.Core.ECS;

namespace Verity.Core.Animation;

public static class AnimationSystem
{
    private static readonly List<Animator> _animators = new();
    private static readonly List<ClipAnimator> _clipAnimators = new();

    public static void Register(Animator animator)
    {
        if (!_animators.Contains(animator))
            _animators.Add(animator);
    }

    public static void Unregister(Animator animator)
    {
        _animators.Remove(animator);
    }

    public static void Register(ClipAnimator animator)
    {
        if (!_clipAnimators.Contains(animator))
            _clipAnimators.Add(animator);
    }

    public static void Unregister(ClipAnimator animator)
    {
        _clipAnimators.Remove(animator);
    }

    public static void Update(float deltaTime)
    {
        for (int i = 0; i < _animators.Count; i++)
        {
            if (_animators[i].Enabled && _animators[i].Owner.Active)
            {
                _animators[i].UpdateAnimation(deltaTime);
            }
        }

        for (int i = 0; i < _clipAnimators.Count; i++)
        {
            if (_clipAnimators[i].Enabled && _clipAnimators[i].Owner.Active)
            {
                _clipAnimators[i].UpdateAnimation(deltaTime);
            }
        }
    }
}
