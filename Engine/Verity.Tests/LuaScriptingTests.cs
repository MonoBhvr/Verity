using Verity.Core;
using Verity.Core.ECS;
using Verity.Core.Engine;
using Verity.Core.Scripting;
using Verity.Core.World;
using Verity.Input;

namespace Verity.Tests;

public sealed class LuaScriptingTests : IDisposable
{
    private readonly string _projectRoot;
    private readonly string _assetsPath;
    private readonly List<string> _logs = [];

    public LuaScriptingTests()
    {
        _projectRoot = Path.Combine(Path.GetTempPath(), "VerityLuaTests", Guid.NewGuid().ToString("N"));
        _assetsPath = Path.Combine(_projectRoot, "Assets");
        Directory.CreateDirectory(_assetsPath);
        Verity.Input.Input.Reset();
        Verity.Input.Input.Enabled = true;
        WorldManager.Reset();
        LuaScriptManager.Dispose();
        LuaScriptManager.SuspendHotReloadEvents = false;
        Time.Reset();
        Debug.OnLog += OnLog;
    }

    [Fact]
    public void LuaBindings_ExposeEngineApisAndUserComponents()
    {
        string scriptPath = WriteLuaScript("bindings.lua", """
local custom = nil

function Start()
    custom = self.Owner:GetComponent("LuaTestUserComponent")
    custom.Counter = custom.Counter + 3
    custom:Increment(2)
    self.Owner.Transform.Position = Vector2(10, 20)
    local vector = Vector3(1, 2, 3)
    custom.LastVectorZ = vector.Z
    custom.LastDelta = Time.DeltaTime
    custom.SpaceDown = Input.IsKeyDown(Keys.Space)
end
""");

        LuaScriptManager.Initialize(_projectRoot, typeof(LuaTestUserComponent).Assembly);
        World world = WorldManager.CreateWorld("Bindings");
        WorldManager.SetActiveWorld(world);
        Entity entity = world.CreateEntity("Player");
        LuaTestUserComponent custom = entity.AddComponent<LuaTestUserComponent>();
        LuaScriptComponent lua = entity.AddComponent<LuaScriptComponent>();
        lua.ScriptPath = AssetPath(scriptPath);
        Time.DeltaTime = 0.25f;

        Assert.True(lua.HasLoadedState);
        Assert.True(lua.HasStartFunction);

        lua._startDelegate?.Invoke();
        lua.UpdateCoroutines(0f);

        Assert.True(_logs.All(static log => !log.Contains("[Lua] Runtime error", StringComparison.Ordinal)), string.Join(Environment.NewLine, _logs));
        Assert.Equal(5, custom.Counter);
        Assert.Equal(3f, custom.LastVectorZ);
        Assert.Equal(0.25f, custom.LastDelta);
        Assert.False(custom.SpaceDown);
        Assert.Equal(new Vector2(10, 20), entity.Transform.Position);
    }

    [Fact]
    public void LuaCoroutines_RunThroughScriptCoroutineLoop()
    {
        string scriptPath = WriteLuaScript("coroutine.lua", """
function Start()
    local custom = self.Owner:GetComponent("LuaTestUserComponent")
    custom.Counter = 1
    coroutine.yield(WaitForSeconds(0.05))
    custom.Counter = 2
    coroutine.yield(WaitUntil(function() return custom.Ready end))
    custom.Counter = 3
end
""");

        LuaScriptManager.Initialize(_projectRoot, typeof(LuaTestUserComponent).Assembly);
        World world = WorldManager.CreateWorld("Coroutines");
        WorldManager.SetActiveWorld(world);
        Entity entity = world.CreateEntity("Runner");
        LuaTestUserComponent custom = entity.AddComponent<LuaTestUserComponent>();
        LuaScriptComponent lua = entity.AddComponent<LuaScriptComponent>();
        lua.ScriptPath = AssetPath(scriptPath);

        Assert.True(lua.HasLoadedState);
        Assert.True(lua.HasStartFunction);

        lua._startDelegate?.Invoke();
        lua.UpdateCoroutines(0f);
        Assert.True(_logs.All(static log => !log.Contains("[Lua] Runtime error", StringComparison.Ordinal)), string.Join(Environment.NewLine, _logs));
        Assert.Equal(1, custom.Counter);

        lua.UpdateCoroutines(0.03f);
        Assert.Equal(1, custom.Counter);

        lua.UpdateCoroutines(0.03f);
        Assert.Equal(2, custom.Counter);

        custom.Ready = true;
        lua.UpdateCoroutines(0.01f);
        Assert.Equal(3, custom.Counter);
    }

