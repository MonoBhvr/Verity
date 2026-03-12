using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hexa.NET.ImGui;
using Irodori.Backend.OpenGL;
using Verity.Core;
using CoreDebug = Verity.Core.Debug;
using Verity.Core.ECS;
using Verity.Core.Engine;
using Verity.Core.World;
using Verity.Editor.Windows;
using Verity.Core.Serialization;
using Verity.Graphics;
using Verity.Input;
using Color = System.Drawing.Color;
using SortingLayer = Verity.Graphics.SortingLayer;

namespace Verity.Editor;

public class EditorGlobalSettings
{
    public string ProjectsRoot { get; set; } = "";
    public string Language { get; set; } = "ko";
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
    private FileSystemWatcher? _assetWatcher;

    private readonly List<(string text, float duration)> _overlayMessages = new();
    private Filter? _filterToDelete;
    private bool _triggerDeletePopup;

    public ProjectSettings ProjectSettings { get; private set; } = new();
    public BuildSettings BuildSettings { get; private set; } = new();

    public bool IsPlaying { get; private set; }
    public bool IsBuilding { get; set; }
    public string BuildStatus { get; set; } = "";

    public string? CurrentProjectName { get; private set; }
    public string ProjectsRoot { get; private set; }
    public string? ProjectPath => CurrentProjectName != null ? Path.Combine(ProjectsRoot, CurrentProjectName) : null;
    public string? AssetsPath => ProjectPath != null ? Path.Combine(ProjectPath, "Assets") : null;

    public string EditorLogoPath {
        get {
            string[] searchPaths = {
                Path.Combine(AppContext.BaseDirectory, "EditorResources", "EditorLogo.png"),
                Path.Combine(AppContext.BaseDirectory, "..", "EditorResources", "EditorLogo.png"),
                Path.Combine(Directory.GetCurrentDirectory(), "EditorResources", "EditorLogo.png")
            };
            return searchPaths.FirstOrDefault(File.Exists) ?? Path.Combine(AppContext.BaseDirectory, "EditorLogo.png");
        }
    }

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
    private bool _pendingLayoutReset;

    public static string Version => VerityCore.Version;
    private bool _hasUnsavedChanges;
    private bool _showExitConfirmPopup;
    private bool _showCloseProjectConfirmPopup;
    private Action? _pendingExitAction;

    public void MarkAsDirty()
    {
        if (!_hasUnsavedChanges)
        {
            _hasUnsavedChanges = true;
            UpdateWindowTitle();
        }
    }

    public void ResetDirty()
    {
        _hasUnsavedChanges = false;
        UpdateWindowTitle();
    }

    public void UpdateWindowTitle()
    {
        string projectName = CurrentProjectName ?? L10n.Tr("field_NoProject");
        string worldName = WorldManager.ActiveWorld?.Name ?? L10n.Tr("field_NoWorld");
        string dirtyMarker = _hasUnsavedChanges ? "*" : "";
        _device.SetWindowTitle($"Verity {Version} - {projectName} - {worldName}.verity{dirtyMarker}");
    }

    public void RequestExit()
    {
        if (_hasUnsavedChanges)
        {
            _pendingExitAction = () => _device.Window.Close();
            _showExitConfirmPopup = true;
        }
        else
        {
            _device.Window.Close();
        }
    }

    public void RequestCloseProject()
    {
        if (_hasUnsavedChanges)
        {
            _pendingExitAction = () => ActualCloseProject();
            _showCloseProjectConfirmPopup = true;
        }
        else
        {
            ActualCloseProject();
        }
    }

    private void ActualCloseProject()
    {
        _projectLock?.Dispose();
        _projectLock = null;
        CurrentProjectName = null;
        ResetDirty();
    }

