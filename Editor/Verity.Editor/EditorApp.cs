using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Numerics;
using Hexa.NET.ImGui;
using CoreDebug = Verity.Core.Debug;
using Verity.Core.Engine;
using Verity.Core.World;
using Verity.Editor.Windows;
using Verity.Graphics;

namespace Verity.Editor;

using ImGuiP = Hexa.NET.ImGui.ImGui; 

public class EditorApp : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly ImGuiController _imgui;
    private readonly Shader2D _shader;
    private readonly TextureManager _textureManager;
    private readonly RenderPipeline _renderPipeline;
    private readonly Camera _worldCamera;
    private readonly List<EditorWindow> _windows = [];
    private readonly Stopwatch _stopwatch = new();
    private GameLoop? _gameLoop;
    private WorldSnapshot? _snapshot;
    private ScriptCompiler? _scriptCompiler;

    public bool IsPlaying { get; private set; }
    public bool IsBuilding { get; set; }
    public string BuildStatus { get; set; } = "";

    public string? CurrentProjectName { get; private set; }
    public string ProjectsRoot { get; }
    public string? ProjectPath => CurrentProjectName != null ? Path.Combine(ProjectsRoot, CurrentProjectName) : null;
    public string? AssetsPath => ProjectPath != null ? Path.Combine(ProjectPath, "Assets") : null;

    public GraphicsDevice Device => _device;
    public Shader2D Shader => _shader;
    public TextureManager TextureManager => _textureManager;
    public RenderPipeline RenderPipeline => _renderPipeline;
    public Camera WorldCamera => _worldCamera;
    public ScriptCompiler? ScriptCompiler => _scriptCompiler;

    public EditorApp(string title = "Verity Editor", int width = 1280, int height = 720)
    {
        CoreDebug.OnLog += (msg, level) => ConsoleWindow.Log(msg, level);
        string docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        ProjectsRoot = Path.Combine(docsPath, "VerityProjects");
        Directory.CreateDirectory(ProjectsRoot);

        _device = GraphicsDevice.Create(title, width, height);
        _imgui = new ImGuiController();
        _imgui.Initialize(_device);
        _shader = Shader2D.Create(_device);
        _textureManager = new TextureManager(_device);
        _renderPipeline = new RenderPipeline(_device, _shader, _textureManager);
        _renderPipeline.SetWhitePixel(_textureManager.CreateWhitePixel());
        DefaultSprites.Initialize(_textureManager);
        _worldCamera = new Camera();
        _worldCamera.SetViewportSize(width, height);
        _device.Window.OnSdlEvent += Verity.Input.Input.ProcessEvent;
    }

    public void OpenProject(string projectName)
    {
        CurrentProjectName = projectName;
        Directory.CreateDirectory(ProjectPath!);
        Directory.CreateDirectory(AssetsPath!);
        
        _renderPipeline.BaseAssetsPath = ProjectPath;

        _scriptCompiler?.Dispose();
        _scriptCompiler = new ScriptCompiler(AssetsPath!);
        _scriptCompiler.OnCompilationFinished += OnScriptsCompiled;
        _scriptCompiler.Compile();
        var worldFiles = Directory.GetFiles(AssetsPath!, "*.verity", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTime).ToList();
        if (worldFiles.Count > 0) GetWindow<ProjectWindow>()?.LoadWorldByPath(worldFiles[0].FullName);
        else {
            var world = WorldManager.CreateWorld("Untitled");
            var cam = world.CreateEntity("Main Camera"); cam.AddComponent<Camera>();
        }
    }

    public void AddWindow(EditorWindow window) => _windows.Add(window);
    public T? GetWindow<T>() where T : EditorWindow => _windows.OfType<T>().FirstOrDefault();

    private void OnScriptsCompiled()
    {
        var world = WorldManager.ActiveWorld;
        if (world == null || IsPlaying) return;
        var json = Verity.Core.Serialization.SceneSerializer.Serialize(world);
        world.ClearAllEntities();
        Verity.Core.Serialization.SceneSerializer.Deserialize(world, json, _scriptCompiler?.CompiledAssembly);
        EditorSelection.SelectedEntity = null;
    }

    public void EnterPlayMode()
    {
        if (WorldManager.ActiveWorld == null || IsPlaying) return;
        _snapshot = WorldSnapshot.Capture(WorldManager.ActiveWorld);
        Time.Reset(); _gameLoop = new GameLoop(); IsPlaying = true;
    }

    public void ExitPlayMode()
    {
        if (!IsPlaying || WorldManager.ActiveWorld == null) return;
        EditorSelection.SelectedEntity = null;
        _snapshot?.Restore(WorldManager.ActiveWorld);
        _snapshot = null; _gameLoop = null; IsPlaying = false;
    }

    public void Run()
    {
        _stopwatch.Start();
        long lastTicks = _stopwatch.ElapsedTicks;
        while (!_device.ShouldClose)
        {
            long currentTicks = _stopwatch.ElapsedTicks;
            float deltaTime = (float)(currentTicks - lastTicks) / Stopwatch.Frequency;
            lastTicks = currentTicks;
            if (!IsPlaying) { Time.DeltaTime = deltaTime; Time.TotalTime += deltaTime; Time.FrameCount++; }
            Verity.Input.Input.BeginFrame(); _device.PollEvents(); Verity.Input.Input.EndFrame();
            if (IsPlaying && _gameLoop != null) _gameLoop.TickLogic(deltaTime);
            _device.Gl.Viewport(0, 0, _device.Window.GetWidth(), _device.Window.GetHeight());
            _device.Clear(Color.FromArgb(255, 30, 30, 30));
            _imgui.BeginFrame();
            if (CurrentProjectName == null) DrawLauncher();
            else {
                SetupDockSpace();
                foreach (var window in _windows) {
                    if (!window.IsOpen) continue;
                    bool open = window.IsOpen;
                    if (ImGui.Begin(window.Title, ref open)) window.OnGui();
                    ImGui.End(); window.IsOpen = open;
                }
            }
            
            // Build Overlay must be drawn AFTER all other windows to be on top
            if (IsBuilding) DrawBuildOverlay();

            _imgui.EndFrame();
            CoreDebug.ClearDrawCommands();
            _device.SwapBuffers();
        }
    }

    private void DrawBuildOverlay()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.SetNextWindowBgAlpha(0.6f); // Dim background
        
        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus;
        if (ImGui.Begin("BuildOverlay", flags))
        {
            var center = viewport.Pos + viewport.Size * 0.5f;
            string text = "BUILDING PROJECT...";
            string subText = BuildStatus;
            
            var textSize = ImGui.CalcTextSize(text);
            var subSize = ImGui.CalcTextSize(subText);
            
            var dl = ImGui.GetWindowDrawList();
            dl.AddText(center - new Vector2(textSize.X * 0.5f, 20), ImGui.GetColorU32(new Vector4(1, 1, 0, 1)), text);
            dl.AddText(center - new Vector2(subSize.X * 0.5f, -10), ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), subText);
            
            ImGui.End();
        }
    }

    private string _newProjectName = "";
    private void DrawLauncher()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos); ImGui.SetNextWindowSize(viewport.Size);
        ImGui.Begin("Launcher", ImGuiWindowFlags.NoDecoration);
        ImGui.TextDisabled("Verity Engine v1.0"); ImGui.Separator();
        ImGui.Columns(2); ImGui.Text("Recent Projects"); ImGui.Separator();
        foreach (var dir in Directory.GetDirectories(ProjectsRoot)) {
            var name = Path.GetFileName(dir);
            if (ImGui.Selectable(name, false, ImGuiSelectableFlags.AllowDoubleClick) && ImGui.IsMouseDoubleClicked(0)) OpenProject(name);
        }
        ImGui.NextColumn(); ImGui.Text("New Project"); ImGui.Separator();
        ImGui.InputText("Name", ref _newProjectName, 64);
        if (ImGui.Button("Create") && !string.IsNullOrWhiteSpace(_newProjectName)) OpenProject(_newProjectName);
        ImGui.End();
    }

    private void SetupDockSpace()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos); ImGui.SetNextWindowSize(viewport.Size);
        var flags = ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f); ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f); ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new System.Numerics.Vector2(0, 0));
        ImGui.Begin("DockSpace", flags); ImGui.PopStyleVar(3);

        if (ImGui.BeginMenuBar()) {
            var assetWindow = GetWindow<ProjectWindow>();
            if (ImGui.BeginMenu("File")) {
                if (ImGui.MenuItem("New World")) assetWindow?.CreateWorldInProject();
                if (ImGui.BeginMenu("Open World")) {
                    if (AssetsPath != null && Directory.Exists(AssetsPath)) {
                        foreach (var f in Directory.GetFiles(AssetsPath, "*.verity", SearchOption.AllDirectories))
                            if (ImGui.MenuItem(Path.GetRelativePath(AssetsPath, f))) assetWindow?.LoadWorldByPath(f);
                    }
                    ImGui.EndMenu();
                }
                if (ImGui.MenuItem("Save World")) assetWindow?.SaveActiveWorldAsAsset();
                ImGui.Separator();
                if (ImGui.MenuItem("Close Project")) CurrentProjectName = null;
                if (ImGui.MenuItem("Exit")) _device.Window.Close();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Window")) {
                foreach (var win in _windows) if (ImGui.MenuItem(win.Title, "", win.IsOpen)) win.IsOpen = !win.IsOpen;
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Build")) {
                if (ImGui.MenuItem("Build Settings")) GetWindow<BuildSettingsWindow>()!.IsOpen = true;
                ImGui.Separator();
                if (ImGui.MenuItem("Build & Run")) assetWindow?.BuildAndRun();
                if (ImGui.MenuItem("Publish (Single EXE)")) assetWindow?.PublishSingleFile();
                ImGui.EndMenu();
            }
            float mid = ImGui.GetWindowWidth() * 0.5f; ImGui.SetCursorPosX(mid - 30);
            if (IsPlaying) { if (ImGui.Button("Stop", new System.Numerics.Vector2(60, 0))) ExitPlayMode(); }
            else { if (ImGui.Button("Play", new System.Numerics.Vector2(60, 0))) EnterPlayMode(); }
            ImGui.EndMenuBar();
        }
        ImGui.DockSpace(ImGui.GetID("VerityDockSpace"));
        ImGui.End();
    }

    public void Dispose() { _scriptCompiler?.Dispose(); _renderPipeline.Dispose(); _shader.Dispose(); _textureManager.Dispose(); _imgui.Dispose(); _device.Dispose(); }
}