    [Fact]
    public void LuaHotReload_RaisesEditorStyleEventOutsidePlayMode()
    {
        string scriptPath = WriteLuaScript("reload.lua", "return 1");
        LuaScriptManager.Initialize(_projectRoot, typeof(LuaTestUserComponent).Assembly);

        List<string>? changed = null;
        void Handler(IReadOnlyList<string> paths) => changed = paths.ToList();

        LuaScriptManager.HotReloadRequested += Handler;
        try
        {
            LuaScriptManager.NotifyScriptChangedForTesting(scriptPath);
        }
        finally
        {
            LuaScriptManager.HotReloadRequested -= Handler;
        }

        Assert.NotNull(changed);
        Assert.Single(changed!);
        Assert.Equal(Path.GetFullPath(scriptPath), changed![0]);
    }

    [Fact]
    public void LuaHotReload_RefreshesActiveComponentStateImmediately()
    {
        string scriptPath = WriteLuaScript("live_reload.lua", """
function Start()
    local custom = self.Owner:GetComponent("LuaTestUserComponent")
    custom.Counter = 1
end
""");

        LuaScriptManager.Initialize(_projectRoot, typeof(LuaTestUserComponent).Assembly);
        World world = WorldManager.CreateWorld("HotReload");
        WorldManager.SetActiveWorld(world);
        Entity entity = world.CreateEntity("Player");
        LuaTestUserComponent custom = entity.AddComponent<LuaTestUserComponent>();
        LuaScriptComponent lua = entity.AddComponent<LuaScriptComponent>();
        lua.ScriptPath = AssetPath(scriptPath);

        lua._startDelegate?.Invoke();
        lua.UpdateCoroutines(0f);
        Assert.Equal(1, custom.Counter);

        WriteLuaScript("live_reload.lua", """
function Start()
    local custom = self.Owner:GetComponent("LuaTestUserComponent")
    custom.Counter = 5
end
""");

        LuaScriptManager.NotifyScriptChangedForTesting(scriptPath);

        lua._startDelegate?.Invoke();
        lua.UpdateCoroutines(0f);
        Assert.Equal(5, custom.Counter);
    }

    [Fact]
    public void LuaBindings_ReadKeyboardInputState()
    {
        string scriptPath = WriteLuaScript("input.lua", """
function Start()
    local custom = self.Owner:GetComponent("LuaTestUserComponent")
    custom.SpacePressed = Input.IsKeyPressed(Keys.Space)
    custom.SpaceDown = Input.IsKeyDown(Keys.Space)
end
""");

        LuaScriptManager.Initialize(_projectRoot, typeof(LuaTestUserComponent).Assembly);
        World world = WorldManager.CreateWorld("Input");
        WorldManager.SetActiveWorld(world);
        Entity entity = world.CreateEntity("Player");
        LuaTestUserComponent custom = entity.AddComponent<LuaTestUserComponent>();
        LuaScriptComponent lua = entity.AddComponent<LuaScriptComponent>();
        lua.ScriptPath = AssetPath(scriptPath);

        Verity.Input.Input.Reset();
        Verity.Input.Input.Enabled = true;
        Verity.Input.Input.ProcessEvent(InputEvent.KeyDown(KeyCode.Space));
        Verity.Input.Input.NewLogicTick();

        lua._startDelegate?.Invoke();
        lua.UpdateCoroutines(0f);

        Assert.True(custom.SpacePressed);
        Assert.True(custom.SpaceDown);
    }