    public EditorApp(string title = "Verity", int width = 900, int height = 600)
    {
        L10n.Initialize();
        CoreDebug.OnLog += (msg, level) => ConsoleWindow.Log(msg, level);
        
        // Initialize default ProjectsRoot
        string docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        ProjectsRoot = Path.Combine(docsPath, "VerityProjects");
        
        LoadGlobalSettings();
        Directory.CreateDirectory(ProjectsRoot);

        _device = GraphicsDevice.Create(title, width, height);
        _imgui = new ImGuiController();
        
        string? fontPath = FindKoreanFont();
        _imgui.Initialize(_device, fontPath, this.ProjectSettings.EditorFontSize);
        
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
                if (settings != null)
                {
                    if (!string.IsNullOrWhiteSpace(settings.ProjectsRoot)) ProjectsRoot = settings.ProjectsRoot;
                    L10n.LoadLanguage(settings.Language ?? "ko");
                }
            } 
            else 
            {
                L10n.LoadLanguage("ko");
            }
        } 
        catch (Exception e) 
        {
            CoreDebug.LogError($"[Launcher] Failed to load global settings: {e.Message}");
            L10n.LoadLanguage("ko");
        }
    }

    private void SaveGlobalSettings() 
    {
        try
        {
            var dir = Path.GetDirectoryName(GlobalSettingsPath);
            if (dir != null) Directory.CreateDirectory(dir);
            
            var settings = new EditorGlobalSettings { 
                ProjectsRoot = ProjectsRoot,
                Language = L10n.CurrentLanguage
            };
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
        string[] searchPaths = {
            Path.Combine(AppContext.BaseDirectory, "EditorResources", "Fonts"),
            Path.Combine(AppContext.BaseDirectory, "..", "EditorResources", "Fonts"),
            Path.Combine(Directory.GetCurrentDirectory(), "EditorResources", "Fonts")
        };
        
        foreach (var path in searchPaths) {
            if (Directory.Exists(path)) {
                var files = Directory.GetFiles(path, "*.ttf");
                if (files.Length > 0) return files[0];
            }
        }
        return null;
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
            _projectLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            using (var writer = new StreamWriter(_projectLock, leaveOpen: true))
            {
                _projectLock.SetLength(0);
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

        _device.SetSize(1600, 900);
        _worldCamera.SetViewportSize(1600, 900);
        UpdateWindowTitle();

        EnsureProjectFileExists(projectPath, projectName);
        _pendingLayoutReset = true;

        Verity.Input.FilterManager.SavePath = Path.Combine(AssetsPath!, "Filters.json");
        Verity.Input.FilterManager.Load();
        LoadProjectSettings();
        LoadBuildSettings();
        _renderPipeline.BaseAssetsPath = ProjectPath;
        
        InitializeAssetWatcher(AssetsPath!);

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

    private static readonly JsonSerializerOptions _projectSettingsOptions = new() 
    { 
        WriteIndented = true, 
        Converters = { new Vector2Converter(), new Verity.Core.Serialization.ColorConverter() }
    };

    private void LoadProjectSettings()
    {
        if (AssetsPath == null) return;
        string path = Path.Combine(AssetsPath, "ProjectSettings.json");
        if (File.Exists(path)) {
            try { 
                var json = File.ReadAllText(path); 
                var settings = JsonSerializer.Deserialize<ProjectSettings>(json, _projectSettingsOptions);
                this.ProjectSettings = settings ?? new();
                SortingLayer.SyncWithSettings(this.ProjectSettings.SortingLayers);
            }
            catch (Exception e) { 
                CoreDebug.LogError($"[Project] Failed to load settings: {e.Message}");
                this.ProjectSettings = new(); 
            }
        } else { this.ProjectSettings = new(); SaveProjectSettings(); }
    }

    public void SaveProjectSettings()
    {
        if (AssetsPath == null) return;
        string path = Path.Combine(AssetsPath, "ProjectSettings.json");
        try {
            var json = JsonSerializer.Serialize(this.ProjectSettings, _projectSettingsOptions);
            File.WriteAllText(path, json);
        } catch (Exception e) {
            CoreDebug.LogError($"[Project] Failed to save settings: {e.Message}");
        }
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
        if (File.Exists(path)) this.BuildSettings = BuildSettings.Load(path);
        else { this.BuildSettings = new BuildSettings(); SaveBuildSettings(); }
    }

    public void SaveBuildSettings()
    {
        if (AssetsPath == null) return;
        string path = Path.Combine(AssetsPath, "BuildSettings.json");
        this.BuildSettings.Save(path);
    }

    public void RecordUndo() { var world = WorldManager.ActiveWorld; if (world != null) { _undoSystem.Record(world, this.ProjectSettings, this.BuildSettings); MarkAsDirty(); } }
    public void BeginUndoAction() { var world = WorldManager.ActiveWorld; if (world != null) { _undoSystem.BeginContinuousAction(world, this.ProjectSettings, this.BuildSettings); MarkAsDirty(); } }
    public void EndUndoAction() { var world = WorldManager.ActiveWorld; if (world != null) { _undoSystem.EndContinuousAction(world, this.ProjectSettings, this.BuildSettings); MarkAsDirty(); } }
    
    public void Undo() 
    { 
        var world = WorldManager.ActiveWorld; 
        if (world == null) return; 
        var state = _undoSystem.Undo(world, this.ProjectSettings, this.BuildSettings);
        if (state != null) RestoreState(state);
        UpdateWindowTitle();
    }

    public void Redo() 
    { 
        var world = WorldManager.ActiveWorld; 
        if (world == null) return; 
        var state = _undoSystem.Redo(world, this.ProjectSettings, this.BuildSettings);
        if (state != null) RestoreState(state);
        UpdateWindowTitle();
    }

    private void RestoreState(UndoState state)
    {
        var world = WorldManager.ActiveWorld;
        if (world == null) return;
        Guid? selectedId = EditorSelection.SelectedEntity?.Id;
        world.ClearAllEntities();
        SceneSerializer.Deserialize(world, state.WorldJson, _scriptCompiler?.CompiledAssembly);
        try { 
            var ps = JsonSerializer.Deserialize<ProjectSettings>(state.ProjectSettingsJson, _projectSettingsOptions);
            if (ps != null) this.ProjectSettings = ps;
            var bs = JsonSerializer.Deserialize<BuildSettings>(state.BuildSettingsJson);
            if (bs != null) this.BuildSettings = bs;
        } catch { }
        foreach (var entity in world.GetAllEntities()) {
            var sr = entity.GetComponent<SpriteRenderer>();
            if (sr != null && !string.IsNullOrWhiteSpace(sr.Sprite.Path)) {
                var fullPath = Path.Combine(ProjectPath!, sr.Sprite.Path);
                if (File.Exists(fullPath)) sr.Texture = TextureManager.Load(fullPath);
            }
        }
        if (selectedId.HasValue)
            EditorSelection.SelectedEntity = world.GetAllEntities().FirstOrDefault(e => e.Id == selectedId.Value);
        UpdateWindowTitle();
    }

    public void AddWindow(EditorWindow window) => _windows.Add(window);
    public T? GetWindow<T>() where T : EditorWindow => _windows.OfType<T>().FirstOrDefault();

    private string? ResolveProjectRoot()
    {
        string[] startPaths = { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (var startPath in startPaths) {
            var curr = startPath;
            while (!string.IsNullOrEmpty(curr)) {
                if (File.Exists(Path.Combine(curr, "Verity.sln"))) return Path.GetFullPath(curr);
                var p = Directory.GetParent(curr);
                if (p == null) break;
                curr = p.FullName;
            }
        }
        return null;
    }

    private void EnsureProjectFileExists(string projectPath, string projectName)
    {
        try {
            string csprojPath = Path.Combine(projectPath, $"{projectName}.csproj");
            if (File.Exists(csprojPath)) return;
            string? engineRoot = ResolveProjectRoot();
            if (engineRoot == null) return;
            string engineDir = Path.Combine(engineRoot, "Engine");
            string coreProj = Path.Combine(engineDir, "Verity.Core", "Verity.Core.csproj");
            string graphicsProj = Path.Combine(engineDir, "Verity.Graphics", "Verity.Graphics.csproj");
            string inputProj = Path.Combine(engineDir, "Verity.Input", "Verity.Input.csproj");
            if (!File.Exists(coreProj)) return;
            string corePath = Path.GetRelativePath(projectPath, coreProj);
            string graphicsPath = Path.GetRelativePath(projectPath, graphicsProj);
            string inputPath = Path.GetRelativePath(projectPath, inputProj);
            string content = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include=""{corePath}"" />
    <ProjectReference Include=""{graphicsPath}"" />
    <ProjectReference Include=""{inputPath}"" />
  </ItemGroup>
</Project>";
            File.WriteAllText(csprojPath, content);
        } catch { }
    }

    private void OnScriptsCompiled() { var world = WorldManager.ActiveWorld; if (world == null || IsPlaying) return; var json = Verity.Core.Serialization.SceneSerializer.Serialize(world); world.ClearAllEntities(); Verity.Core.Serialization.SceneSerializer.Deserialize(world, json, _scriptCompiler?.CompiledAssembly); EditorSelection.SelectedEntity = null; }

    public void EnterPlayMode() { if (WorldManager.ActiveWorld == null || IsPlaying) return; _snapshot = WorldSnapshot.Capture(WorldManager.ActiveWorld); Time.Reset(); _gameLoop = new GameLoop { ProjectSettings = this.ProjectSettings }; IsPlaying = true; }
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

            // Handle window close button
            if (_device.Window.ShouldClose && _hasUnsavedChanges && !_showExitConfirmPopup)
            {
                _device.Window.CancelClose();
                RequestExit();
            }

            if (IsPlaying && _gameLoop != null) _gameLoop.TickLogic(deltaTime);
            HandleGlobalShortcuts();
            if (_isFocusInterpolating) {
                float t = Math.Min(1.0f, deltaTime * 8.0f);
                _worldCamera.Position = Vector2.Lerp(_worldCamera.Position, _targetCameraPosition, t);
                _worldCamera.Zoom = _worldCamera.Zoom + (_targetCameraZoom - _worldCamera.Zoom) * t;
                if (Vector2.DistanceSquared(_worldCamera.Position, _targetCameraPosition) < 0.000001f && MathF.Abs(_worldCamera.Zoom - _targetCameraZoom) < 0.0001f) {
                    _worldCamera.Position = _targetCameraPosition; _worldCamera.Zoom = _targetCameraZoom; _isFocusInterpolating = false;
                }
            }
            _device.Gl.Viewport(0, 0, _device.Window.GetWidth(), _device.Window.GetHeight());
            _device.Clear(Color.FromArgb(255, 30, 30, 30));
            _imgui.BeginFrame();
            if (CurrentProjectName == null) DrawLauncher();
            else {
                SetupDockSpace(); _isScreenFocused = false;
                foreach (var window in _windows) {
                    if (!window.IsOpen) continue;
                    bool open = window.IsOpen;
                    if (ImGui.Begin(window.Title, ref open)) { if (window is ScreenWindow && ImGui.IsWindowFocused()) _isScreenFocused = true; window.OnGui(); }
                    ImGui.End(); window.IsOpen = open;
                }
            }
            DrawGlobalPopups();
            DrawOverlays(deltaTime);
            _imgui.EndFrame();
            CoreDebug.ClearDrawCommands();
            _device.SwapBuffers();
        }
    }

    public void RequestDeleteFilter(Filter filter)
    {
        _filterToDelete = filter;
        _triggerDeletePopup = true;
    }

    private unsafe void DrawGlobalPopups()
    {
        if (_triggerDeletePopup) { ImGui.OpenPopup("DeleteFilterConfirm"); _triggerDeletePopup = false; }
        if (_showExitConfirmPopup) { ImGui.OpenPopup("ExitConfirm"); _showExitConfirmPopup = false; }
        if (_showCloseProjectConfirmPopup) { ImGui.OpenPopup("CloseProjectConfirm"); _showCloseProjectConfirmPopup = false; }

        var modalFlags = ImGuiWindowFlags.AlwaysAutoResize;
        var btnSize = new Vector2(150, 30);

        if (ImGui.BeginPopupModal("DeleteFilterConfirm", (bool*)null, modalFlags)) {
            ImGui.Text(L10n.Tr("msg_confirm_delete_filter", _filterToDelete?.Name ?? ""));
            ImGui.Separator(); ImGui.Dummy(new Vector2(0, 10));
            if (ImGui.Button(L10n.Tr("btn_delete") ?? "Delete", btnSize)) {
                if (_filterToDelete != null) {
                    GetWindow<FilterEditorWindow>()?.SelectFilter(null);
                    FilterManager.Remove(_filterToDelete.Name);
                    _filterToDelete = null;
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button(L10n.Tr("btn_cancel") ?? "Cancel", btnSize)) { _filterToDelete = null; ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }

        if (ImGui.BeginPopupModal("ExitConfirm", (bool*)null, modalFlags)) {
            ImGui.Text(L10n.Tr("msg_unsaved_changes"));
            ImGui.TextDisabled(L10n.Tr("msg_exit_confirm"));
            ImGui.Separator(); ImGui.Dummy(new Vector2(0, 10));
            if (ImGui.Button(L10n.Tr("btn_save_and_exit"), btnSize)) { 
                GetWindow<ProjectWindow>()?.SaveActiveWorldAsAsset(); 
                SaveProjectSettings(); 
                _pendingExitAction?.Invoke(); 
                ImGui.CloseCurrentPopup(); 
            }
            ImGui.SameLine();
            if (ImGui.Button(L10n.Tr("btn_exit_without_save"), btnSize)) { _pendingExitAction?.Invoke(); ImGui.CloseCurrentPopup(); }
            ImGui.SameLine();
            if (ImGui.Button(L10n.Tr("btn_cancel"), btnSize)) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        if (ImGui.BeginPopupModal("CloseProjectConfirm", (bool*)null, modalFlags)) {
            ImGui.Text(L10n.Tr("msg_unsaved_changes"));
            ImGui.TextDisabled(L10n.Tr("msg_close_project_confirm"));
            ImGui.Separator(); ImGui.Dummy(new Vector2(0, 10));
            if (ImGui.Button(L10n.Tr("btn_save_and_close"), btnSize)) { 
                GetWindow<ProjectWindow>()?.SaveActiveWorldAsAsset(); 
                SaveProjectSettings(); 
                _pendingExitAction?.Invoke(); 
                ImGui.CloseCurrentPopup(); 
            }
            ImGui.SameLine();
            if (ImGui.Button(L10n.Tr("btn_close_without_save"), btnSize)) { _pendingExitAction?.Invoke(); ImGui.CloseCurrentPopup(); }
            ImGui.SameLine();
            if (ImGui.Button(L10n.Tr("btn_cancel"), btnSize)) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }

    private void DrawOverlays(float dt)
    {
        var viewport = ImGui.GetMainViewport();
        var dl = ImGui.GetForegroundDrawList();
        var center = viewport.Pos + viewport.Size * 0.5f;

        if (IsBuilding) {
            // Full screen dimming
            dl.AddRectFilled(viewport.Pos, viewport.Pos + viewport.Size, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.6f)));
            
            string t1 = L10n.Tr("msg_building_project"); string t2 = BuildStatus;
            var s1 = ImGui.CalcTextSize(t1); var s2 = ImGui.CalcTextSize(t2);
            
            dl.AddText(center - new Vector2(s1.X * 0.5f, 20), ImGui.GetColorU32(new Vector4(1, 1, 0, 1)), t1);
            dl.AddText(center - new Vector2(s2.X * 0.5f, -10), ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), t2);
        }

        if (_overlayMessages.Count > 0) {
            float yOffset = viewport.Size.Y - 40;
            for (int i = _overlayMessages.Count - 1; i >= 0; i--) {
                var msg = _overlayMessages[i];
                string text = $"[Verity] {msg.text}";
                var textSize = ImGui.CalcTextSize(text);
                var pos = viewport.Pos + new Vector2(20, yOffset - textSize.Y);
                
                // Draw background box for readability
                dl.AddRectFilled(pos - new Vector2(5, 2), pos + textSize + new Vector2(5, 2), ImGui.GetColorU32(new Vector4(0, 0, 0, 0.8f)), 4f);
                dl.AddText(pos, ImGui.GetColorU32(new Vector4(1, 0.8f, 0.2f, 1)), text);
                
                yOffset -= (textSize.Y + 10);
                float newDur = msg.duration - dt; 
                if (newDur <= 0) _overlayMessages.RemoveAt(i); 
                else _overlayMessages[i] = (msg.text, newDur);
            }
        }
    }

    private unsafe void DrawLauncher()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos); ImGui.SetNextWindowSize(viewport.Size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
        ImGui.Begin(L10n.Tr("window_launcher"), ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoBringToFrontOnFocus);
        ImGui.PopStyleVar();
        var drawList = ImGui.GetWindowDrawList(); var winSize = ImGui.GetWindowSize();
        drawList.AddRectFilledMultiColor(viewport.Pos, viewport.Pos + winSize, 
            ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.16f, 1.0f)), ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.16f, 1.0f)),
            ImGui.GetColorU32(new Vector4(0.06f, 0.06f, 0.08f, 1.0f)), ImGui.GetColorU32(new Vector4(0.06f, 0.06f, 0.08f, 1.0f)));
        ImGui.SetCursorPosY(50);
        if (File.Exists(EditorLogoPath)) {
            var tex = _textureManager.Load(EditorLogoPath);
            if (tex is OpenGlTexture glTex) {
                float aspect = (float)glTex.Width / glTex.Height; float drawH = 100; float drawW = drawH * aspect;
                ImGui.SetCursorPosX((winSize.X - drawW) * 0.5f);
                ImGui.Image(new ImTextureRef(null, new ImTextureID((nint)glTex.Id)), new Vector2(drawW, drawH), new Vector2(0, 1), new Vector2(1, 0));
            }
        } else {
            ImGui.SetCursorPosX((winSize.X - 400) * 0.5f); ImGui.TextColored(new Vector4(0.3f, 0.7f, 1.0f, 1.0f), "V E R I T Y   E N G I N E");
        }
        ImGui.SetCursorPosY(170); ImGui.Separator(); ImGui.Dummy(new Vector2(0, 20));
        float contentW = winSize.X * 0.9f; ImGui.SetCursorPosX((winSize.X - contentW) * 0.5f);
        if (ImGui.BeginChild("LauncherContent", new Vector2(contentW, winSize.Y - 240), (ImGuiChildFlags)0, ImGuiWindowFlags.NoBackground)) {
            ImGui.Columns(2, "LauncherColumns", false); ImGui.SetColumnWidth(0, contentW * 0.65f);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f)); ImGui.TextUnformatted(L10n.Tr("label_recent_projects")); ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(0, 10));
            if (Directory.Exists(ProjectsRoot)) {
                if (ImGui.BeginChild("ProjectList", new Vector2(-10, -1), (ImGuiChildFlags)0, ImGuiWindowFlags.NoBackground)) {
                    var projectInfos = Directory.GetDirectories(ProjectsRoot).Select(d => {
                        var di = new DirectoryInfo(d); var assetsDir = Path.Combine(d, "Assets"); DateTime lastMod = di.LastWriteTime;
                        if (Directory.Exists(assetsDir)) {
                            var files = Directory.GetFiles(assetsDir, "*", SearchOption.AllDirectories);
                            if (files.Length > 0) { var latestFileMod = files.Select(f => File.GetLastWriteTime(f)).Max(); if (latestFileMod > lastMod) lastMod = latestFileMod; }
                        }
                        return new { Name = di.Name, FullPath = di.FullName, LastModified = lastMod };
                    }).OrderByDescending(p => p.LastModified).ToList();
                    foreach (var proj in projectInfos) {
                        ImGui.PushID(proj.FullPath); ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 8f); ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(1, 1, 1, 0.03f));
                        if (ImGui.BeginChild("Card", new Vector2(-1, 80), (ImGuiChildFlags)1, ImGuiWindowFlags.NoScrollbar)) {
                            if (ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows)) {
                                drawList.AddRectFilled(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize(), ImGui.GetColorU32(new Vector4(1, 1, 1, 0.05f)), 8f);
                                if (ImGui.IsMouseClicked(0)) LaunchProjectInstance(proj.Name);
                                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                            }
                            ImGui.SetCursorPos(new Vector2(20, 12)); ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.9f, 0.9f, 0.9f, 1.0f)); ImGui.TextUnformatted(proj.Name); ImGui.PopStyleColor();
                            ImGui.SetCursorPos(new Vector2(20, 34)); ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.4f, 0.4f, 1.0f)); ImGui.TextUnformatted(proj.FullPath); ImGui.PopStyleColor();
                            ImGui.SetCursorPos(new Vector2(20, 54)); ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.3f, 0.6f, 0.3f, 1.0f)); ImGui.TextUnformatted(L10n.Tr("label_last_modified", proj.LastModified.ToString("yyyy-MM-dd HH:mm:ss"))); ImGui.PopStyleColor();
                            ImGui.SetCursorPos(new Vector2(ImGui.GetWindowWidth() - 100, 25)); if (ImGui.Button(L10n.Tr("btn_open"), new Vector2(80, 30))) LaunchProjectInstance(proj.Name);
                        }
                        ImGui.EndChild(); ImGui.PopStyleColor(); ImGui.PopStyleVar(); ImGui.Dummy(new Vector2(0, 8)); ImGui.PopID();
                    }
                }
                ImGui.EndChild();
            }
            ImGui.NextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f)); ImGui.TextUnformatted(L10n.Tr("label_quick_actions")); ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(0, 10));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f); ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(15, 15));
            if (ImGui.BeginChild("ActionsPanel", new Vector2(-1, -1), (ImGuiChildFlags)1, ImGuiWindowFlags.NoScrollbar)) {
                ImGui.Text(L10n.Tr("label_create_new_project")); ImGui.Dummy(new Vector2(0, 5)); ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##NewProjInput", L10n.Tr("label_name") + "...", ref _newProjectName, 64);
                ImGui.Dummy(new Vector2(0, 10)); ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.45f, 0.8f, 1.0f)); ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.55f, 0.9f, 1.0f));
                if (ImGui.Button(L10n.Tr("btn_create_project"), new Vector2(-1, 40)) && !string.IsNullOrWhiteSpace(_newProjectName)) LaunchProjectInstance(_newProjectName);
                ImGui.PopStyleColor(2); ImGui.Dummy(new Vector2(0, 30)); ImGui.Separator(); ImGui.Dummy(new Vector2(0, 15));
                ImGui.TextDisabled(L10n.Tr("label_projects_root")); ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 0.4f, 0.4f, 1.0f)); ImGui.TextWrapped(ProjectsRoot); ImGui.PopStyleColor();
                ImGui.Dummy(new Vector2(0, 10));
                float btnWidth = (ImGui.GetContentRegionAvail().X - 10) * 0.5f;
                if (ImGui.Button(L10n.Tr("btn_open_in_explorer") + " (F)", new Vector2(btnWidth, 30))) { if (Directory.Exists(ProjectsRoot)) Process.Start("explorer.exe", ProjectsRoot.Replace("/", "\\")); }
                ImGui.SameLine();
                if (ImGui.Button(L10n.Tr("btn_change_root_path"), new Vector2(btnWidth, 30))) { 
                    var newPath = SelectFolderNative(ProjectsRoot);
                    if (newPath != null && Directory.Exists(newPath)) {
                        ProjectsRoot = newPath;
                        SaveGlobalSettings();
                    }
                }
            }
            ImGui.EndChild(); ImGui.PopStyleVar(2);
        }
        ImGui.EndChild();
        ImGui.SetCursorPos(new Vector2(20, winSize.Y - 35)); ImGui.TextDisabled($"Verity Engine v{Version} | Built on Irodori & SDL2");
        ImGui.End();
    }

    private unsafe void SetupDockSpace()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos); ImGui.SetNextWindowSize(viewport.Size);
        var flags = ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f); ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f); ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
        ImGui.Begin("DockSpace", flags); ImGui.PopStyleVar(3);
        
        bool resetLayout = false;

        if (ImGui.BeginMenuBar()) {
            var assetWindow = GetWindow<ProjectWindow>();
            if (ImGui.BeginMenu(L10n.Tr("menu_file"))) {
                if (ImGui.MenuItem(L10n.Tr("menu_new_world"))) assetWindow?.CreateWorldInProject();
                if (ImGui.BeginMenu(L10n.Tr("menu_open_world"))) {
                    if (AssetsPath != null && Directory.Exists(AssetsPath)) {
                        foreach (var f in Directory.GetFiles(AssetsPath, "*.verity", SearchOption.AllDirectories))
                            if (ImGui.MenuItem(Path.GetRelativePath(AssetsPath, f))) assetWindow?.LoadWorldByPath(f);
                    }
                    ImGui.EndMenu();
                }
                if (ImGui.MenuItem(L10n.Tr("menu_save_world"))) assetWindow?.SaveActiveWorldAsAsset();
                ImGui.Separator();
                if (ImGui.MenuItem(L10n.Tr("menu_close_project"))) RequestCloseProject();
                if (ImGui.MenuItem(L10n.Tr("menu_exit"))) RequestExit();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu(L10n.Tr("menu_window"))) {
                foreach (var win in _windows) if (ImGui.MenuItem(win.Title, "", win.IsOpen)) win.IsOpen = !win.IsOpen;
                ImGui.Separator();
                if (ImGui.MenuItem(L10n.Tr("menu_reset_layout"))) resetLayout = true;
                ImGui.Separator();
                if (ImGui.BeginMenu(L10n.Tr("menu_language"))) {
                    if (ImGui.MenuItem("English", "", L10n.CurrentLanguage == "en")) { L10n.LoadLanguage("en"); SaveGlobalSettings(); resetLayout = true; }
                    if (ImGui.MenuItem("한국어", "", L10n.CurrentLanguage == "ko")) { L10n.LoadLanguage("ko"); SaveGlobalSettings(); resetLayout = true; }
                    ImGui.EndMenu();
                }
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu(L10n.Tr("menu_build"))) {
                if (ImGui.MenuItem(L10n.Tr("window_buildsettings"))) GetWindow<BuildSettingsWindow>()!.IsOpen = true;
                ImGui.Separator();
                if (ImGui.MenuItem(L10n.Tr("menu_publish"))) assetWindow?.PublishSingleFile();
                ImGui.EndMenu();
            }
            float mid = ImGui.GetWindowWidth() * 0.5f; ImGui.SetCursorPosX(mid - 30);
            if (IsPlaying) { if (ImGui.Button(L10n.Tr("btn_stop"), new Vector2(60, 0))) ExitPlayMode(); }
            else { if (ImGui.Button(L10n.Tr("btn_play"), new Vector2(60, 0))) EnterPlayMode(); }
            ImGui.EndMenuBar();
        }

        uint dockId = ImGui.GetID("VerityDockSpace");

        if (resetLayout || _pendingLayoutReset || ImGuiP.DockBuilderGetNode(dockId).Handle == null)
        {
            _pendingLayoutReset = false;
            foreach (var win in _windows) win.RefreshTitle();

            ImGuiP.DockBuilderRemoveNode(dockId);
            ImGuiP.DockBuilderAddNode(dockId, ImGuiDockNodeFlags.None);
            ImGuiP.DockBuilderSetNodeSize(dockId, viewport.Size);

            uint centerId = dockId;
            uint leftId;
            uint rightId;
            uint bottomId;

            ImGuiP.DockBuilderSplitNode(centerId, ImGuiDir.Left, 0.2f, &leftId, &centerId);
            ImGuiP.DockBuilderSplitNode(centerId, ImGuiDir.Right, 0.25f, &rightId, &centerId);
            ImGuiP.DockBuilderSplitNode(centerId, ImGuiDir.Down, 0.3f, &bottomId, &centerId);

            ImGuiP.DockBuilderDockWindow(L10n.Tr("window_hierarchy"), leftId);
            ImGuiP.DockBuilderDockWindow(L10n.Tr("window_inspector"), rightId);
            ImGuiP.DockBuilderDockWindow(L10n.Tr("window_project"), bottomId);
            ImGuiP.DockBuilderDockWindow(L10n.Tr("window_console"), bottomId);
            ImGuiP.DockBuilderDockWindow(L10n.Tr("window_worldview"), centerId);
            ImGuiP.DockBuilderDockWindow(L10n.Tr("window_screen"), centerId);
            
            ImGuiP.DockBuilderFinish(dockId);
        }

        ImGui.DockSpace(dockId);
        ImGui.End();
    }

    public void SaveEntityAsBlueprint(Entity entity, string? targetPath = null) {
        string dir = targetPath ?? AssetsPath ?? ""; if (string.IsNullOrEmpty(dir)) return;
        string safeName = string.Join("_", entity.Name.Split(Path.GetInvalidFileNameChars())); if (string.IsNullOrWhiteSpace(safeName)) safeName = "Entity";
        string path = Path.Combine(dir, $"{safeName}.blueprint"); int count = 1; while (File.Exists(path)) path = Path.Combine(dir, $"{safeName}_{count++}.blueprint");
        try { string json = Verity.Core.Serialization.SceneSerializer.SerializeEntity(entity); File.WriteAllText(path, json); } catch { }
    }

    public Entity? InstantiateBlueprint(string path, Vector2? position = null, Entity? parent = null) {
        var world = WorldManager.ActiveWorld; if (world == null || !File.Exists(path) || AssetsPath == null) return null;
        try { string json = File.ReadAllText(path); var entity = Verity.Core.Serialization.SceneSerializer.DeserializeEntity(world, json, ScriptCompiler?.CompiledAssembly); if (entity != null) { if (position.HasValue) entity.Transform.Position = position.Value; if (parent != null) entity.Transform.SetParent(parent.Transform, false); BindAssetsRecursive(entity); return entity; } } catch { } return null;
    }

    private void BindAssetsRecursive(Entity entity) {
        var sr = entity.GetComponent<SpriteRenderer>(); if (sr != null && !string.IsNullOrWhiteSpace(sr.Sprite.Path)) { var fullPath = Path.Combine(ProjectPath!, sr.Sprite.Path); if (File.Exists(fullPath)) sr.Texture = TextureManager.Load(fullPath); }
        foreach (var child in entity.Transform.Children) BindAssetsRecursive(child.Owner);
    }

    private void InitializeAssetWatcher(string path)
    {
        _assetWatcher?.Dispose();
        _assetWatcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            Filter = "*.*",
            EnableRaisingEvents = true
        };

        FileSystemEventHandler onChange = (s, e) => {
            string ext = Path.GetExtension(e.FullPath).ToLower();
            if (ext == ".style") {
                if (ProjectPath != null) {
                    string relPath = Path.GetRelativePath(ProjectPath, e.FullPath).Replace("\\", "/");
                    _renderPipeline.ClearStyleCache(relPath);
                }
            } else if (ext == ".shader") {
                _renderPipeline.ClearShaderCache(e.FullPath);
            }
        };

        _assetWatcher.Changed += onChange;
        _assetWatcher.Created += onChange;
        _assetWatcher.Deleted += onChange;
        _assetWatcher.Renamed += (s, e) => onChange(s, e);
    }

    private string? SelectFolderNative(string initial)
    {
        try {
            // Using a PowerShell one-liner to pop up a standard Windows folder browser.
            // This avoids adding WinForms or Win32 COM complexity to the core project.
            var script = "Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.FolderBrowserDialog; " +
                         $"$f.SelectedPath = '{initial.Replace("'", "''")}'; " +
                         "if($f.ShowDialog() -eq 'OK') { $f.SelectedPath }";
            
            var psi = new ProcessStartInfo("powershell", $"-NoProfile -Command \"{script}\"") {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var proc = Process.Start(psi);
            var result = proc?.StandardOutput.ReadToEnd().Trim();
            proc?.WaitForExit();
            return string.IsNullOrEmpty(result) ? null : result;
        } catch { return null; }
    }

    public void Dispose() { 
        _assetWatcher?.Dispose();
        _scriptCompiler?.Dispose(); 
        _renderPipeline.Dispose(); 
        _shader.Dispose(); 
        _textureManager.Dispose(); 
        _imgui.Dispose(); 
        _device.Dispose(); 
    }

    private void HandleGlobalShortcuts() {
        var io = ImGui.GetIO(); if (io.WantCaptureKeyboard) return; bool ctrl = io.KeyCtrl; bool shift = io.KeyShift;
        if (ctrl && !shift && ImGui.IsKeyPressed(ImGuiKey.S)) { GetWindow<ProjectWindow>()?.SaveActiveWorldAsAsset(); SaveProjectSettings(); }
        if (ctrl && ImGui.IsKeyPressed(ImGuiKey.P)) { if (IsPlaying) ExitPlayMode(); else EnterPlayMode(); }
        if (ctrl && shift && ImGui.IsKeyPressed(ImGuiKey.B)) GetWindow<ProjectWindow>()?.PublishSingleFile();
        if (ctrl && !shift && ImGui.IsKeyPressed(ImGuiKey.Z)) Undo();
        if ((ctrl && !shift && ImGui.IsKeyPressed(ImGuiKey.Y)) || (ctrl && shift && ImGui.IsKeyPressed(ImGuiKey.Z))) Redo();
    }
}
