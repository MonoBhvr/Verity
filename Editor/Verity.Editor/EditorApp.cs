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

    public EditorApp(string title = "Verity Editor", int width = 1280, int height = 720)
    {
        CoreDebug.OnLog += (msg, level) => ConsoleWindow.Log(msg, level);
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
        // Simple manual JSON check or just rely on default
        try {
            if (File.Exists(GlobalSettingsPath)) {
                // Not implementing full JSON parser here to avoid dependency issues if corrupted
                // Just ensuring directory exists
            }
        } catch { }
    }

    private void SaveGlobalSettings()
    {
        // Placeholder for global settings save
    }

    private string? FindKoreanFont()
    {
        string appDir = AppContext.BaseDirectory;
        string globalFontsDir = Path.Combine(appDir, "EditorResources", "Fonts");
        Directory.CreateDirectory(globalFontsDir);

        var files = Directory.GetFiles(globalFontsDir, "*.ttf");
        if (files.Length > 0) return files[0];
        return null;
    }

    public void OpenProject(string projectName)
    {
        CurrentProjectName = projectName;
        Directory.CreateDirectory(ProjectPath!);
        Directory.CreateDirectory(AssetsPath!);
        
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
        if (worldFiles.Count > 0) GetWindow<ProjectWindow>()?.LoadWorldByPath(worldFiles[0].FullName);
        else {
            var world = WorldManager.CreateWorld("Untitled");
            var cam = world.CreateEntity("Main Camera"); cam.AddComponent<Camera>();
        }
    }

    private void LoadProjectSettings()
    {
        if (AssetsPath == null) return;
        string path = Path.Combine(AssetsPath, "ProjectSettings.json");
        if (File.Exists(path))
        {
            try {
                var json = File.ReadAllText(path);
                ProjectSettings = System.Text.Json.JsonSerializer.Deserialize<ProjectSettings>(json) ?? new();
            } catch { ProjectSettings = new(); }
        }
        else {
            ProjectSettings = new();
            SaveProjectSettings();
        }
    }

    public void SaveProjectSettings()
    {
        if (AssetsPath == null) return;
        string path = Path.Combine(AssetsPath, "ProjectSettings.json");
        var json = System.Text.Json.JsonSerializer.Serialize(ProjectSettings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private void LoadBuildSettings()
    {
        if (AssetsPath == null) return;
        string path = Path.Combine(AssetsPath, "BuildSettings.json");
        if (File.Exists(path))
        {
            BuildSettings = BuildSettings.Load(path);
        }
        else {
            BuildSettings = new BuildSettings();
            SaveBuildSettings();
        }
    }

    public void SaveBuildSettings()
    {
        if (AssetsPath == null) return;
        string path = Path.Combine(AssetsPath, "BuildSettings.json");
        BuildSettings.Save(path);
    }

    public void RecordUndo()
    {
        var world = WorldManager.ActiveWorld;
        if (world != null) _undoSystem.Record(world);
    }

    public void BeginUndoAction()
    {
        var world = WorldManager.ActiveWorld;
        if (world != null) _undoSystem.BeginContinuousAction(world);
    }

    public void EndUndoAction()
    {
        var world = WorldManager.ActiveWorld;
        if (world != null) _undoSystem.EndContinuousAction(world);
    }

    public void Undo()
    {
        var world = WorldManager.ActiveWorld;
        if (world == null) return;

        Guid? selectedId = EditorSelection.SelectedEntity?.Id;
        _undoSystem.Undo(world);
        
        if (selectedId.HasValue)
            EditorSelection.SelectedEntity = world.GetAllEntities().FirstOrDefault(e => e.Id == selectedId.Value);
    }

    public void Redo()
    {
        var world = WorldManager.ActiveWorld;
        if (world == null) return;

        Guid? selectedId = EditorSelection.SelectedEntity?.Id;
        _undoSystem.Redo(world);

        if (selectedId.HasValue)
            EditorSelection.SelectedEntity = world.GetAllEntities().FirstOrDefault(e => e.Id == selectedId.Value);
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
        Time.Reset(); 
        _gameLoop = new GameLoop { ProjectSettings = ProjectSettings }; 
        IsPlaying = true;
    }

    public void ExitPlayMode()
    {
        if (!IsPlaying || WorldManager.ActiveWorld == null) return;
        EditorSelection.SelectedEntity = null;
        _snapshot?.Restore(WorldManager.ActiveWorld);
        _snapshot = null; _gameLoop = null; IsPlaying = false;
        Verity.Input.Input.Enabled = true;
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
            Time.FrameCount++;
            if (!IsPlaying) { Time.DeltaTime = deltaTime; Time.TotalTime += deltaTime; }
            
            Verity.Input.Input.Enabled = _isScreenFocused;
            _device.PollEvents();
            
            if (IsPlaying && _gameLoop != null) _gameLoop.TickLogic(deltaTime);
            HandleGlobalShortcuts();
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
                    if (ImGui.Begin(window.Title, ref open)) {
                        if (window is ScreenWindow && ImGui.IsWindowFocused())
                            _isScreenFocused = true;
                        
                        window.OnGui();
                    }
                    ImGui.End(); window.IsOpen = open;
                }
            }
            
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
        ImGui.SetNextWindowBgAlpha(0.6f);
        
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
    private unsafe void DrawLauncher()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos); ImGui.SetNextWindowSize(viewport.Size);
        ImGui.Begin("Launcher", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize);

        var drawList = ImGui.GetWindowDrawList();
        var winSize = ImGui.GetWindowSize();
        
        drawList.AddRectFilledMultiColor(viewport.Pos, viewport.Pos + winSize, 
            ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.15f, 1.0f)),
            ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.15f, 1.0f)),
            ImGui.GetColorU32(new Vector4(0.07f, 0.07f, 0.09f, 1.0f)),
            ImGui.GetColorU32(new Vector4(0.07f, 0.07f, 0.09f, 1.0f)));

        ImGui.SetCursorPosY(60);
        if (File.Exists(EditorLogoPath)) {
            var tex = _textureManager.Load(EditorLogoPath);
            if (tex is OpenGlTexture glTex) {
                float aspect = (float)glTex.Width / glTex.Height;
                float drawH = 140; float drawW = drawH * aspect;
                ImGui.SetCursorPosX((winSize.X - drawW) * 0.5f);
                
                ImTextureID texID = new((nint)glTex.Id);
                var texRef = new ImTextureRef(null, texID);
                ImGui.Image(texRef, new Vector2(drawW, drawH), new Vector2(0, 1), new Vector2(1, 0));
            }
        } else {
            ImGui.SetCursorPosX((winSize.X - 300) * 0.5f);
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "V E R I T Y   E N G I N E");
        }
        ImGui.Dummy(new Vector2(0, 40));
        
        float contentW = Math.Min(winSize.X * 0.85f, 1100f);
        ImGui.SetCursorPosX((winSize.X - contentW) * 0.5f);
        
        if (ImGui.BeginChild("LauncherContent", new Vector2(contentW, winSize.Y - 280), (ImGuiChildFlags)0, ImGuiWindowFlags.NoBackground)) {
            ImGui.Columns(2, "LauncherColumns", false);
            ImGui.SetColumnWidth(0, contentW * 0.65f);

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
            ImGui.TextUnformatted("RECENT PROJECTS");
            ImGui.PopStyleColor();
            ImGui.Separator();
            ImGui.Dummy(new Vector2(0, 10));
            
            if (Directory.Exists(ProjectsRoot)) {
                foreach (var dir in Directory.GetDirectories(ProjectsRoot)) {
                    var name = Path.GetFileName(dir);
                    ImGui.PushID(name);
                    ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
                    if (ImGui.BeginChild("Card", new Vector2(-10, 90), (ImGuiChildFlags)1, ImGuiWindowFlags.NoScrollbar)) {
                        bool cardHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows);
                        if (cardHovered) {
                            var dl = ImGui.GetWindowDrawList();
                            dl.AddRectFilled(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize(), ImGui.GetColorU32(new Vector4(1, 1, 1, 0.05f)), 6f);
                            if (ImGui.IsMouseClicked(0)) OpenProject(name);
                        }
                        ImGui.SetCursorPos(new Vector2(20, 20));
                        ImGui.TextUnformatted(name);
                        ImGui.SetCursorPos(new Vector2(20, 50));
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
                        ImGui.TextUnformatted(dir);
                        ImGui.PopStyleColor();
                    }
                    ImGui.EndChild();
                    ImGui.PopStyleVar();
                    ImGui.Dummy(new Vector2(0, 10));
                    ImGui.PopID();
                }
            }

            ImGui.NextColumn();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
            ImGui.TextUnformatted("ACTIONS");
            ImGui.PopStyleColor();
            ImGui.Separator();
            ImGui.Dummy(new Vector2(0, 15));
            
            ImGui.Text("New Project Name");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##NewProjInput", ref _newProjectName, 64);
            
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.5f, 0.8f, 1.0f));
            if (ImGui.Button("Create New Project", new Vector2(-1, 45)) && !string.IsNullOrWhiteSpace(_newProjectName)) {
                OpenProject(_newProjectName);
            }
            ImGui.PopStyleColor();
            
            ImGui.Dummy(new Vector2(0, 30));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1.0f));
            ImGui.TextUnformatted("SETTINGS");
            ImGui.PopStyleColor();
            ImGui.Separator();
            ImGui.Dummy(new Vector2(0, 10));
            
            ImGui.TextDisabled("Projects Directory:");
            ImGui.TextWrapped(ProjectsRoot);
            if (ImGui.Button("Change Root Path", new Vector2(-1, 35))) {
                _targetPathChangeBuffer = ProjectsRoot;
                ImGui.OpenPopup("ChangeProjectsRootModal");
            }

            if (ImGui.BeginPopupModal("ChangeProjectsRootModal", null, ImGuiWindowFlags.AlwaysAutoResize)) {
                ImGui.Text("Enter New Projects Root Path:");
                ImGui.Dummy(new Vector2(0, 5));
                ImGui.SetNextItemWidth(400);
                ImGui.InputText("##PathInputText", ref _targetPathChangeBuffer, 256);
                ImGui.Separator();
                if (ImGui.Button("Apply", new Vector2(120, 0))) {
                    if (Directory.Exists(_targetPathChangeBuffer)) {
                        ProjectsRoot = _targetPathChangeBuffer;
                        SaveGlobalSettings();
                        ImGui.CloseCurrentPopup();
                    } else {
                        CoreDebug.LogError("[Launcher] Invalid directory path.");
                    }
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(120, 0))) ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
            ImGui.EndChild();
        }
        
        ImGui.SetCursorPos(new Vector2(20, winSize.Y - 40));
        ImGui.TextDisabled("Verity Engine v1.0.0-alpha | Powered by Irodori & OpenTK");
        
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

    public void SaveEntityAsBlueprint(Entity entity, string? targetPath = null)
    {
        string dir = targetPath ?? AssetsPath ?? "";
        if (string.IsNullOrEmpty(dir)) return;

        string fileName = $"{entity.Name}.blueprint";
        string path = Path.Combine(dir, fileName);

        int count = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(dir, $"{entity.Name}_{count++}.blueprint");
        }

        try {
            string json = Verity.Core.Serialization.SceneSerializer.SerializeEntity(entity);
            File.WriteAllText(path, json);
            CoreDebug.Log($"[Blueprint] Saved: {Path.GetFileName(path)}");
        } catch (Exception e) {
            CoreDebug.LogError($"[Blueprint] Failed to save: {e.Message}");
        }
    }

    public Entity? InstantiateBlueprint(string path, Vector2? position = null, Entity? parent = null)
    {
        var world = WorldManager.ActiveWorld;
        if (world == null || !File.Exists(path) || AssetsPath == null) return null;

        try {
            string json = File.ReadAllText(path);
            var entity = Verity.Core.Serialization.SceneSerializer.DeserializeEntity(world, json, ScriptCompiler?.CompiledAssembly);

            if (entity != null)
            {
                if (position.HasValue) entity.Transform.Position = position.Value;
                if (parent != null) entity.Transform.SetParent(parent.Transform, false);
                
                BindAssetsRecursive(entity);
                CoreDebug.Log($"[Blueprint] Instantiated: {entity.Name}");
                return entity;
            }
        } catch (Exception e) {
            CoreDebug.LogError($"[Blueprint] Failed to instantiate: {e.Message}");
        }
        return null;
    }

    private void BindAssetsRecursive(Entity entity)
    {
        var sr = entity.GetComponent<SpriteRenderer>();
        if (sr != null && !string.IsNullOrWhiteSpace(sr.Sprite.Path))
        {
            var fullPath = Path.Combine(ProjectPath!, sr.Sprite.Path);
            if (File.Exists(fullPath))
            {
                sr.Texture = TextureManager.Load(fullPath);
            }
        }

        foreach (var child in entity.Transform.Children)
        {
            BindAssetsRecursive(child.Owner);
        }
    }

    public void MoveProject(string newRoot)
    {
        if (CurrentProjectName == null || ProjectPath == null) return;
        string oldPath = ProjectPath;
        string dest = Path.Combine(newRoot, CurrentProjectName);
        
        if (Directory.Exists(dest)) {
            CoreDebug.LogError($"[Project] Cannot move: Destination '{dest}' already exists.");
            return;
        }

        try {
            CoreDebug.Log($"[Project] Moving '{CurrentProjectName}' to {newRoot}...");
            Directory.CreateDirectory(newRoot);
            CopyDirectory(oldPath, dest);
            Directory.Delete(oldPath, true);
            
            ProjectsRoot = newRoot;
            SaveGlobalSettings();
            CoreDebug.Log($"[Project] Successfully moved to: {dest}");
        } catch (Exception e) {
            CoreDebug.LogError($"[Project] Move Failed: {e.Message}");
        }
    }

    private static void CopyDirectory(string source, string dest) {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories)) {
            var rel = Path.GetRelativePath(source, f); var d = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(d)!); File.Copy(f, d, true);
        }
    }

    public void Dispose() { _scriptCompiler?.Dispose(); _renderPipeline.Dispose(); _shader.Dispose(); _textureManager.Dispose(); _imgui.Dispose(); _device.Dispose(); }

    private void HandleGlobalShortcuts()
    {
        var io = ImGui.GetIO();
        if (io.WantCaptureKeyboard) return;

        bool ctrl = io.KeyCtrl;
        bool shift = io.KeyShift;

        if (ctrl && !shift && ImGui.IsKeyPressed(ImGuiKey.S))
        {
            GetWindow<ProjectWindow>()?.SaveActiveWorldAsAsset();
            SaveProjectSettings();
        }

        if (ctrl && ImGui.IsKeyPressed(ImGuiKey.P))
        {
            if (IsPlaying) ExitPlayMode();
            else EnterPlayMode();
        }

        if (ctrl && shift && ImGui.IsKeyPressed(ImGuiKey.B))
        {
            GetWindow<ProjectWindow>()?.PublishSingleFile();
        }

        if (ctrl && !shift && ImGui.IsKeyPressed(ImGuiKey.Z))
        {
            Undo();
        }

        if ((ctrl && !shift && ImGui.IsKeyPressed(ImGuiKey.Y)) || (ctrl && shift && ImGui.IsKeyPressed(ImGuiKey.Z)))
        {
            Redo();
        }
    }
}