    [Fact]
    public void LuaBindings_ReadKeyboardInputState_WithMethodSyntax()
    {
        string scriptPath = WriteLuaScript("input_method.lua", """
function Start()
    local custom = self.Owner:GetComponent("LuaTestUserComponent")
    custom.SpacePressed = Input:IsKeyPressed(Keys.Space)
    custom.SpaceDown = Input:IsKeyDown(Keys.Space)
end
""");

        LuaScriptManager.Initialize(_projectRoot, typeof(LuaTestUserComponent).Assembly);
        World world = WorldManager.CreateWorld("InputMethod");
        WorldManager.SetActiveWorld(world);
        Entity entity = world.CreateEntity("Player");
        LuaTestUserComponent custom = entity.AddComponent<LuaTestUserComponent>();
        LuaScriptComponent lua = entity.AddComponent<LuaScriptComponent>();
        lua.ScriptPath = AssetPath(scriptPath);

        Verity.Input.Input.Reset();
        Verity.Input.Input.Enabled = true;
        Verity.Input.Input.ProcessEvent(InputEvent.KeyDown(KeyCode.Space));
        Verity.Input.Input.NewLogicTick();

        lua._startDelegate?.Invoke();
        lua.UpdateCoroutines(0f);

        Assert.True(custom.SpacePressed);
        Assert.True(custom.SpaceDown);
    }

    [Fact]
    public void LuaBindings_ExposeColorConstructorAndComponentColorFields()
    {
        string scriptPath = WriteLuaScript("color.lua", """
function Start()
    local custom = self.Owner:GetComponent("LuaTestUserComponent")
    custom.Tint = Color(0.25, 0.5, 0.75, 1.0)
    local tint = custom.Tint
    custom.LastColorR = tint.R
    custom.LastColorG = tint.G
    custom.LastColorB = tint.B
    custom.LastColorA = tint.A
end
""");

        LuaScriptManager.Initialize(_projectRoot, typeof(LuaTestUserComponent).Assembly);
        World world = WorldManager.CreateWorld("Color");
        WorldManager.SetActiveWorld(world);
        Entity entity = world.CreateEntity("Player");
        LuaTestUserComponent custom = entity.AddComponent<LuaTestUserComponent>();
        LuaScriptComponent lua = entity.AddComponent<LuaScriptComponent>();
        lua.ScriptPath = AssetPath(scriptPath);

        lua._startDelegate?.Invoke();
        lua.UpdateCoroutines(0f);

        Assert.Equal(0.25f, custom.Tint.R);
        Assert.Equal(0.5f, custom.Tint.G);
        Assert.Equal(0.75f, custom.Tint.B);
        Assert.Equal(1f, custom.Tint.A);
        Assert.Equal(0.25f, custom.LastColorR);
        Assert.Equal(0.5f, custom.LastColorG);
        Assert.Equal(0.75f, custom.LastColorB);
        Assert.Equal(1f, custom.LastColorA);
    }

    public void Dispose()
    {
        Debug.OnLog -= OnLog;
        LuaScriptManager.Dispose();
        LuaScriptManager.SuspendHotReloadEvents = false;
        Verity.Input.Input.Enabled = true;
        Verity.Input.Input.Reset();
        WorldManager.Reset();

        if (Directory.Exists(_projectRoot))
            Directory.Delete(_projectRoot, recursive: true);
    }

    private string WriteLuaScript(string fileName, string contents)
    {
        string fullPath = Path.Combine(_assetsPath, fileName);
        File.WriteAllText(fullPath, contents);
        return fullPath;
    }

    private static string AssetPath(string fullPath) => $"Assets/{Path.GetFileName(fullPath)}";

    private void OnLog(string message, LogLevel level)
    {
        _logs.Add($"{level}:{message}");
    }
}

public sealed class LuaTestUserComponent : Component
{
    public int Counter { get; set; }
    public bool Ready { get; set; }
    public float LastVectorZ { get; set; }
    public float LastDelta { get; set; }
    public bool SpaceDown { get; set; }
    public bool SpacePressed { get; set; }
    public Color Tint { get; set; }
    public float LastColorR { get; set; }
    public float LastColorG { get; set; }
    public float LastColorB { get; set; }
    public float LastColorA { get; set; }

    public void Increment(int value)
    {
        Counter += value;
    }
}
