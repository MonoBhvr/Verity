using System.Text.Json.Serialization;

namespace Verity.Core.Animation;

public enum AnimatorConditionMode
{
    If,
    IfNot,
    Greater,
    Less,
    Equals,
    NotEqual
}

public class AnimatorCondition
{
    public string Parameter { get; set; } = "";
    public AnimatorConditionMode Mode { get; set; } = AnimatorConditionMode.If;
    public float Threshold { get; set; }
}

public class AnimatorTransition
{
    public string ToState { get; set; } = "";
    public bool HasExitTime { get; set; }
    public float ExitTime { get; set; } = 1.0f;
    public List<AnimatorCondition> Conditions { get; set; } = new();
}

public class AnimatorState
{
    public string Name { get; set; } = "New State";
    public AnimationClip? Clip { get; set; }
    public List<AnimatorTransition> Transitions { get; set; } = new();
}

public class AnimatorController
{
    public List<AnimatorState> States { get; set; } = new();
    public string DefaultStateName { get; set; } = "";
    
    // Parameters (Float, Int, Bool, Trigger)
    public Dictionary<string, float> FloatParameters { get; set; } = new();
    public Dictionary<string, int> IntParameters { get; set; } = new();
    public Dictionary<string, bool> BoolParameters { get; set; } = new();
    public Dictionary<string, bool> TriggerParameters { get; set; } = new();

    [JsonIgnore]
    public AnimatorState? DefaultState => FindState(DefaultStateName) ?? States.FirstOrDefault();

    public AnimatorState? FindState(string? stateName)
    {
        return string.IsNullOrWhiteSpace(stateName)
            ? null
            : States.FirstOrDefault(s => string.Equals(s.Name, stateName, StringComparison.Ordinal));
    }

    public void AddState(AnimatorState state)
    {
        States.Add(state);
        if (States.Count == 1 || string.IsNullOrWhiteSpace(DefaultStateName))
            DefaultStateName = state.Name;
    }

    public void PostLoad()
    {
        foreach (var state in States)
            state.Clip?.PostLoad();

        if (string.IsNullOrWhiteSpace(DefaultStateName) && States.Count > 0)
            DefaultStateName = States[0].Name;
    }
}
