using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Numerics;
using Hexa.NET.ImGui;
using Irodori.Backend.OpenGL;
using CoreDebug = Verity.Core.Debug;
using Verity.Core.ECS;
using Verity.Core.Engine;
using Verity.Core.World;
using Verity.Editor.Windows;
using Verity.Graphics;

namespace Verity.Editor;

using ImGuiP = Hexa.NET.ImGui.ImGui; 

public class EditorGlobalSettings
{
    public string ProjectsRoot { get; set; } = "";
}

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
    private readonly UndoSystem _undoSystem = new();

    private readonly List<(string text, float duration)> _overlayMessages = new();

    public ProjectSettings ProjectSettings { get; private set; } = new();
    public BuildSettings BuildSettings { get; private set; } = new();

    public bool IsPlaying { get; private set; }
    public bool IsBuilding { get; set; }
    public string BuildStatus { get; set; } = "";

    public string? CurrentProjectName { get; private set; }
    public string ProjectsRoot { get; private set; }
    public string? ProjectPath => CurrentProjectName != null ? Path.Combine(ProjectsRoot, CurrentProjectName) : null;
    public string? AssetsPath => ProjectPath != null ? Path.Combine(ProjectPath, "Assets") : null;

    public string EditorLogoPath => Path.Combine(AppContext.BaseDirectory, "EditorResources", "EditorLogo.png");
    private string GlobalSettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VerityProjects", "GlobalSettings.json");

    public GraphicsDevice Device => _device;
    public Shader2D Shader => _shader;
    public TextureManager TextureManager => _textureManager;
    public RenderPipeline RenderPipeline => _renderPipeline;
    public Camera WorldCamera => _worldCamera;
    public ScriptCompiler? ScriptCompiler => _scriptCompiler;

    private bool _isScreenFocused;
    private string _targetPathChangeBuffer = "";
    private string _newProjectName = "";

    private Vector2 _targetCameraPosition;
    private float _targetCameraZoom;
    private bool _isFocusInterpolating;

    public EditorApp(string title = "Verity", int width = 900, int height = 600)
    {
        CoreDebug.OnLog += (msg, level) => ConsoleWindow.Log(msg, level);
        
        // Initialize default ProjectsRoot
        string docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        ProjectsRoot = Path.Combine(docsPath, "VerityProjects");
        
        LoadGlobalSettings();
        Directory.CreateDirectory(ProjectsRoot);

        _device = GraphicsDevice.Create(title, width, height);
        _imgui = new ImGuiController();
        
        string? fontPath = FindKoreanFont();
        _imgui.Initialize(_device, fontPath, ProjectSettings.EditorFontSize);
        
        _shader = Shader2D.Create(_device);
        _textureManager = new TextureManager(_device);
        _renderPipeline = new RenderPipeline(_device, _shader, _textureManager);
        _renderPipeline.SetWhitePixel(_textureManager.CreateWhitePixel());
        DefaultSprites.Initialize(_textureManager);
        _worldCamera = new Camera();
        _worldCamera.SetViewportSize(width, height);
        _device.Window.OnSdlEvent += Verity.Input.Input.ProcessEvent;

        ApplyEditorIcon();
    }

    public void ShowOverlayMessage(string text, float duration = 2.0f)
    {
        _overlayMessages.Add((text, duration));
    }

    private void ApplyEditorIcon()
    {
        if (File.Exists(EditorLogoPath))
        {
            try {
                var raw = _textureManager.GetRawPixels(EditorLogoPath);
                _device.SetWindowIcon(raw.Pixels, raw.Width, raw.Height);
            } catch { }
        }
    }

    private void LoadGlobalSettings()
    {
        try { 
            if (File.Exists(GlobalSettingsPath)) 
            { 
                var json = File.ReadAllText(GlobalSettingsPath);
                var settings = System.Text.Json.JsonSerializer.Deserialize<EditorGlobalSettings>(json);
                if (settings != null && !string.IsNullOrWhiteSpace(settings.ProjectsRoot))
                {
                    ProjectsRoot = settings.ProjectsRoot;
                }
            } 
        } 
        catch (Exception e) 
        {
            CoreDebug.LogError($"[Launcher] Failed to load global settings: {e.Message}");
        }
    }

    private void SaveGlobalSettings() 
    {
        try
        {
            var dir = Path.GetDirectoryName(GlobalSettingsPath);
            if (dir != null) Directory.CreateDirectory(dir);
            
            var settings = new EditorGlobalSettings { ProjectsRoot = ProjectsRoot };
            var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(GlobalSettingsPath, json);
        }
        catch (Exception e)
        {
            CoreDebug.LogError($"[Launcher] Failed to save global settings: {e.Message}");
        }
    }

    private string? FindKoreanFont()
    {
        string appDir = AppContext.BaseDirectory;
        string globalFontsDir = Path.Combine(appDir, "EditorResources", "Fonts");
        Directory.CreateDirectory(globalFontsDir);
        var files = Directory.GetFiles(globalFontsDir, "*.ttf");
        return files.Length > 0 ? files[0] : null;
    }

    private FileStream? _projectLock;

    public bool OpenProject(string projectName)
    {
        CurrentProjectName = projectName;
        string projectPath = ProjectPath!;
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(AssetsPath!);

        // Try to lock the project
        string lockPath = Path.Combine(projectPath, ".lock");
        try
        {
            // FileShare.None is the key: OS will release this lock if process dies
            _projectLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            
            // Write current PID to the lock file for diagnostic info
            using (var writer = new StreamWriter(_projectLock, leaveOpen: true))
            {
                _projectLock.SetLength(0); // Clear existing content
                writer.WriteLine(Process.GetCurrentProcess().Id);
                writer.Flush();
            }
        }
        catch (IOException)
        {
            CoreDebug.LogError($"[Project] Project '{projectName}' is already open in another instance.");
            CurrentProjectName = null;
            return false;
        }
        catch (Exception e)
        {
            CoreDebug.LogError($"[Project] Failed to acquire project lock: {e.Message}");
            CurrentProjectName = null;
            return false;
        }

        // Resize window and change title for Editor
        _device.SetSize(1600, 900);
        _device.SetWindowTitle("Verity Editor");
        _worldCamera.SetViewportSize(1600, 900);

        Verity.Input.FilterManager.SavePath = Path.Combine(AssetsPath!, "Filters.json");
        Verity.Input.FilterManager.Load();
        LoadProjectSettings();
        LoadBuildSettings();
        _renderPipeline.BaseAssetsPath = ProjectPath;
        _scriptCompiler?.Dispose();
        _scriptCompiler = new ScriptCompiler(AssetsPath!);
        _scriptCompiler.OnCompilationFinished += OnScriptsCompiled;
        _scriptCompiler.Compile();
        var worldFiles = Directory.GetFiles(AssetsPath!, "*.verity", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTime).ToList();
        
        if (worldFiles.Count > 0) 
        {
            GetWindow<ProjectWindow>()?.LoadWorldByPath(worldFiles[0].FullName);
        }
        else 
        {
            var world = WorldManager.CreateOrReplaceWorld("Main");
            var cam = world.CreateEntity("Main Camera");
            cam.AddComponent<Camera>();
            WorldManager.SetActiveWorld(world);
            string mainWorldPath = Path.Combine(AssetsPath!, "Main.verity");
            try { File.WriteAllText(mainWorldPath, Verity.Core.Serialization.SceneSerializer.Serialize(world)); } catch { }
        }
        return true;
    }

    public void LaunchProjectInstance(string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName)) return;
        if (CurrentProjectName == projectName) return;

        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--project \"{projectName}\"",
                UseShellExecute = false
            };
            Process.Start(startInfo);
        }
        catch (Exception e)
        {
            CoreDebug.LogError($"[Launcher] Failed to launch project instance: {e.Message}");
        }
    }

    public void CloseProject()
    {
        _projectLock?.Dispose();
        _projectLock = null;
        _device.Window.Close();
    }

    private void LoadProjectSettings()
    {
        if (AssetsPath == null) return;
        string path = Path.Combine(AssetsPath, "ProjectSettings.json");
        if (File.Exists(path)) {
            try { var json = File.ReadAllText(path); ProjectSettings = System.Text.Json.JsonSerializer.Deserialize<ProjectSettings>(json) ?? new(); }
            catch { ProjectSettings = new(); }
        } else { ProjectSettings = new(); SaveProjectSettings(); }
    }

    public void SaveProjectSettings()
    {
        if (AssetsPath == null) return;
        string path = Path.Combine(AssetsPath, "ProjectSettings.json");
        var json = System.Text.Json.JsonSerializer.Serialize(ProjectSettings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public void FocusEntity(Entity entity)
    {
        if (entity == null) return;
        _targetCameraPosition = entity.Transform.WorldPosition;
        float s = MathF.Max(MathF.Abs(entity.Transform.WorldScale.X), MathF.Abs(entity.Transform.WorldScale.Y));
        if (s > 0) _targetCameraZoom = Math.Clamp(s / _worldCamera.OrthographicSize, 0.01f, 20.0f);
        else _targetCameraZoom = _worldCamera.Zoom;
        _isFocusInterpolating = true;
    }

    public void StopFocusInterpolation() => _isFocusInterpolating = false;

    private void LoadBuildSettings()
    {
        if (AssetsPath == null) return;
        string path = Path.Combine(AssetsPath, "BuildSettings.json");
        if (File.Exists(path)) BuildSettings = BuildSettings.Load(path);
        else { BuildSettings = new BuildSettings(); SaveBuildSettings(); }
    }

    public void SaveBuildSettings()
    {
        if (AssetsPath == null) return;
        string path = Path.Combine(AssetsPath, "BuildSettings.json");
        BuildSettings.Save(path);
    }

    public void RecordUndo() { var world = WorldManager.ActiveWorld; if (world != null) _undoSystem.Record(world); }
    public void BeginUndoAction() { var world = WorldManager.ActiveWorld; if (world != null) _undoSystem.BeginContinuousAction(world); }
    public void EndUndoAction() { var world = WorldManager.ActiveWorld; if (world != null) _undoSystem.EndContinuousAction(world); }
    public void Undo() { var world = WorldManager.ActiveWorld; if (world == null) return; Guid? selId = EditorSelection.SelectedEntity?.Id; _undoSystem.Undo(world); if (selId.HasValue) EditorSelection.SelectedEntity = world.GetAllEntities().FirstOrDefault(e => e.Id == selId.Value); }
    public void Redo() { var world = WorldManager.ActiveWorld; if (world == null) return; Guid? selId = EditorSelection.SelectedEntity?.Id; _undoSystem.Redo(world); if (selId.HasValue) EditorSelection.SelectedEntity = world.GetAllEntities().FirstOrDefault(e => e.Id == selId.Value); }

    public void AddWindow(EditorWindow window) => _windows.Add(window);
    public T? GetWindow<T>() where T : EditorWindow => _windows.OfType<T>().FirstOrDefault();

    private void OnScriptsCompiled() { var world = WorldManager.ActiveWorld; if (world == null || IsPlaying) return; var json = Verity.Core.Serialization.SceneSerializer.Serialize(world); world.ClearAllEntities(); Verity.Core.Serialization.SceneSerializer.Deserialize(world, json, _scriptCompiler?.CompiledAssembly); EditorSelection.SelectedEntity = null; }

    public void EnterPlayMode() { if (WorldManager.ActiveWorld == null || IsPlaying) return; _snapshot = WorldSnapshot.Capture(WorldManager.ActiveWorld); Time.Reset(); _gameLoop = new GameLoop { ProjectSettings = ProjectSettings }; IsPlaying = true; }
    public void ExitPlayMode() { if (!IsPlaying || WorldManager.ActiveWorld == null) return; EditorSelection.SelectedEntity = null; _snapshot?.Restore(WorldManager.ActiveWorld); _snapshot = null; _gameLoop = null; IsPlaying = false; Verity.Input.Input.Enabled = true; }

    public void Run()
    {
        _stopwatch.Start();
        long lastTicks = _stopwatch.ElapsedTicks;
        while (!_device.ShouldClose)
        {
            long currentTicks = _stopwatch.ElapsedTicks;
            float deltaTime = (float)(currentTicks - lastTicks) / Stopwatch.Frequency;
            lastTicks = currentTicks;
            Time.FrameCount++;
            if (!IsPlaying) { Time.DeltaTime = deltaTime; Time.TotalTime += deltaTime; }
            
            Verity.Input.Input.Enabled = _isScreenFocused;
            _device.PollEvents();
            
            if (IsPlaying && _gameLoop != null) _gameLoop.TickLogic(deltaTime);
            HandleGlobalShortcuts();

            if (_isFocusInterpolating)
            {
                float t = Math.Min(1.0f, deltaTime * 8.0f);
                _worldCamera.Position = Vector2.Lerp(_worldCamera.Position, _targetCameraPosition, t);
                _worldCamera.Zoom = _worldCamera.Zoom + (_targetCameraZoom - _worldCamera.Zoom) * t;
                if (Vector2.DistanceSquared(_worldCamera.Position, _targetCameraPosition) < 0.000001f && MathF.Abs(_worldCamera.Zoom - _targetCameraZoom) < 0.0001f)
                {
                    _worldCamera.Position = _targetCameraPosition;
                    _worldCamera.Zoom = _targetCameraZoom;
                    _isFocusInterpolating = false;
                }
            }
            
            _device.Gl.Viewport(0, 0, _device.Window.GetWidth(), _device.Window.GetHeight());
            _device.Clear(Color.FromArgb(255, 30, 30, 30));
            
            _imgui.BeginFrame();
            if (CurrentProjectName == null) DrawLauncher();
            else {
                SetupDockSpace();
                _isScreenFocused = false;
                foreach (var window in _windows) {
                    if (!window.IsOpen) continue;
                    bool open = window.IsOpen;
                    
                    // Stop play mode if any editor window is moving or resizing
                    if (IsPlaying && ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) && ImGui.IsMouseDragging(0)) {
                        // Debounce or refine? For now, any structural change stops play.
                    }

                    if (ImGui.Begin(window.Title, ref open)) {
                        if (window is ScreenWindow && ImGui.IsWindowFocused()) _isScreenFocused = true;
                        window.OnGui();
                    }
                    ImGui.End(); window.IsOpen = open;
                }
            }
            
            DrawOverlays(deltaTime);
            _imgui.EndFrame();
            
            CoreDebug.ClearDrawCommands();
            _device.SwapBuffers();
        }
    }

    private void DrawOverlays(float dt)
    {
        var viewport = ImGui.GetMainViewport();
        
        // --- 1. Build Overlay ---
        if (IsBuilding)
        {
            ImGui.SetNextWindowPos(viewport.Pos);
            ImGui.SetNextWindowSize(viewport.Size);
            ImGui.SetNextWindowBgAlpha(0.7f);
            var bFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoFocusOnAppearing;
            if (ImGui.Begin("##BuildOverlay", bFlags))
            {
                var center = viewport.Pos + viewport.Size * 0.5f;
                string t1 = "BUILDING PROJECT..."; string t2 = BuildStatus;
                var s1 = ImGui.CalcTextSize(t1); var s2 = ImGui.CalcTextSize(t2);
                var dl = ImGui.GetWindowDrawList();
                dl.AddText(center - new Vector2(s1.X * 0.5f, 20), ImGui.GetColorU32(new Vector4(1, 1, 0, 1)), t1);
                dl.AddText(center - new Vector2(s2.X * 0.5f, -10), ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), t2);
                ImGui.End();
            }
        }

        // --- 2. Message Overlays ---
        if (_overlayMessages.Count > 0)
        {
            ImGui.SetNextWindowPos(viewport.Pos + new Vector2(20, viewport.Size.Y - 60));
            ImGui.SetNextWindowBgAlpha(0.85f);
            var mFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus;
            if (ImGui.Begin("##MessageOverlay", mFlags))
            {
                for (int i = _overlayMessages.Count - 1; i >= 0; i--)
                {
                    var msg = _overlayMessages[i];
                    ImGui.TextColored(new Vector4(1, 0.8f, 0.2f, 1), $"[Verity] {msg.text}");
                    float newDur = msg.duration - dt;
                    if (newDur <= 0) _overlayMessages.RemoveAt(i);
                    else _overlayMessages[i] = (msg.text, newDur);
                }
                ImGui.End();
            }
        }
    }

    private unsafe void DrawLauncher()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
        ImGui.Begin("Launcher", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus);
        ImGui.PopStyleVar();

        var drawList = ImGui.GetWindowDrawList();
        var winSize = ImGui.GetWindowSize();
        
        // Gradient Background
        drawList.AddRectFilledMultiColor(viewport.Pos, viewport.Pos + winSize, 
            ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.16f, 1.0f)),
            ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.16f, 1.0f)),
            ImGui.GetColorU32(new Vector4(0.06f, 0.06f, 0.08f, 1.0f)),
            ImGui.GetColorU32(new Vector4(0.06f, 0.06f, 0.08f, 1.0f)));

        // Header / Logo
        ImGui.SetCursorPosY(50);
        if (File.Exists(EditorLogoPath)) {
            var tex = _textureManager.Load(EditorLogoPath);
            if (tex is OpenGlTexture glTex) {
                float aspect = (float)glTex.Width / glTex.Height;
                float drawH = 100; float drawW = drawH * aspect;
                ImGui.SetCursorPosX((winSize.X - drawW) * 0.5f);
                ImTextureID texID = new((nint)glTex.Id);
                var texRef = new ImTextureRef(null, texID);
                ImGui.Image(texRef, new Vector2(drawW, drawH), new Vector2(0, 1), new Vector2(1, 0));
            }
        } else {
            ImGui.SetCursorPosX((winSize.X - 400) * 0.5f);
            ImGui.TextColored(new Vector4(0.3f, 0.7f, 1.0f, 1.0f), "V E R I T Y   E N G I N E");
        }

        ImGui.SetCursorPosY(170);
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 20));
        
        float contentW = winSize.X * 0.9f;
        ImGui.SetCursorPosX((winSize.X - contentW) * 0.5f);
        
        if (ImGui.BeginChild("LauncherContent", new Vector2(contentW, winSize.Y - 240), (ImGuiChildFlags)0, ImGuiWindowFlags.NoBackground)) {
            ImGui.Columns(2, "LauncherColumns", false);
            ImGui.SetColumnWidth(0, contentW * 0.65f);

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
            ImGui.TextUnformatted("RECENT PROJECTS");
            ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(0, 10));
            
            if (Directory.Exists(ProjectsRoot)) {
                if (ImGui.BeginChild("ProjectList", new Vector2(-10, -1), (ImGuiChildFlags)0, ImGuiWindowFlags.NoBackground))
                {
                    var projectInfos = Directory.GetDirectories(ProjectsRoot)
                        .Select(d => {
                            var di = new DirectoryInfo(d);
                            var assetsDir = Path.Combine(d, "Assets");
                            DateTime lastMod = di.LastWriteTime;
                            if (Directory.Exists(assetsDir))
                            {
                                var files = Directory.GetFiles(assetsDir, "*", SearchOption.AllDirectories);
                                if (files.Length > 0)
                                {
                                    var latestFileMod = files.Select(f => File.GetLastWriteTime(f)).Max();
                                    if (latestFileMod > lastMod) lastMod = latestFileMod;
                                }
                            }
                            return new { Name = di.Name, FullPath = di.FullName, LastModified = lastMod };
                        })
                        .OrderByDescending(p => p.LastModified)
                        .ToList();

                    foreach (var proj in projectInfos) {
                        var name = proj.Name;
                        var fullPath = proj.FullPath;
                        var lastModified = proj.LastModified.ToString("yyyy-MM-dd HH:mm:ss");

                        ImGui.PushID(fullPath);
                        
                        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f);
                        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(1, 1, 1, 0.03f));
                        
                        if (ImGui.BeginChild("Card", new Vector2(-1, 80), (ImGuiChildFlags)1, ImGuiWindowFlags.NoScrollbar)) {
                            bool hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows);
                            if (hovered) {
                                drawList.AddRectFilled(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize(), ImGui.GetColorU32(new Vector4(1, 1, 1, 0.05f)), 8f);
                                if (ImGui.IsMouseClicked(0)) LaunchProjectInstance(name);
                                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                            }

                            ImGui.SetCursorPos(new Vector2(20, 12));
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f));
                            ImGui.TextUnformatted(name);
                            ImGui.PopStyleColor();

                            ImGui.SetCursorPos(new Vector2(20, 34));
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
                            ImGui.TextUnformatted(fullPath);
                            ImGui.PopStyleColor();

                            ImGui.SetCursorPos(new Vector2(20, 54));
                            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 0.6f, 0.3f, 1.0f));
                            ImGui.TextUnformatted($"Last Modified: {lastModified}");
                            ImGui.PopStyleColor();
                            
                            ImGui.SetCursorPos(new Vector2(ImGui.GetWindowWidth() - 100, 25));
                            if (ImGui.Button("Open", new Vector2(80, 30))) LaunchProjectInstance(name);
                        }
                        ImGui.EndChild();
                        ImGui.PopStyleColor();
                        ImGui.PopStyleVar();
                        ImGui.Dummy(new Vector2(0, 8));
                        ImGui.PopID();
                    }
                    ImGui.EndChild();
                }
            }

            ImGui.NextColumn();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
            ImGui.TextUnformatted("QUICK ACTIONS");
            ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(0, 10));

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15, 15));
            if (ImGui.BeginChild("ActionsPanel", new Vector2(-1, -1), (ImGuiChildFlags)1, ImGuiWindowFlags.NoScrollbar))
            {
                ImGui.Text("Create New Project");
                ImGui.Dummy(new Vector2(0, 5));
                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##NewProjInput", "Project Name...", ref _newProjectName, 64);
                
                ImGui.Dummy(new Vector2(0, 10));
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.45f, 0.8f, 1.0f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.55f, 0.9f, 1.0f));
                if (ImGui.Button("Create Project", new Vector2(-1, 40)) && !string.IsNullOrWhiteSpace(_newProjectName)) {
                    LaunchProjectInstance(_newProjectName);
                }
                ImGui.PopStyleColor(2);

                ImGui.Dummy(new Vector2(0, 30));
                ImGui.Separator();
                ImGui.Dummy(new Vector2(0, 15));

                ImGui.TextDisabled("Projects Root:");
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.4f, 0.4f, 1.0f));
                ImGui.TextWrapped(ProjectsRoot);
                ImGui.PopStyleColor();
                
                ImGui.Dummy(new Vector2(0, 10));
                if (ImGui.Button("Change Root Path", new Vector2(-1, 30))) {
                    _targetPathChangeBuffer = ProjectsRoot;
                    ImGui.OpenPopup("ChangeProjectsRootModal");
                }
            }
            ImGui.EndChild();
            ImGui.PopStyleVar(2);

            if (ImGui.BeginPopupModal("ChangeProjectsRootModal", null, ImGuiWindowFlags.AlwaysAutoResize)) {
                ImGui.Text("Select new directory for projects:");
                ImGui.Dummy(new Vector2(0, 10));
                ImGui.SetNextItemWidth(450);
                ImGui.InputText("##PathInputText", ref _targetPathChangeBuffer, 256);
                ImGui.Dummy(new Vector2(0, 10));
                ImGui.Separator();
                ImGui.Dummy(new Vector2(0, 5));
                if (ImGui.Button("Apply", new Vector2(120, 35))) {
                    if (Directory.Exists(_targetPathChangeBuffer)) {
                        ProjectsRoot = _targetPathChangeBuffer;
                        SaveGlobalSettings();
                        ImGui.CloseCurrentPopup();
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(120, 35))) ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
            ImGui.EndChild();
        }
        
        ImGui.SetCursorPos(new Vector2(20, winSize.Y - 35));
        ImGui.TextDisabled("Verity Engine v1.0.0-alpha | Built on Irodori & SDL2");
        
        ImGui.End();
    }

    private void SetupDockSpace()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos); ImGui.SetNextWindowSize(viewport.Size);
        var flags = ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f); ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f); ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
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
                if (ImGui.MenuItem("Close Project")) CloseProject();
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
                if (ImGui.MenuItem("Publish (Single EXE)")) assetWindow?.PublishSingleFile();
                ImGui.EndMenu();
            }
            float mid = ImGui.GetWindowWidth() * 0.5f; ImGui.SetCursorPosX(mid - 30);
            if (IsPlaying) { if (ImGui.Button("Stop", new Vector2(60, 0))) ExitPlayMode(); }
            else { if (ImGui.Button("Play", new Vector2(60, 0))) EnterPlayMode(); }
            ImGui.EndMenuBar();
        }
        ImGui.DockSpace(ImGui.GetID("VerityDockSpace"));
        ImGui.End();
    }

    public void SaveEntityAsBlueprint(Entity entity, string? targetPath = null)
    {
        string dir = targetPath ?? AssetsPath ?? ""; if (string.IsNullOrEmpty(dir)) return;
        string path = Path.Combine(dir, $"{entity.Name}.blueprint"); int count = 1;
        while (File.Exists(path)) path = Path.Combine(dir, $"{entity.Name}_{count++}.blueprint");
        try { File.WriteAllText(path, Verity.Core.Serialization.SceneSerializer.SerializeEntity(entity)); CoreDebug.Log($"[Blueprint] Saved: {Path.GetFileName(path)}"); }
        catch (Exception e) { CoreDebug.LogError($"[Blueprint] Failed to save: {e.Message}"); }
    }

    public Entity? InstantiateBlueprint(string path, Vector2? position = null, Entity? parent = null)
    {
        var world = WorldManager.ActiveWorld; if (world == null || !File.Exists(path) || AssetsPath == null) return null;
        try {
            string json = File.ReadAllText(path); var entity = Verity.Core.Serialization.SceneSerializer.DeserializeEntity(world, json, ScriptCompiler?.CompiledAssembly);
            if (entity != null) { if (position.HasValue) entity.Transform.Position = position.Value; if (parent != null) entity.Transform.SetParent(parent.Transform, false); BindAssetsRecursive(entity); return entity; }
        } catch (Exception e) { CoreDebug.LogError($"[Blueprint] Failed: {e.Message}"); }
        return null;
    }

    private void BindAssetsRecursive(Entity entity) {
        var sr = entity.GetComponent<SpriteRenderer>();
        if (sr != null && !string.IsNullOrWhiteSpace(sr.Sprite.Path)) {
            var fullPath = Path.Combine(ProjectPath!, sr.Sprite.Path);
            if (File.Exists(fullPath)) sr.Texture = TextureManager.Load(fullPath);
        }
        foreach (var child in entity.Transform.Children) BindAssetsRecursive(child.Owner);
    }

    public void Dispose() { _scriptCompiler?.Dispose(); _renderPipeline.Dispose(); _shader.Dispose(); _textureManager.Dispose(); _imgui.Dispose(); _device.Dispose(); }

    private void HandleGlobalShortcuts()
    {
        var io = ImGui.GetIO(); if (io.WantCaptureKeyboard) return;
        bool ctrl = io.KeyCtrl; bool shift = io.KeyShift;
        if (ctrl && !shift && ImGui.IsKeyPressed(ImGuiKey.S)) { GetWindow<ProjectWindow>()?.SaveActiveWorldAsAsset(); SaveProjectSettings(); }
        if (ctrl && ImGui.IsKeyPressed(ImGuiKey.P)) { if (IsPlaying) ExitPlayMode(); else EnterPlayMode(); }
        if (ctrl && shift && ImGui.IsKeyPressed(ImGuiKey.B)) GetWindow<ProjectWindow>()?.PublishSingleFile();
        if (ctrl && !shift && ImGui.IsKeyPressed(ImGuiKey.Z)) Undo();
        if ((ctrl && !shift && ImGui.IsKeyPressed(ImGuiKey.Y)) || (ctrl && shift && ImGui.IsKeyPressed(ImGuiKey.Z))) Redo();
    }
}
