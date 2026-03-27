using Verity.Core.ECS;

namespace Verity.Core.Animation;

public static class AnimationSystem
{
    private static readonly List<Animator> _animators = new();

    public static void Register(Animator animator)
    {
        if (!_animators.Contains(animator))
            _animators.Add(animator);
    }

    public static void Unregister(Animator animator)
    {
        _animators.Remove(animator);
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
    }
}
