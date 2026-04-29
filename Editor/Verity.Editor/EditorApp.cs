using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hexa.NET.ImGui;
using Irodori.Backend.OpenGL;
using Irodori.Texture;
using Verity.Core;
using CoreDebug = Verity.Core.Debug;
using Verity.Core.ECS;
using Verity.Core.Engine;
using Verity.Core.World;
using Verity.Editor.Windows;
using Verity.Filter;
using FilterType = Verity.Filter.Filter;
using Verity.Core.Serialization;
using Verity.Graphics;
using Verity.Input;
using Verity.Core.UI;
using SortingLayer = Verity.Graphics.SortingLayer;
using Verity.Core.Audio;
using Verity.Core.Scripting;
using Verity.Editor.Profiling;

namespace Verity.Editor;

public class EditorGlobalSettings
{
    public string ProjectsRoot { get; set; } = "";
    public string Language { get; set; } = "ko";
}

public enum EditorWindowMode
{  
    Docked,
    Detached
} 

public enum EditorAssetKind
{
    World,
    Blueprint
}

internal sealed class EditorUndoState
{
    public WorldViewUndoState? WorldView { get; set; }
    public string? SelectedAssetPath { get; set; }
    public Verity.Core.World.TilemapEditor.Tool SelectedTileTool { get; set; } = Verity.Core.World.TilemapEditor.Tool.Brush;
    public int TileBrushSize { get; set; } = 1;
    public Verity.Core.World.TilemapEditor.BrushShape TileBrushShape { get; set; } = Verity.Core.World.TilemapEditor.BrushShape.Rectangle;
    public EditingPolygonUndoState? EditingPolygon { get; set; }
}

internal sealed class EditingPolygonUndoState
{
    public Guid EntityId { get; set; }
    public string ComponentTypeName { get; set; } = "";
}

public class EditorApp : IDisposable
{
    private const long AssetRefreshDebounceMs = 250;
    private const long LauncherProjectRefreshIntervalMs = 2000;

    private readonly record struct WindowPlacement(Vector2 Position, Vector2 Size);
    private readonly record struct BlueprintInstanceRefreshState(Entity Root, JsonArray Overrides);
    private readonly record struct LauncherProjectInfo(string Name, string FullPath, DateTime LastModified);

    private readonly GraphicsDevice _device;
    private readonly ImGuiController _imgui;
    private readonly Shader2D _shader;
    private readonly TextureManager _textureManager;
    private readonly RenderPipeline _renderPipeline;
    private readonly Camera _worldCamera;
    private readonly List<EditorWindow> _windows = [];
    private readonly Stopwatch _stopwatch = new();
    private readonly EditorProfiler _profiler = new();
    private GameLoop? _gameLoop;
    private WorldSnapshot? _snapshot;
    private ScriptCompiler? _scriptCompiler;
    private readonly UndoSystem _undoSystem = new();
    private FileSystemWatcher? _assetWatcher;
    private readonly object _assetInvalidationLock = new();
    private readonly HashSet<string> _pendingTextureRefreshes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingTileRefreshes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _pendingTextureRefreshDeadlines = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _pendingTileRefreshDeadlines = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _processedTextureRefreshSignatures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _processedTileRefreshSignatures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingLuaHotReloadPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _pendingLuaHotReloadDeadlines = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<Action> _pendingMainThreadActions = new();

    private readonly List<(string text, float duration)> _overlayMessages = new();
    private readonly List<LauncherProjectInfo> _launcherProjectCache = [];
    private readonly List<string> _worldAssetCache = [];
    private FilterType? _filterToDelete;
    private bool _triggerDeletePopup;

    public ProjectSettings ProjectSettings { get; private set; } = new();
    public BuildSettings BuildSettings { get; private set; } = new();

    public bool IsPlaying { get; private set; }
    public int LastPlayLogicTicksThisFrame { get; private set; }
    public bool IsBuilding { get; set; }
    public string BuildStatus { get; set; } = "";
    public bool HasScriptCompilationErrors => _scriptCompiler?.HasCompilationErrors == true;

    public string? CurrentProjectName { get; private set; }
    public string ProjectsRoot { get; private set; }
    public string? ProjectPath => CurrentProjectName != null ? Path.Combine(ProjectsRoot, CurrentProjectName) : null;
    public string? AssetsPath => ProjectPath != null ? Path.Combine(ProjectPath, "Assets") : null;
    public string? ActiveAssetPath { get; private set; }
    public string? LastWorldAssetPath { get; private set; }
    public EditorAssetKind ActiveAssetKind { get; private set; } = EditorAssetKind.World;
    public bool IsEditingBlueprint => ActiveAssetKind == EditorAssetKind.Blueprint;

    private string? _cachedEditorLogoPath;
    private bool _launcherProjectCacheDirty = true;
    private long _launcherProjectCacheNextRefreshMs;
    private bool _worldAssetCacheDirty = true;

    public string EditorLogoPath {
        get {
            _cachedEditorLogoPath ??= ResolveEditorLogoPath();
            return _cachedEditorLogoPath;
        }
    }

    private string GlobalSettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VerityProjects", "GlobalSettings.json");
    private string LayoutPresetsRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VerityProjects", "EditorLayouts");

    public GraphicsDevice Device => _device;
    public Shader2D Shader => _shader;
    public TextureManager TextureManager => _textureManager;
    public RenderPipeline RenderPipeline => _renderPipeline;
    public Camera WorldCamera => _worldCamera;
    public ScriptCompiler? ScriptCompiler => _scriptCompiler;
    public EditorProfiler Profiler => _profiler;

    private bool _isScreenFocused;
    private string _newProjectName = "";

    private Vector2 _targetCameraPosition;
    private float _targetCameraZoom;
    private bool _isFocusInterpolating;
    private bool _pendingLayoutReset;
    private bool _pendingDetachedLayoutReset;
    private bool _loadedDockLayoutFromSettings;
    private bool _dockLayoutPersistenceReady;
    private EditorWindowMode _windowMode = EditorWindowMode.Docked;
    private EditorWindow? _pendingFocusedWindow;
    private EditorWindow? _fullscreenWindow;
    private float _menuBarHeight;

    private string ResolveEditorLogoPath()
    {
        string[] searchPaths = {
            Path.Combine(AppContext.BaseDirectory, "EditorResources", "EditorLogo.png"),
            Path.Combine(AppContext.BaseDirectory, "..", "EditorResources", "EditorLogo.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "EditorResources", "EditorLogo.png")
        };

        return searchPaths.FirstOrDefault(File.Exists) ?? Path.Combine(AppContext.BaseDirectory, "EditorLogo.png");
    }

    private void InvalidateLauncherProjectCache()
    {
        _launcherProjectCacheDirty = true;
        _launcherProjectCacheNextRefreshMs = 0;
    }

    private void InvalidateWorldAssetCache()
    {
        _worldAssetCacheDirty = true;
    }

    private IReadOnlyList<string> GetWorldAssetPaths()
    {
        if (!_worldAssetCacheDirty)
            return _worldAssetCache;

        _worldAssetCache.Clear();
        if (AssetsPath != null && Directory.Exists(AssetsPath))
        {
            foreach (string path in Directory.EnumerateFiles(AssetsPath, "*.verity", SearchOption.AllDirectories))
                _worldAssetCache.Add(path);

            _worldAssetCache.Sort(StringComparer.OrdinalIgnoreCase);
        }

        _worldAssetCacheDirty = false;
        return _worldAssetCache;
    }

    private IReadOnlyList<LauncherProjectInfo> GetLauncherProjectInfos()
    {
        long nowMs = Environment.TickCount64;
        if (!_launcherProjectCacheDirty && nowMs < _launcherProjectCacheNextRefreshMs)
            return _launcherProjectCache;

        _launcherProjectCache.Clear();
        if (!Directory.Exists(ProjectsRoot))
        {
            _launcherProjectCacheDirty = false;
            _launcherProjectCacheNextRefreshMs = nowMs + LauncherProjectRefreshIntervalMs;
            return _launcherProjectCache;
        }

        foreach (string projectDirectory in Directory.GetDirectories(ProjectsRoot))
        {
            var projectInfo = new DirectoryInfo(projectDirectory);
            string assetsDir = Path.Combine(projectDirectory, "Assets");
            DateTime lastModified = projectInfo.LastWriteTimeUtc;

            if (Directory.Exists(assetsDir))
            {
                foreach (string assetPath in Directory.EnumerateFiles(assetsDir, "*", SearchOption.AllDirectories))
                {
                    DateTime assetWriteTime = File.GetLastWriteTimeUtc(assetPath);
                    if (assetWriteTime > lastModified)
                        lastModified = assetWriteTime;
                }
            }

            _launcherProjectCache.Add(new LauncherProjectInfo(projectInfo.Name, projectInfo.FullName, lastModified.ToLocalTime()));
        }

        _launcherProjectCache.Sort(static (a, b) => b.LastModified.CompareTo(a.LastModified));
        _launcherProjectCacheDirty = false;
        _launcherProjectCacheNextRefreshMs = nowMs + LauncherProjectRefreshIntervalMs;
        return _launcherProjectCache;
    }
    private bool _triggerSaveLayoutPresetPopup;
    private string _layoutPresetNameBuffer = "";
    private bool _triggerAddLanguagePopup;
    private string _newLangCodeBuffer = "";
    private string _newLangDisplayNameBuffer = "";
    private int _newLangBaseLanguageIndex;
    private readonly Dictionary<string, WindowPlacement> _dockedWindowPlacements = new(StringComparer.Ordinal);
    private WindowPlacement? _dockedHostPlacement;

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
        string assetLabel = ActiveAssetPath != null
            ? Path.GetFileName(ActiveAssetPath)
            : $"{WorldManager.ActiveWorld?.Name ?? L10n.Tr("field_NoWorld")}{(IsEditingBlueprint ? ".blueprint" : ".verity")}";
        string dirtyMarker = _hasUnsavedChanges ? "*" : "";
        _device.SetWindowTitle(L10n.Tr("window_title_format", Version, projectName, assetLabel, dirtyMarker));
    }

    public void SetActiveAssetContext(string? assetPath, EditorAssetKind assetKind)
    {
        ActiveAssetPath = string.IsNullOrWhiteSpace(assetPath) ? null : Path.GetFullPath(assetPath);
        ActiveAssetKind = assetKind;
        if (assetKind == EditorAssetKind.World && ActiveAssetPath != null)
        {
            LastWorldAssetPath = ActiveAssetPath;
            ProjectSettings.LastOpenedWorldAssetPath = AssetPathUtility.Normalize(ActiveAssetPath);
        }
        UpdateWindowTitle();
    }

    public bool OpenBlueprintAsset(string path)
    {
        string normalized = Path.GetFullPath(path);
        if (!File.Exists(normalized))
            return false;

        if (!CanDeserializeScriptedAssets())
        {
            ShowOverlayMessage(L10n.Tr("msg_cannot_load_script_asset_compilation_errors"), 3.0f);
            CoreDebug.LogError("[Editor] Cannot open blueprint asset while user script compilation errors exist and no valid compiled assembly is available.");
            return false;
        }

        if (IsPlaying)
            ExitPlayMode();

        EditorSelection.EditingPolygonComponent = null;
        EditorSelection.ClearSelection();
        EditorSelection.SelectedAssetPath = normalized;

        var world = WorldManager.CreateOrReplaceWorld(Path.GetFileNameWithoutExtension(normalized));
        SceneSerializer.Deserialize(world, File.ReadAllText(normalized), _scriptCompiler?.CompiledAssembly, preserveEntityIds: true);
        BindWorldAssets(world);
        WorldManager.SetActiveWorld(world);
        SetActiveAssetContext(normalized, EditorAssetKind.Blueprint);
        ResetDirty();
        return true;
    }

    public bool SaveActiveBlueprint()
    {
        if (!IsEditingBlueprint || ActiveAssetPath == null || WorldManager.ActiveWorld == null)
            return false;

        string normalized = Path.GetFullPath(ActiveAssetPath);
        var refreshStates = CaptureBlueprintInstanceRefreshStates(normalized);

        File.WriteAllText(normalized, SceneSerializer.SerializeBlueprint(WorldManager.ActiveWorld));
        AssetPathUtility.EnsureMetaAndGetGuid(normalized);

        foreach (var state in refreshStates)
        {
            Entity? refreshed = SceneSerializer.RefreshBlueprintInstance(state.Root, state.Overrides, _scriptCompiler?.CompiledAssembly);
            if (refreshed != null)
                BindEntityAssetsRecursive(refreshed);
        }

        SetActiveAssetContext(normalized, EditorAssetKind.Blueprint);
        ResetDirty();
        ShowOverlayMessage(L10n.Tr("msg_blueprint_saved", Path.GetFileNameWithoutExtension(normalized)));
        return true;
    }

    public bool SaveActiveAssetForBuild()
    {
        if (IsPlaying)
        {
            ShowOverlayMessage(L10n.Tr("msg_cannot_save_world_play_mode"), 3.0f);
            return false;
        }

        if (IsEditingBlueprint)
            return SaveActiveBlueprint();

        NormalizeCameraOutputsForProjectSettings(WorldManager.ActiveWorld);
        GetWindow<ProjectWindow>()?.SaveActiveWorldAsAsset();
        return WorldManager.ActiveWorld != null;
    }

    public void NormalizeCameraOutputsForProjectSettings(World? world)
    {
        _ = world;
    }

    public bool EnsureStartupWorldForBuild()
    {
        if (AssetsPath == null)
            return false;

        if (BuildSettings.Worlds.Count > 0)
        {
            bool changed = false;
            if (BuildSettings.StartWorldIndex < 0 || BuildSettings.StartWorldIndex >= BuildSettings.Worlds.Count)
            {
                BuildSettings.StartWorldIndex = 0;
                changed = true;
            }

            string startupPath = Path.Combine(AssetsPath, BuildSettings.Worlds[BuildSettings.StartWorldIndex]);
            if (File.Exists(startupPath))
            {
                if (changed)
                    SaveBuildSettings();

                return true;
            }

            CoreDebug.Log($"[Build] Startup world is missing: {BuildSettings.Worlds[BuildSettings.StartWorldIndex]}. Falling back to the active world.");
        }

        var active = WorldManager.ActiveWorld;
        if (active == null)
        {
            CoreDebug.LogError("[Build] No active world. Open or create a world before building.");
            return false;
        }

        string? worldPath = null;
        if (ActiveAssetKind == EditorAssetKind.World &&
            !string.IsNullOrWhiteSpace(ActiveAssetPath) &&
            ActiveAssetPath.EndsWith(".verity", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(ActiveAssetPath))
        {
            worldPath = ActiveAssetPath;
        }

        worldPath ??= Directory.GetFiles(AssetsPath, $"{active.Name}.verity", SearchOption.AllDirectories).FirstOrDefault();
        if (worldPath == null)
        {
            GetWindow<ProjectWindow>()?.SaveActiveWorldAsAsset();
            string fallbackPath = Path.Combine(AssetsPath, $"{active.Name}.verity");
            if (File.Exists(fallbackPath))
                worldPath = fallbackPath;
        }

        if (worldPath == null)
        {
            CoreDebug.LogError($"[Build] Could not find saved world asset for '{active.Name}'.");
            return false;
        }

        string rel = Path.GetRelativePath(AssetsPath, worldPath).Replace("\\", "/");
        int existingIndex = BuildSettings.Worlds.FindIndex(path => string.Equals(path, rel, StringComparison.OrdinalIgnoreCase));
        bool added = existingIndex < 0;
        if (added)
        {
            BuildSettings.Worlds.Insert(0, rel);
            existingIndex = 0;
        }

        BuildSettings.StartWorldIndex = existingIndex;
        SaveBuildSettings();
        CoreDebug.Log(added ? $"[Build] Added startup world: {rel}" : $"[Build] Selected startup world: {rel}");
        return true;
    }

    public Entity? GetBlueprintDefaultParent()
    {
        if (!IsEditingBlueprint)
            return null;

        return WorldManager.ActiveWorld?.RootEntities.FirstOrDefault();
    }

    public void AttachToBlueprintDefaultParent(Entity? entity)
    {
        if (entity == null || !IsEditingBlueprint || entity.Transform.Parent != null)
            return;

        Entity? defaultParent = GetBlueprintDefaultParent();
        if (defaultParent == null || defaultParent == entity)
            return;

        entity.Transform.SetParent(defaultParent.Transform, false);
    }

    public bool TryGetBlueprintPreviewSprite(string path, out Sprite sprite)
    {
        sprite = default;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            JsonNode? root = JsonNode.Parse(File.ReadAllText(path));
            if (root is not JsonArray entitiesArray)
                return false;

            foreach (JsonNode? entityNode in entitiesArray)
            {
                if (entityNode?["Components"] is not JsonArray componentsArray)
                    continue;

                foreach (JsonNode? componentNode in componentsArray)
                {
                    if (!string.Equals((string?)componentNode?["Type"], "Verity.Graphics.SpriteRenderer", StringComparison.Ordinal))
                        continue;

                    JsonNode? spriteNode = componentNode?["Fields"]?["Sprite"];
                    if (spriteNode == null)
                        continue;

                    sprite = AssetPathUtility.FromSpriteJsonNode(spriteNode);
                    if (!string.IsNullOrWhiteSpace(sprite.Path))
                        return true;
                }
            }
        }
        catch
        {
        }

        return false;
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
        AutoSaveEditorState();
        _projectLock?.Dispose();
        _projectLock = null;
        BuildManagerWindow.ShutdownPreviewServer();
        ClearAssetRefreshTracking();
        InvalidateWorldAssetCache();
        CurrentProjectName = null;
        LastWorldAssetPath = null;
        InvalidateLauncherProjectCache();
        SceneSerializer.AssetRootPath = null;
        SetActiveAssetContext(null, EditorAssetKind.World);
        _dockLayoutPersistenceReady = false;
        ResetDirty();
    }

    public EditorApp(string title = "Verity", int width = 900, int height = 600)
    {
        L10n.Initialize();
        CoreDebug.OnLog += OnCoreLog;
        
        // Initialize default ProjectsRoot
        string docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        ProjectsRoot = Path.Combine(docsPath, "VerityProjects");
        
        LoadGlobalSettings();
        Directory.CreateDirectory(ProjectsRoot);

        _device = GraphicsDevice.Create(title, width, height);
        _device.SetSwapInterval(1);
        _imgui = new ImGuiController();
        
        string? fontPath = FindKoreanFont();
        _imgui.Initialize(_device, fontPath, this.ProjectSettings.EditorFontSize);
        UiRenderer.DefaultFontPath = string.Empty;
        UiRenderer.DefaultFontFamily = FindUiFontFamily();
        _imgui.SetMultiViewportEnabled(true);
        
        _shader = Shader2D.Create(_device);
        _textureManager = new TextureManager(_device);
        _renderPipeline = new RenderPipeline(_device, _shader, _textureManager);
        _renderPipeline.SetWhitePixel(_textureManager.CreateWhitePixel());
        DefaultSprites.Initialize(_textureManager);
        _worldCamera = new Camera();
        _worldCamera.SetViewportSize(width, height);
        _device.Window.OnSdlEvent += Verity.Input.Input.ProcessEvent;

        // Initialize Audio System
        AudioSystem.Initialize();

        ApplyEditorIcon();
    }

    public void ShowOverlayMessage(string text, float duration = 2.0f)
    {
        _overlayMessages.Add((text, duration));
    }

    private static void OnCoreLog(string msg, LogLevel level)
    {
        ConsoleWindow.Log(msg, level);
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
                    if (!string.IsNullOrWhiteSpace(settings.ProjectsRoot))
                    {
                        ProjectsRoot = settings.ProjectsRoot;
                        InvalidateLauncherProjectCache();
                    }
                    L10n.LoadLanguage(settings.Language);
                }
            } 
            else 
            {
                L10n.LoadLanguage(null);
            }
        } 
        catch (Exception e) 
        {
            CoreDebug.LogError($"[Launcher] Failed to load global settings: {e.Message}");
            L10n.LoadLanguage(null);
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

    private static string FindUiFontFamily()
    {
        string[] candidates =
        [
            "Malgun Gothic",
            "Noto Sans KR",
            "Noto Sans CJK KR",
            "Gulim",
            "Batang",
            "Segoe UI"
        ];

        foreach (string candidate in candidates)
        {
            try
            {
                using var family = new System.Drawing.FontFamily(candidate);
                return family.Name;
            }
            catch
            {
            }
        }

        return string.Empty;
    }

    private FileStream? _projectLock;

    private static void LogProjectOpenPhase(string projectName, string phase, Stopwatch timer, ref long lastElapsedMs)
    {
        long elapsedMs = timer.ElapsedMilliseconds;
        CoreDebug.Log($"[ProjectOpen:{projectName}] {phase}: {elapsedMs - lastElapsedMs} ms");
        lastElapsedMs = elapsedMs;
    }

    private bool TryRestoreLastOpenedWorld()
    {
        if (ProjectPath == null || string.IsNullOrWhiteSpace(ProjectSettings.LastOpenedWorldAssetPath))
            return false;

        string lastWorldPath = AssetPathUtility.ResolvePath(ProjectPath, ProjectSettings.LastOpenedWorldAssetPath);
        if (!File.Exists(lastWorldPath))
            return false;

        if (!CanDeserializeScriptedAssets())
        {
            ShowOverlayMessage(L10n.Tr("msg_cannot_load_script_asset_compilation_errors"), 3.0f);
            CoreDebug.LogError("[Editor] Skipping last world restore because user script compilation errors exist and no valid compiled assembly is available.");
            return false;
        }

        GetWindow<ProjectWindow>()?.LoadWorldByPath(lastWorldPath);
        return WorldManager.ActiveWorld != null;
    }

    private bool TryLoadMostRecentWorld()
    {
        if (AssetsPath == null || !Directory.Exists(AssetsPath))
            return false;

        string? newestWorldPath = null;
        DateTime newestWriteTimeUtc = DateTime.MinValue;

        foreach (string path in Directory.EnumerateFiles(AssetsPath, "*.verity", SearchOption.AllDirectories))
        {
            DateTime writeTimeUtc = File.GetLastWriteTimeUtc(path);
            if (writeTimeUtc <= newestWriteTimeUtc)
                continue;

            newestWriteTimeUtc = writeTimeUtc;
            newestWorldPath = path;
        }

        if (string.IsNullOrWhiteSpace(newestWorldPath))
            return false;

        if (!CanDeserializeScriptedAssets())
        {
            ShowOverlayMessage(L10n.Tr("msg_cannot_load_script_asset_compilation_errors"), 3.0f);
            CoreDebug.LogError("[Editor] Skipping world auto-load because user script compilation errors exist and no valid compiled assembly is available.");
            return false;
        }

        GetWindow<ProjectWindow>()?.LoadWorldByPath(newestWorldPath);
        return WorldManager.ActiveWorld != null;
    }

    public bool CanDeserializeScriptedAssets()
    {
        return !HasScriptCompilationErrors || _scriptCompiler?.CompiledAssembly != null;
    }

    private void CreateDefaultStartupWorld()
    {
        if (AssetsPath == null)
            return;

        var world = WorldManager.CreateOrReplaceWorld("Main");
        var cam = world.CreateEntity(L10n.Tr("creation_default_main_camera"));
        cam.Tag = CameraSelection.MainCameraTag;
        cam.AddComponent<Camera>();
        cam.AddComponent<CameraOutput>();
        WorldManager.SetActiveWorld(world);

        string mainWorldPath = Path.Combine(AssetsPath, "Main.verity");
        try
        {
            File.WriteAllText(mainWorldPath, Verity.Core.Serialization.SceneSerializer.Serialize(world));
        }
        catch
        {
        }

        SetActiveAssetContext(mainWorldPath, EditorAssetKind.World);
        ResetDirty();
    }

    public bool OpenProject(string projectName)
    {
        var openTimer = Stopwatch.StartNew();
        long lastPhaseMs = 0;

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
        LogProjectOpenPhase(projectName, "Acquire lock", openTimer, ref lastPhaseMs);

        _device.SetSize(1600, 900);
        _worldCamera.SetViewportSize(1600, 900);
        UpdateWindowTitle();

        EnsureProjectFileExists(projectPath, projectName);
        _dockLayoutPersistenceReady = false;
        LogProjectOpenPhase(projectName, "Prepare window and project files", openTimer, ref lastPhaseMs);

        Verity.Filter.FilterManager.SavePath = Path.Combine(AssetsPath!, "Filters.json");
        Verity.Filter.FilterManager.Load();
        LoadProjectSettings();
        if (!LoadProjectDockLayout())
            ResetEditorLayout();
        LoadBuildSettings();
        LuaLanguageServerSupport.EnsureProjectSupport(ProjectPath, _scriptCompiler?.GetAllAddableComponentTypes());
        RenderPipeline.BaseAssetsPath = ProjectPath;
        UiSystem.AssetsRoot = ProjectPath;
        SceneSerializer.AssetRootPath = ProjectPath;
        LogProjectOpenPhase(projectName, "Load settings and layout", openTimer, ref lastPhaseMs);
        
        InitializeAssetWatcher(AssetsPath!);
        LogProjectOpenPhase(projectName, "Initialize asset watcher", openTimer, ref lastPhaseMs);

        LuaScriptManager.HotReloadRequested -= OnLuaScriptsHotReloadRequested;
        LuaScriptManager.HotReloadRequested += OnLuaScriptsHotReloadRequested;
        LuaScriptManager.RefreshBindings(_scriptCompiler?.CompiledAssembly, ProjectPath);

        if (_scriptCompiler != null)
        {
            _scriptCompiler.OnCompilationFinished -= OnScriptsCompiled;
            _scriptCompiler.Dispose();
        }

        _scriptCompiler = new ScriptCompiler(AssetsPath!);
        _scriptCompiler.OnCompilationFinished += OnScriptsCompiled;
        _scriptCompiler.Compile();
        LogProjectOpenPhase(projectName, "Compile user scripts", openTimer, ref lastPhaseMs);

        if (TryRestoreLastOpenedWorld())
        {
            LogProjectOpenPhase(projectName, "Restore last opened world", openTimer, ref lastPhaseMs);
        }
        else if (TryLoadMostRecentWorld())
        {
            LogProjectOpenPhase(projectName, "Scan and load most recent world", openTimer, ref lastPhaseMs);
        }
        else 
        {
            CreateDefaultStartupWorld();
            LogProjectOpenPhase(projectName, "Create default world", openTimer, ref lastPhaseMs);
        }

        CoreDebug.Log($"[ProjectOpen:{projectName}] Total: {openTimer.ElapsedMilliseconds} ms");
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
        AutoSaveEditorState();
        _projectLock?.Dispose();
        _projectLock = null;
        _dockLayoutPersistenceReady = false;
        _device.Window.Close();
    }

    private static readonly JsonSerializerOptions _projectSettingsOptions = new() 
    { 
        WriteIndented = true, 
        Converters = { new Vector2Converter(), new Verity.Core.Serialization.ColorConverter(), new UiAssetConverter(), new UiAssetReferenceConverter(), new UiRoleBindingConverter() }
    };

    private void LoadProjectSettings()
    {
        if (AssetsPath == null) return;
        string path = Path.Combine(AssetsPath, "ProjectSettings.json");
        bool shouldSave = false;
        if (File.Exists(path)) {
            try { 
                var json = File.ReadAllText(path); 
                var settings = JsonSerializer.Deserialize<ProjectSettings>(json, _projectSettingsOptions);
                this.ProjectSettings = settings ?? new();
                SortingLayer.SyncWithSettings(this.ProjectSettings.SortingLayers);
                UiSystem.ProjectSettings = this.ProjectSettings;
            }
              catch (Exception e) { 
                  CoreDebug.LogError($"[Project] Failed to load settings: {e.Message}");
                  this.ProjectSettings = new(); 
                  UiSystem.ProjectSettings = this.ProjectSettings;
                  shouldSave = true;
              }
        } else { this.ProjectSettings = new(); UiSystem.ProjectSettings = this.ProjectSettings; shouldSave = true; }
  
        if (ProjectSettings.EditorDockLayout == null)
            ProjectSettings.EditorDockLayout = new EditorDockLayoutSettings();

        try
        {
            shouldSave |= EnsureDefaultUiFontAsset();
        }
        catch (Exception e)
        {
            CoreDebug.LogError($"[Font] Default UI font initialization failed: {e.Message}");
        }
        ApplyProjectUiFontDefaults();
        if (shouldSave)
            SaveProjectSettings();
    }

    public void SaveProjectSettings()
    {
        if (AssetsPath == null) return;
        PersistProjectDockLayoutState();
        ApplyProjectUiFontDefaults();
        string path = Path.Combine(AssetsPath, "ProjectSettings.json");
        try {
            var json = JsonSerializer.Serialize(this.ProjectSettings, _projectSettingsOptions);
            File.WriteAllText(path, json);
        } catch (Exception e) {
            CoreDebug.LogError($"[Project] Failed to save settings: {e.Message}");
        }
    }

    private void ApplyProjectUiFontDefaults()
    {
        UiRenderer.DefaultFontPath = ProjectSettings.DefaultUiFontPath;
        UiRenderer.DefaultFontFamily = string.IsNullOrWhiteSpace(ProjectSettings.DefaultUiFontPath)
            ? FindUiFontFamily()
            : string.Empty;
    }

    private bool EnsureDefaultUiFontAsset()
    {
        if (AssetsPath == null)
            return false;

        string resolvedExisting = AssetPathUtility.ResolvePath(ProjectPath ?? AssetsPath, ProjectSettings.DefaultUiFontPath, ProjectSettings.DefaultUiFontGuid);
        if (!string.IsNullOrWhiteSpace(resolvedExisting) &&
            File.Exists(resolvedExisting) &&
            SdfFontAsset.IsFontAssetPath(resolvedExisting))
        {
            if (string.IsNullOrWhiteSpace(ProjectSettings.DefaultUiFontPath))
                ProjectSettings.DefaultUiFontPath = AssetPathUtility.Normalize(resolvedExisting);
            if (string.IsNullOrWhiteSpace(ProjectSettings.DefaultUiFontGuid))
                ProjectSettings.DefaultUiFontGuid = AssetPathUtility.EnsureMetaAndGetGuid(resolvedExisting);
            return false;
        }

        string bundledAssetPath = FindBundledDefaultUiFontAssetPath();
        if (string.IsNullOrWhiteSpace(bundledAssetPath) || !File.Exists(bundledAssetPath))
        {
            CoreDebug.LogError("[Font] Bundled default UI font asset could not be found.");
            return false;
        }

        string destinationDirectory = Path.Combine(AssetsPath, "Fonts", "BuiltIn");
        string destinationAssetPath = Path.Combine(destinationDirectory, Path.GetFileName(bundledAssetPath));

        Directory.CreateDirectory(destinationDirectory);
        File.Copy(bundledAssetPath, destinationAssetPath, true);
        AssetPathUtility.EnsureMetaAndGetGuid(destinationAssetPath);

        var asset = SdfFontAsset.Load(destinationAssetPath);
        foreach (var atlasPage in asset.AtlasPages)
        {
            string sourceAtlasPath = Path.Combine(Path.GetDirectoryName(bundledAssetPath)!, atlasPage.Path);
            string destinationAtlasPath = Path.Combine(destinationDirectory, atlasPage.Path);
            if (!File.Exists(sourceAtlasPath))
                continue;

            File.Copy(sourceAtlasPath, destinationAtlasPath, true);
            AssetPathUtility.EnsureMetaAndGetGuid(destinationAtlasPath);
        }

        ProjectSettings.DefaultUiFontPath = AssetPathUtility.Normalize(destinationAssetPath);
        ProjectSettings.DefaultUiFontGuid = AssetPathUtility.EnsureMetaAndGetGuid(destinationAssetPath);
        CoreDebug.Log($"[Font] Installed bundled default UI font: {ProjectSettings.DefaultUiFontPath}");
        return true;
    }

    private static string FindBundledDefaultUiFontAssetPath()
    {
        string[] searchPaths =
        [
            Path.Combine(AppContext.BaseDirectory, "EditorResources", "Fonts", "DefaultUI.fontasset"),
            Path.Combine(AppContext.BaseDirectory, "..", "EditorResources", "Fonts", "DefaultUI.fontasset"),
            Path.Combine(Directory.GetCurrentDirectory(), "Editor", "Verity.Editor", "EditorResources", "Fonts", "DefaultUI.fontasset")
        ];

        foreach (string path in searchPaths)
        {
            string fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return string.Empty;
    }

    private void PersistProjectDockLayoutState()
    {
        if (CurrentProjectName == null || _windowMode != EditorWindowMode.Docked || !_dockLayoutPersistenceReady)
            return;

        ProjectSettings.EditorDockLayout = CaptureCurrentDockLayoutState();
    }

    private EditorDockLayoutSettings CaptureCurrentDockLayoutState()
    {
        return new EditorDockLayoutSettings
        {
            Ini = _imgui.SaveLayout(),
            OpenWindowIds = _windows.Where(static win => win.IsOpen).Select(static win => win.WindowId).ToList()
        };
    }

    private bool ApplyDockLayoutState(EditorDockLayoutSettings? state)
    {
        if (state == null)
            return false;

        var openWindowIds = state.OpenWindowIds ?? [];
        if (openWindowIds.Count > 0)
        {
            var openWindowIdSet = new HashSet<string>(openWindowIds, StringComparer.Ordinal);
            foreach (var window in _windows)
                window.IsOpen = openWindowIdSet.Contains(window.WindowId);
        }

        string ini = state.Ini ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ini))
            return openWindowIds.Count > 0;

        _imgui.ClearLayout();
        _imgui.LoadLayout(ini);
        _pendingLayoutReset = false;
        _loadedDockLayoutFromSettings = true;
        return true;
    }

    private void AutoSaveEditorState()
    {
        if (CurrentProjectName == null)
            return;

        PersistProjectDockLayoutState();
        SaveProjectSettings();
    }

    private IEnumerable<string> GetLayoutPresetFiles()
    {
        if (!Directory.Exists(LayoutPresetsRoot))
            return Enumerable.Empty<string>();

        return Directory.GetFiles(LayoutPresetsRoot, "*.layout.json", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase);
    }

    private static string SanitizeLayoutPresetName(string name)
    {
        string safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "Layout" : safe;
    }

    private bool SaveLayoutPreset(string name)
    {
        if (CurrentProjectName == null || _windowMode != EditorWindowMode.Docked)
            return false;

        Directory.CreateDirectory(LayoutPresetsRoot);
        string fileName = SanitizeLayoutPresetName(name) + ".layout.json";
        string presetPath = Path.Combine(LayoutPresetsRoot, fileName);
        try
        {
            var state = CaptureCurrentDockLayoutState();
            var json = JsonSerializer.Serialize(state, _projectSettingsOptions);
            File.WriteAllText(presetPath, json);
            ShowOverlayMessage(L10n.Tr("msg_layout_saved", Path.GetFileNameWithoutExtension(fileName)));
            return true;
        }
        catch (Exception e)
        {
            CoreDebug.LogError($"[Layout] Failed to save preset: {e.Message}");
            return false;
        }
    }

    private bool LoadLayoutPreset(string presetPath)
    {
        if (!File.Exists(presetPath))
            return false;

        try
        {
            string json = File.ReadAllText(presetPath);
            var state = JsonSerializer.Deserialize<EditorDockLayoutSettings>(json, _projectSettingsOptions);
            if (state == null)
                return false;

            SetWindowMode(EditorWindowMode.Docked);
            if (!ApplyDockLayoutState(state))
                return false;

            ProjectSettings.EditorDockLayout = state;
            SaveProjectSettings();
            ShowOverlayMessage(L10n.Tr("msg_layout_loaded", Path.GetFileNameWithoutExtension(presetPath)));
            return true;
        }
        catch (Exception e)
        {
            CoreDebug.LogError($"[Layout] Failed to load preset: {e.Message}");
            return false;
        }
    }

    private bool LoadProjectDockLayout()
    {
        if (ProjectSettings.EditorDockLayout == null)
            return false;

        SetWindowMode(EditorWindowMode.Docked);
        bool loaded = ApplyDockLayoutState(ProjectSettings.EditorDockLayout);
        if (loaded)
            ShowOverlayMessage(L10n.Tr("msg_project_layout_loaded"));
        return loaded;
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

    private string GetUndoScopeKey()
    {
        if (!string.IsNullOrWhiteSpace(ActiveAssetPath))
            return $"{ActiveAssetKind}:{Path.GetFullPath(ActiveAssetPath)}";

        string worldName = WorldManager.ActiveWorld?.Name ?? "NoWorld";
        return $"{ActiveAssetKind}:{worldName}";
    }

    public void RecordUndo() { var world = WorldManager.ActiveWorld; if (world != null) { _undoSystem.Record(GetUndoScopeKey(), world, this.ProjectSettings, this.BuildSettings, CaptureEditorUndoState()); MarkAsDirty(); } }
    public void BeginUndoAction() { var world = WorldManager.ActiveWorld; if (world != null) { _undoSystem.BeginContinuousAction(GetUndoScopeKey(), world, this.ProjectSettings, this.BuildSettings, CaptureEditorUndoState()); MarkAsDirty(); } }
    public void EndUndoAction() { var world = WorldManager.ActiveWorld; if (world != null) { _undoSystem.EndContinuousAction(GetUndoScopeKey(), world, this.ProjectSettings, this.BuildSettings, CaptureEditorUndoState()); MarkAsDirty(); } }
    
    public void Undo() 
    { 
        var world = WorldManager.ActiveWorld; 
        if (world == null) return; 
        var state = _undoSystem.Undo(GetUndoScopeKey(), world, this.ProjectSettings, this.BuildSettings, CaptureEditorUndoState());
        if (state != null) RestoreState(state);
        UpdateWindowTitle();
    }

    public void Redo() 
    { 
        var world = WorldManager.ActiveWorld; 
        if (world == null) return; 
        var state = _undoSystem.Redo(GetUndoScopeKey(), world, this.ProjectSettings, this.BuildSettings, CaptureEditorUndoState());
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
            UiSystem.ProjectSettings = this.ProjectSettings;
            var bs = JsonSerializer.Deserialize<BuildSettings>(state.BuildSettingsJson);
            if (bs != null) this.BuildSettings = bs;
        } catch { }
        BindWorldAssets(world);
        if (selectedId.HasValue)
            EditorSelection.SelectedEntity = world.GetAllEntities().FirstOrDefault(e => e.Id == selectedId.Value);
        RestoreEditorUndoState(state.EditorStateJson);
        UpdateWindowTitle();
    }

    private string CaptureEditorUndoState()
    {
        var state = new EditorUndoState
        {
            WorldView = GetWindow<WorldViewWindow>()?.CaptureUndoState(),
            SelectedAssetPath = EditorSelection.SelectedAssetPath,
            SelectedTileTool = EditorSelection.SelectedTool,
            TileBrushSize = EditorSelection.TileBrushSize,
            TileBrushShape = EditorSelection.TileBrushShape,
            EditingPolygon = CaptureEditingPolygonUndoState()
        };

        return JsonSerializer.Serialize(state);
    }

    private EditingPolygonUndoState? CaptureEditingPolygonUndoState()
    {
        var component = EditorSelection.EditingPolygonComponent;
        if (component == null)
            return null;

        Type type = component.GetType();
        return new EditingPolygonUndoState
        {
            EntityId = component.Owner.Id,
            ComponentTypeName = type.AssemblyQualifiedName ?? type.FullName ?? type.Name
        };
    }

    private void RestoreEditorUndoState(string? editorStateJson)
    {
        if (string.IsNullOrWhiteSpace(editorStateJson))
            return;

        try
        {
            var state = JsonSerializer.Deserialize<EditorUndoState>(editorStateJson);
            if (state == null)
                return;

            GetWindow<WorldViewWindow>()?.RestoreUndoState(state.WorldView);

            EditorSelection.SelectedTool = state.SelectedTileTool;
            EditorSelection.TileBrushSize = Math.Max(1, state.TileBrushSize);
            EditorSelection.TileBrushShape = state.TileBrushShape;
            GetWindow<TilePaletteWindow>()?.RestoreUndoState(state.SelectedAssetPath);
            EditorSelection.EditingPolygonComponent = ResolveEditingPolygonUndoState(state.EditingPolygon);
        }
        catch
        {
        }
    }

    private Component? ResolveEditingPolygonUndoState(EditingPolygonUndoState? state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.ComponentTypeName))
            return null;

        var world = WorldManager.ActiveWorld;
        if (world == null)
            return null;

        var entity = world.GetAllEntities().FirstOrDefault(candidate => candidate.Id == state.EntityId);
        if (entity == null)
            return null;

        var componentType = Type.GetType(state.ComponentTypeName, throwOnError: false);
        if (componentType == null)
            return null;

        return entity.GetAllComponents().FirstOrDefault(component => componentType.IsInstanceOfType(component));
    }

    public void AddWindow(EditorWindow window)
    {
        window.SetWindowId(window.GetType().Name);
        _windows.Add(window);
    }
    public T? GetWindow<T>() where T : EditorWindow => _windows.OfType<T>().FirstOrDefault();

    public void OpenWindow(EditorWindow window, bool focus = true)
    {
        window.IsOpen = true;
        if (focus)
            _pendingFocusedWindow = window;
    }

    public T? OpenWindow<T>(bool focus = true) where T : EditorWindow
    {
        var window = GetWindow<T>();
        if (window != null)
            OpenWindow(window, focus);
        return window;
    }

    private void CaptureDockedHostPlacement()
    {
        var (x, y) = _device.GetWindowPosition();
        _dockedHostPlacement = new WindowPlacement(
            new Vector2(x, y),
            new Vector2(_device.Window.GetWidth(), _device.Window.GetHeight()));
    }

    private void RememberDockedWindowPlacement(EditorWindow window)
    {
        if (string.IsNullOrWhiteSpace(window.WindowId))
            return;

        Vector2 size = ImGui.GetWindowSize();
        if (size.X <= 1f || size.Y <= 1f)
            return;

        _dockedWindowPlacements[window.WindowId] = new WindowPlacement(ImGui.GetWindowPos(), size);
    }

    private bool TryGetDockedWindowPlacement(EditorWindow window, out WindowPlacement placement)
    {
        if (!string.IsNullOrWhiteSpace(window.WindowId) &&
            _dockedWindowPlacements.TryGetValue(window.WindowId, out placement) &&
            placement.Size.X > 1f &&
            placement.Size.Y > 1f)
        {
            return true;
        }

        placement = default;
        return false;
    }

    private void SetWindowMode(EditorWindowMode mode)
    {
        if (_windowMode == mode)
            return;

        if (_fullscreenWindow != null)
            ExitFullscreen();

        if (_windowMode == EditorWindowMode.Docked)
        {
            PersistProjectDockLayoutState();
            CaptureDockedHostPlacement();
        }

        _windowMode = mode;
        _imgui.SetMultiViewportEnabled(true, separateAllWindows: mode == EditorWindowMode.Detached);
        // Multi-viewport OpenGL can block once per platform window when vsync is enabled.
        // In detached mode, disable swap interval to avoid N-window frame pacing stalls.
        _device.SetSwapInterval(mode == EditorWindowMode.Detached ? 0 : 1);
        if (CurrentProjectName != null)
        {
            if (mode == EditorWindowMode.Detached)
            {
                if (GetWindow<ProjectWindow>() is { } projectWindow &&
                    TryGetDockedWindowPlacement(projectWindow, out var projectPlacement))
                {
                    _device.SetWindowPosition((int)projectPlacement.Position.X, (int)projectPlacement.Position.Y);
                    _device.SetSize(
                        Math.Max((int)projectPlacement.Size.X, 420),
                        Math.Max((int)projectPlacement.Size.Y, 360));
                }
                else if (_dockedHostPlacement is WindowPlacement hostPlacement)
                {
                    _device.SetWindowPosition((int)hostPlacement.Position.X, (int)hostPlacement.Position.Y);
                    _device.SetSize((int)hostPlacement.Size.X, (int)hostPlacement.Size.Y);
                }
                else
                {
                    _device.SetSize(560, 900);
                }
            }
            else
            {
                if (_dockedHostPlacement is WindowPlacement hostPlacement)
                {
                    _device.SetWindowPosition((int)hostPlacement.Position.X, (int)hostPlacement.Position.Y);
                    _device.SetSize((int)hostPlacement.Size.X, (int)hostPlacement.Size.Y);
                }
                else
                {
                    _device.SetSize(1600, 900);
                }
            }
        }
        ResetEditorLayout();
    }

    private void ResetEditorLayout()
    {
        foreach (var win in _windows)
            win.RefreshTitle();

        _pendingLayoutReset = true;
        _pendingDetachedLayoutReset = true;
        _loadedDockLayoutFromSettings = false;
    }

    private string GetWindowName<T>() where T : EditorWindow
    {
        return GetWindow<T>()?.ImGuiName ?? typeof(T).Name;
    }

    private void RequestSaveLayoutPreset()
    {
        string baseName = string.IsNullOrWhiteSpace(CurrentProjectName) ? "Layout" : $"{CurrentProjectName} Layout";
        _layoutPresetNameBuffer = baseName;
        _triggerSaveLayoutPresetPopup = true;
    }

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

    private void OnScriptsCompiled() 
    {
        _pendingMainThreadActions.Enqueue(HandleScriptsCompiledOnMainThread);
    }

    private void HandleScriptsCompiledOnMainThread()
    {
        var world = WorldManager.ActiveWorld; 

        LuaScriptManager.RefreshBindings(_scriptCompiler?.CompiledAssembly, ProjectPath);
        LuaLanguageServerSupport.EnsureProjectSupport(ProjectPath, _scriptCompiler?.GetAllAddableComponentTypes());

        if (world == null) return;

        if (IsPlaying)
        {
            CoreDebug.Log("[Editor] Scripts compiled during Play Mode. Hot-reload will occur after exiting Play Mode.");
            return;
        }

        CoreDebug.Log("[Editor] Scripts compiled. Performing hot-reload of active world...");

        if (ReloadActiveWorldPreservingState("C# script compilation", autoSaveAfterReload: true))
        {
            ShowOverlayMessage(L10n.Tr("msg_scripts_reloaded"));
            CoreDebug.Log("[Editor] Hot-reload successful.");
        }
    }

    private void OnLuaScriptsHotReloadRequested(IReadOnlyList<string> changedPaths)
    {
        lock (_assetInvalidationLock)
        {
            foreach (string path in changedPaths)
            {
                _pendingLuaHotReloadPaths.Add(path);
                _pendingLuaHotReloadDeadlines[path] = Environment.TickCount64 + AssetRefreshDebounceMs;
            }
        }
    }

    private bool ReloadActiveWorldPreservingState(string reason, bool autoSaveAfterReload)
    {
        var world = WorldManager.ActiveWorld;
        if (world == null)
            return false;

        Guid? selectedId = EditorSelection.SelectedEntity?.Id;

        try
        {
            string json = Verity.Core.Serialization.SceneSerializer.Serialize(world);
            world.ClearAllEntities();
            Verity.Core.Serialization.SceneSerializer.Deserialize(world, json, _scriptCompiler?.CompiledAssembly);
            BindWorldAssets(world);

            if (selectedId.HasValue)
                EditorSelection.SelectedEntity = world.GetAllEntities().FirstOrDefault(e => e.Id == selectedId.Value);

            if (autoSaveAfterReload)
            {
                GetWindow<Windows.ProjectWindow>()?.SaveActiveWorldAsAsset();
                ResetDirty();
            }

            return true;
        }
        catch (Exception e)
        {
            CoreDebug.LogError($"[Editor] Critical error during {reason}: {e.Message}");
            return false;
        }
    }

    public void EnterPlayMode() 
    { 
        if (WorldManager.ActiveWorld == null || IsPlaying) return; 

        if (_scriptCompiler != null && !_scriptCompiler.Compile())
        {
            ShowOverlayMessage(L10n.Tr("msg_cannot_play_compilation_errors"), 3.0f);
            CoreDebug.LogError("[Editor] Cannot enter Play Mode because user script compilation failed.");
            return;
        }

        if (HasScriptCompilationErrors)
        {
            ShowOverlayMessage(L10n.Tr("msg_cannot_play_compilation_errors"), 3.0f);
            CoreDebug.LogError("[Editor] Cannot enter Play Mode while user script compilation errors exist.");
            return;
        }
        
        // Pause compiler during play mode
        if (_scriptCompiler != null) _scriptCompiler.IsPaused = true;

        _snapshot = WorldSnapshot.Capture(WorldManager.ActiveWorld); 
        Time.Reset(); 
        LuaScriptManager.Dispose();
        LuaScriptManager.Initialize(ProjectPath, _scriptCompiler?.CompiledAssembly);
        BindWorldAssets(WorldManager.ActiveWorld);
        _gameLoop = new GameLoop { ProjectSettings = this.ProjectSettings }; 
        IsPlaying = true; 
    }
    
    public void ExitPlayMode() 
    { 
        if (!IsPlaying || WorldManager.ActiveWorld == null) return; 
        EditorSelection.SelectedEntity = null; 
        
        LuaScriptManager.Dispose();
        _snapshot?.Restore(WorldManager.ActiveWorld, _scriptCompiler?.CompiledAssembly); 
        BindWorldAssets(WorldManager.ActiveWorld);
        _snapshot = null; 
        LuaScriptManager.SuspendHotReloadEvents = false;
        LuaScriptManager.Initialize(ProjectPath, _scriptCompiler?.CompiledAssembly);
        _gameLoop = null; 
        IsPlaying = false; 
        Verity.Input.Input.Enabled = true; 

        // Resume compiler after play mode
        if (_scriptCompiler != null) _scriptCompiler.IsPaused = false;

        if (_pendingLuaHotReloadPaths.Count > 0)
        {
            lock (_assetInvalidationLock)
            {
                foreach (string path in _pendingLuaHotReloadPaths.ToArray())
                    _pendingLuaHotReloadDeadlines[path] = Environment.TickCount64;
            }
        }
    }

    public void Run()
    {
        _stopwatch.Start();
        long lastTicks = _stopwatch.ElapsedTicks;
        while (!_device.ShouldClose)
        {
            long frameStart = Stopwatch.GetTimestamp();
            bool profilerOpen = GetWindow<ProfilerWindow>()?.IsOpen == true;
            _profiler.BeginFrame(profilerOpen);
            RuntimeProfiler.Enabled = _profiler.IsCollectingFrame;
            long currentTicks = _stopwatch.ElapsedTicks;
            float deltaTime = (float)(currentTicks - lastTicks) / Stopwatch.Frequency;
            lastTicks = currentTicks;
            Time.AdvanceFrame();
            if (!IsPlaying) { Time.DeltaTime = deltaTime; Time.TotalTime += deltaTime; }
            Verity.Input.Input.Enabled = _isScreenFocused;

            long stageStart = Stopwatch.GetTimestamp();
            _device.PollEvents();
            _profiler.RecordFrameStage("Poll Events", Stopwatch.GetElapsedTime(stageStart).TotalMilliseconds);

            stageStart = Stopwatch.GetTimestamp();
            ProcessPendingAssetInvalidations();
            _profiler.RecordFrameStage("Asset Refresh", Stopwatch.GetElapsedTime(stageStart).TotalMilliseconds);

            stageStart = Stopwatch.GetTimestamp();
            ProcessPendingMainThreadActions();
            _profiler.RecordFrameStage("Main Thread Actions", Stopwatch.GetElapsedTime(stageStart).TotalMilliseconds);

            // Handle window close button
            if (_device.Window.ShouldClose && _hasUnsavedChanges && !_showExitConfirmPopup)
            {
                _device.Window.CancelClose();
                RequestExit();
            }

            stageStart = Stopwatch.GetTimestamp();
            LastPlayLogicTicksThisFrame = IsPlaying && _gameLoop != null
                ? _gameLoop.TickLogic(deltaTime)
                : 0;
            _profiler.RecordFrameStage("Play Logic", Stopwatch.GetElapsedTime(stageStart).TotalMilliseconds);

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
            _device.Clear(System.Drawing.Color.FromArgb(255, 30, 30, 30));
            stageStart = Stopwatch.GetTimestamp();
            _imgui.BeginFrame();
            if (CurrentProjectName == null) DrawLauncher();
            else {
                _isScreenFocused = false;
                if (_windowMode == EditorWindowMode.Docked)
                    DrawDockedWorkspace();
                else
                    DrawDetachedWorkspace();
            }
            DrawGlobalPopups();
            DrawOverlays(deltaTime);

            if (_fullscreenWindow != null)
                RenderFullscreenOverlay(_fullscreenWindow);
            if (CurrentProjectName != null)
                _dockLayoutPersistenceReady = true;
            _imgui.EndFrame();
            _profiler.RecordFrameStage("Editor UI", Stopwatch.GetElapsedTime(stageStart).TotalMilliseconds);
            CoreDebug.ClearDrawCommands();
            stageStart = Stopwatch.GetTimestamp();
            _device.SwapBuffers();
            _profiler.RecordFrameStage("Swap Buffers", Stopwatch.GetElapsedTime(stageStart).TotalMilliseconds);
            _profiler.EndFrame(Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds);
        }
    }

    public void RequestDeleteFilter(FilterType filter)
    {
        _filterToDelete = filter;
        _triggerDeletePopup = true;
    }

    private unsafe void DrawGlobalPopups()
    {
        if (_triggerDeletePopup) { ImGui.OpenPopup("DeleteFilterConfirm"); _triggerDeletePopup = false; }
        if (_showExitConfirmPopup) { ImGui.OpenPopup("ExitConfirm"); _showExitConfirmPopup = false; }
        if (_showCloseProjectConfirmPopup) { ImGui.OpenPopup("CloseProjectConfirm"); _showCloseProjectConfirmPopup = false; }
        if (_triggerSaveLayoutPresetPopup) { ImGui.OpenPopup("SaveLayoutPreset"); _triggerSaveLayoutPresetPopup = false; }
        if (_triggerAddLanguagePopup) { ImGui.OpenPopup("AddLanguage"); _triggerAddLanguagePopup = false; }

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
                AutoSaveEditorState(); 
                _pendingExitAction?.Invoke(); 
                ImGui.CloseCurrentPopup(); 
            }
            ImGui.SameLine();
            if (ImGui.Button(L10n.Tr("btn_exit_without_save"), btnSize)) { AutoSaveEditorState(); _pendingExitAction?.Invoke(); ImGui.CloseCurrentPopup(); }
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
                AutoSaveEditorState(); 
                _pendingExitAction?.Invoke(); 
                ImGui.CloseCurrentPopup(); 
            }
            ImGui.SameLine();
            if (ImGui.Button(L10n.Tr("btn_close_without_save"), btnSize)) { AutoSaveEditorState(); _pendingExitAction?.Invoke(); ImGui.CloseCurrentPopup(); }
            ImGui.SameLine();
            if (ImGui.Button(L10n.Tr("btn_cancel"), btnSize)) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        if (ImGui.BeginPopupModal("SaveLayoutPreset", (bool*)null, modalFlags)) {
        ImGui.Text(L10n.Tr("msg_save_layout_preset"));
            ImGui.Separator(); ImGui.Dummy(new Vector2(0, 10));
            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();
        ImGui.InputText(L10n.Tr("label_name"), ref _layoutPresetNameBuffer, 128);
            ImGui.Separator(); ImGui.Dummy(new Vector2(0, 10));
        if (ImGui.Button(L10n.Tr("btn_save"), btnSize)) {
                if (SaveLayoutPreset(_layoutPresetNameBuffer))
                    ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button(L10n.Tr("btn_cancel"), btnSize))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        if (ImGui.BeginPopupModal("AddLanguage", (bool*)null, modalFlags)) {
        ImGui.Text(L10n.Tr("msg_add_language"));
            ImGui.Separator(); ImGui.Dummy(new Vector2(0, 10));
            if (ImGui.IsWindowAppearing())
                ImGui.SetKeyboardFocusHere();
        ImGui.InputText(L10n.Tr("label_language_code"), ref _newLangCodeBuffer, 8);
        ImGui.InputText(L10n.Tr("label_display_name"), ref _newLangDisplayNameBuffer, 64);

            string[] baseLangNames = ["English", "한국어"];
        string baseLangLabel = L10n.Tr("label_base_language");
            if (ImGui.Combo(baseLangLabel, ref _newLangBaseLanguageIndex, baseLangNames, baseLangNames.Length))
            { }

            ImGui.Separator(); ImGui.Dummy(new Vector2(0, 10));
        if (ImGui.Button(L10n.Tr("btn_create"), btnSize)) {
                string baseCode = _newLangBaseLanguageIndex == 1 ? "ko" : "en";
                string code = _newLangCodeBuffer.Trim().ToLowerInvariant();
                string name = _newLangDisplayNameBuffer.Trim();
                if (L10n.AddLanguage(code, name, baseCode)) {
                    _newLangCodeBuffer = "";
                    _newLangDisplayNameBuffer = "";
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.SameLine();
            if (ImGui.Button(L10n.Tr("btn_cancel"), btnSize))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }

    private void DrawOverlays(float dt)
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowViewport(viewport.ID);
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);

        var overlayFlags = ImGuiWindowFlags.NoDecoration |
                           ImGuiWindowFlags.NoDocking |
                           ImGuiWindowFlags.NoMove |
                           ImGuiWindowFlags.NoResize |
                           ImGuiWindowFlags.NoBackground |
                           ImGuiWindowFlags.NoSavedSettings |
                           ImGuiWindowFlags.NoNav |
                           ImGuiWindowFlags.NoFocusOnAppearing;

        if (!IsBuilding)
            overlayFlags |= ImGuiWindowFlags.NoInputs;

        if (ImGui.Begin("##GlobalOverlay", overlayFlags))
        {
            var dl = ImGui.GetWindowDrawList();
            var center = new System.Numerics.Vector2(viewport.Pos.X + viewport.Size.X * 0.5f, viewport.Pos.Y + viewport.Size.Y * 0.5f);

            if (IsBuilding)
            {
                dl.AddRectFilled(viewport.Pos, viewport.Pos + viewport.Size, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.6f)));

                string t1 = L10n.Tr("msg_building_project");
                string t2 = BuildStatus;
                var s1 = ImGui.CalcTextSize(t1);
                var s2 = ImGui.CalcTextSize(t2);

                dl.AddText(new System.Numerics.Vector2(center.X - s1.X * 0.5f, center.Y - 20), ImGui.GetColorU32(new Vector4(1, 1, 0, 1)), t1);
                dl.AddText(new System.Numerics.Vector2(center.X - s2.X * 0.5f, center.Y + 10), ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), t2);
            }

            if (_overlayMessages.Count > 0)
            {
                float yOffset = viewport.Size.Y - 40;
                for (int i = _overlayMessages.Count - 1; i >= 0; i--)
                {
                    var msg = _overlayMessages[i];
                    string text = $"[Verity] {msg.text}";
                    var textSize = ImGui.CalcTextSize(text);
                    var pos = new System.Numerics.Vector2(viewport.Pos.X + 20, viewport.Pos.Y + yOffset - textSize.Y);

                    dl.AddRectFilled(
                        new System.Numerics.Vector2(pos.X - 5, pos.Y - 2),
                        new System.Numerics.Vector2(pos.X + textSize.X + 5, pos.Y + textSize.Y + 2),
                        ImGui.GetColorU32(new Vector4(0, 0, 0, 0.8f)),
                        4f);
                    dl.AddText(pos, ImGui.GetColorU32(new Vector4(1, 0.8f, 0.2f, 1)), text);

                    yOffset -= textSize.Y + 10;
                    float newDur = msg.duration - dt;
                    if (newDur <= 0)
                        _overlayMessages.RemoveAt(i);
                    else
                        _overlayMessages[i] = (msg.text, newDur);
                }
            }
        }

        ImGui.End();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(3);
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
        string editorLogoPath = EditorLogoPath;
        if (File.Exists(editorLogoPath)) {
            var tex = _textureManager.Load(editorLogoPath);
            if (tex != null && tex.ImGuiTextureId != 0) {
                float aspect = (float)tex.Width / tex.Height; float drawH = 100; float drawW = drawH * aspect;
                ImGui.SetCursorPosX((winSize.X - drawW) * 0.5f);
                ImGui.Image(new ImTextureRef(null, new ImTextureID(tex.ImGuiTextureId)), new Vector2(drawW, drawH), new Vector2(0, 1), new Vector2(1, 0));
            }
        } else {
            ImGui.SetCursorPosX((winSize.X - 400) * 0.5f); ImGui.TextColored(new Vector4(0.3f, 0.7f, 1.0f, 1.0f), L10n.Tr("label_launcher_brand"));
        }
        ImGui.SetCursorPosY(170); ImGui.Separator(); ImGui.Dummy(new Vector2(0, 20));
        float contentW = winSize.X * 0.9f; ImGui.SetCursorPosX((winSize.X - contentW) * 0.5f);
        if (ImGui.BeginChild("LauncherContent", new Vector2(contentW, winSize.Y - 240), (ImGuiChildFlags)0, ImGuiWindowFlags.NoBackground)) {
            ImGui.Columns(2, "LauncherColumns", false); ImGui.SetColumnWidth(0, contentW * 0.65f);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1.0f)); ImGui.TextUnformatted(L10n.Tr("label_recent_projects")); ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(0, 10));
            if (Directory.Exists(ProjectsRoot)) {
                if (ImGui.BeginChild("ProjectList", new Vector2(-10, -1), (ImGuiChildFlags)0, ImGuiWindowFlags.NoBackground)) {
                    var projectInfos = GetLauncherProjectInfos();
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
                if (ImGui.Button(L10n.Tr("btn_open_in_explorer_shortcut"), new Vector2(btnWidth, 30))) { if (Directory.Exists(ProjectsRoot)) Process.Start("explorer.exe", ProjectsRoot.Replace("/", "\\")); }
                ImGui.SameLine();
                if (ImGui.Button(L10n.Tr("btn_change_root_path"), new Vector2(btnWidth, 30))) { 
                    var newPath = SelectFolderNative(ProjectsRoot);
                    if (newPath != null && Directory.Exists(newPath)) {
                        ProjectsRoot = newPath;
                        InvalidateLauncherProjectCache();
                        SaveGlobalSettings();
                    }
                }
            }
            ImGui.EndChild(); ImGui.PopStyleVar(2);
        }
        ImGui.EndChild();
        ImGui.SetCursorPos(new Vector2(20, winSize.Y - 35)); ImGui.TextDisabled(L10n.Tr("label_launcher_footer", Version));
        ImGui.End();
    }

    private unsafe void SetupDockSpaceLegacy()
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
                        foreach (var f in GetWorldAssetPaths())
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
                    foreach (string lang in L10n.AvailableLanguages) {
                        string displayName = L10n.GetLanguageDisplayName(lang);
                        if (ImGui.MenuItem(displayName, "", L10n.CurrentLanguage == lang)) { L10n.LoadLanguage(lang); SaveGlobalSettings(); resetLayout = true; }
                    }
                    ImGui.Separator();
                    if (ImGui.MenuItem(L10n.Tr("menu_add_language"))) { _triggerAddLanguagePopup = true; }
                    ImGui.EndMenu();
                }
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu(L10n.Tr("menu_build"))) {
                if (ImGui.MenuItem(L10n.Tr("window_buildmanager"))) GetWindow<BuildManagerWindow>()!.IsOpen = true;
                if (ImGui.MenuItem(L10n.Tr("window_buildsettings"))) GetWindow<BuildSettingsWindow>()!.IsOpen = true;
                ImGui.EndMenu();
            }
            float mid = ImGui.GetWindowWidth() * 0.5f; ImGui.SetCursorPosX(mid - 30);
            if (IsPlaying) { if (ImGui.Button(L10n.Tr("btn_stop"), new Vector2(60, 0))) ExitPlayMode(); }
            else {
                if (HasScriptCompilationErrors) ImGui.BeginDisabled();
                if (ImGui.Button(L10n.Tr("btn_play"), new Vector2(60, 0))) EnterPlayMode();
                if (HasScriptCompilationErrors) ImGui.EndDisabled();
            }
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
            ImGuiP.DockBuilderDockWindow(L10n.Tr("window_animation"), bottomId);
            ImGuiP.DockBuilderDockWindow(L10n.Tr("window_worldview"), centerId);
            ImGuiP.DockBuilderDockWindow(L10n.Tr("window_screen"), centerId);
            
            ImGuiP.DockBuilderFinish(dockId);
        }

        ImGui.DockSpace(dockId);
        ImGui.End();
    }

    private void DrawDockedWorkspace()
    {
        DrawDockSpaceHost();

        foreach (var window in _windows)
            RenderEditorWindow(window, inDetachedMode: false, applyDetachedLayout: false);
    }

    private void DrawDetachedWorkspace()
    {
        bool applyDetachedLayout = _pendingDetachedLayoutReset;

        foreach (var window in _windows.Where(static window => window is ProjectWindow))
            RenderEditorWindow(window, inDetachedMode: true, applyDetachedLayout);

        foreach (var window in _windows.Where(static window => window is not ProjectWindow))
            RenderEditorWindow(window, inDetachedMode: true, applyDetachedLayout);

        _pendingDetachedLayoutReset = false;
    }

    private void RenderEditorWindow(EditorWindow window, bool inDetachedMode, bool applyDetachedLayout)
    {
        if (!window.IsOpen && !(inDetachedMode && window is ProjectWindow))
            return;

        bool isFullscreen = _fullscreenWindow != null && ReferenceEquals(window, _fullscreenWindow);

        bool isProjectHub = inDetachedMode && window is ProjectWindow;
        bool useCloseButton = !inDetachedMode && !isProjectHub;
        bool forceSeparateViewport = window is UIEditorWindow;
        ImGuiWindowFlags flags = (inDetachedMode || forceSeparateViewport) ? ImGuiWindowFlags.NoDocking : ImGuiWindowFlags.None;
        flags |= ImGuiWindowFlags.NoCollapse;
        if (isProjectHub)
        {
            flags |= ImGuiWindowFlags.MenuBar |
                     ImGuiWindowFlags.NoTitleBar |
                     ImGuiWindowFlags.NoMove |
                     ImGuiWindowFlags.NoResize;
        }
        else if (inDetachedMode)
        {
            flags |= ImGuiWindowFlags.NoTitleBar;
        }

        ApplyWindowDefaults(window, inDetachedMode, applyDetachedLayout);
        bool shouldFocus = ReferenceEquals(_pendingFocusedWindow, window);
        if (shouldFocus)
            ImGui.SetNextWindowFocus();

        bool open = window.IsOpen;
        bool began = useCloseButton
            ? ImGui.Begin(window.ImGuiName, ref open, flags)
            : ImGui.Begin(window.ImGuiName, flags);

        if (began)
        {
            if (!isFullscreen && isProjectHub)
            {
                bool resetLayout = false;
                DrawEditorMenuBar(ref resetLayout);
                if (resetLayout)
                    ResetEditorLayout();
            }

            if (window is ScreenWindow &&
                (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) ||
                 ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows)))
                _isScreenFocused = true;

            if (isFullscreen)
            {
                ImGui.TextDisabled($"[{window.Title}]");
            }
            else
            {
                if (!isProjectHub && !inDetachedMode)
                    DetectFullscreenToggle(window);

                if (!inDetachedMode && !forceSeparateViewport)
                    RememberDockedWindowPlacement(window);

                long windowStart = Stopwatch.GetTimestamp();
                window.OnGui();
                _profiler.RecordWindow(window.Title, Stopwatch.GetElapsedTime(windowStart).TotalMilliseconds);
            }
        }

        ImGui.End();
        window.IsOpen = useCloseButton ? open : true;
        if (shouldFocus)
            _pendingFocusedWindow = null;
    }

    private void RenderFullscreenOverlay(EditorWindow window)
    {
        if (!window.IsOpen)
        {
            _fullscreenWindow = null;
            return;
        }

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new Vector2(viewport.Pos.X, viewport.Pos.Y + _menuBarHeight));
        ImGui.SetNextWindowSize(new Vector2(viewport.Size.X, viewport.Size.Y - _menuBarHeight));
        ImGui.SetNextWindowViewport(viewport.ID);
        ImGui.SetNextWindowFocus();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDocking |
                                 ImGuiWindowFlags.NoTitleBar |
                                 ImGuiWindowFlags.NoCollapse |
                                 ImGuiWindowFlags.NoResize |
                                 ImGuiWindowFlags.NoMove |
                                 ImGuiWindowFlags.NoBringToFrontOnFocus |
                                 ImGuiWindowFlags.NoNavFocus;

        string overlayName = string.IsNullOrWhiteSpace(window.WindowId)
            ? $"{window.Title}###__fullscreen"
            : $"{window.Title}###__fullscreen_{window.WindowId}";

        bool began = ImGui.Begin(overlayName, flags);
        ImGui.PopStyleVar(3);

        if (began)
        {
            bool exitRequested = DrawFullscreenTitleBar(window);
            ImGui.SetCursorPos(new Vector2(8f, ImGui.GetCursorPosY() + 8f));
            long windowStart = Stopwatch.GetTimestamp();
            window.OnGui();
            _profiler.RecordWindow(window.Title, Stopwatch.GetElapsedTime(windowStart).TotalMilliseconds);
            if (exitRequested || (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.IsKeyPressed(ImGuiKey.Escape)))
                ExitFullscreen();
        }

        ImGui.End();
    }

    private void DetectFullscreenToggle(EditorWindow window)
    {
        // The fullscreen overlay is rendered in the main viewport.
        // Triggering it from a floating platform window leaves that source
        // window alive underneath, which breaks input and makes the original
        // contents appear "blank". Limit the toggle to docked panels only.
        if (!ImGui.IsWindowDocked())
            return;

        Vector2 windowPos = ImGui.GetWindowPos();
        Vector2 mousePos = ImGui.GetMousePos();
        float titleHeight = ImGui.GetFrameHeight();
        float windowWidth = ImGui.GetWindowWidth();

        bool inTitleArea = mousePos.X >= windowPos.X
            && mousePos.X <= windowPos.X + windowWidth
            && mousePos.Y >= windowPos.Y
            && mousePos.Y <= windowPos.Y + titleHeight;

        if (inTitleArea && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            ToggleFullscreen(window);
            return;
        }
    }

    private void ProcessPendingMainThreadActions()
    {
        while (_pendingMainThreadActions.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                CoreDebug.LogError($"[Editor] Main-thread action failed: {ex.Message}");
            }
        }
    }

    internal void EnqueueMainThreadAction(Action action)
    {
        if (action == null)
            return;

        _pendingMainThreadActions.Enqueue(action);
    }

    private void ToggleFullscreen(EditorWindow window)
    {
        if (_fullscreenWindow == window)
        {
            ExitFullscreen();
            return;
        }

        if (_fullscreenWindow != null)
            ExitFullscreen();

        _fullscreenWindow = window;
    }

    private void ExitFullscreen()
    {
        _fullscreenWindow = null;
    }



    private bool DrawFullscreenTitleBar(EditorWindow window)
    {
        float titleBarHeight = ImGui.GetFrameHeight() + 4f;
        Vector2 cursor = ImGui.GetCursorScreenPos();
        Vector2 titleBarMin = cursor;
        Vector2 titleBarMax = new(cursor.X + ImGui.GetWindowWidth(), cursor.Y + titleBarHeight);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(titleBarMin, titleBarMax, ImGui.GetColorU32(ImGuiCol.TitleBgActive));

        float buttonHeight = Math.Max(20f, titleBarHeight - 6f);
        Vector2 buttonSize = new(Math.Max(70f, ImGui.CalcTextSize(L10n.Tr("tooltip_restore")).X + 18f), buttonHeight);
        Vector2 buttonPos = new(ImGui.GetWindowWidth() - buttonSize.X - 6f, 3f);

        ImGui.SetCursorPos(new Vector2(8f, Math.Max(0f, (titleBarHeight - ImGui.GetTextLineHeight()) * 0.5f)));
        ImGui.TextUnformatted(window.Title);

        ImGui.SetCursorPos(buttonPos);
        bool restoreClicked = ImGui.Button(L10n.Tr("tooltip_restore"), buttonSize);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(L10n.Tr("tooltip_restore"));
            ImGui.EndTooltip();
        }

        Vector2 mousePos = ImGui.GetMousePos();
        bool doubleClicked = mousePos.X >= titleBarMin.X
            && mousePos.X <= titleBarMax.X
            && mousePos.Y >= titleBarMin.Y
            && mousePos.Y <= titleBarMax.Y
            && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);

        ImGui.SetCursorPos(new Vector2(0f, titleBarHeight));
        ImGui.Separator();

        return restoreClicked || doubleClicked;
    }

    private void ApplyWindowDefaults(EditorWindow window, bool inDetachedMode, bool applyDetachedLayout)
    {
        ImGuiCond cond = applyDetachedLayout ? ImGuiCond.Always : ImGuiCond.FirstUseEver;

        if (window is UIEditorWindow)
        {
            var uiViewport = ImGui.GetMainViewport();
            float uiGap = 24f;
            Vector2 detachedPos = new(uiViewport.Pos.X + uiViewport.Size.X + uiGap, uiViewport.Pos.Y + uiGap);
            Vector2 detachedSize = new(
                Math.Max(1100f, MathF.Min(1500f, uiViewport.WorkSize.X)),
                Math.Max(760f, MathF.Min(920f, uiViewport.WorkSize.Y)));

            ImGui.SetNextWindowPos(detachedPos, cond);
            ImGui.SetNextWindowSize(detachedSize, cond);
        }

        if (!inDetachedMode)
            return;

        var viewport = ImGui.GetMainViewport();
        if (window is ProjectWindow)
        {
            ImGui.SetNextWindowViewport(viewport.ID);
            ImGui.SetNextWindowPos(viewport.WorkPos, ImGuiCond.Always);
            ImGui.SetNextWindowSize(viewport.WorkSize, ImGuiCond.Always);
            return;
        }

        if (applyDetachedLayout)
        {
            if (TryGetDockedWindowPlacement(window, out var dockedPlacement))
            {
                ImGui.SetNextWindowPos(dockedPlacement.Position, ImGuiCond.Always);
                ImGui.SetNextWindowSize(dockedPlacement.Size, ImGuiCond.Always);
                return;
            }
        }

        float gap = 24f;
        Vector2 hubOrigin = new(viewport.WorkPos.X + 10f, viewport.WorkPos.Y + 10f);
        Vector2 hubSize = new(
            Math.Clamp(viewport.WorkSize.X - 20f, 420f, 620f),
            Math.Clamp(viewport.WorkSize.Y - 20f, 560f, 1200f));

        float detachedLeft = viewport.Pos.X + viewport.Size.X + gap;
        float detachedTop = viewport.Pos.Y + 24f;
        float worldWidth = 1400f;
        float worldHeight = 820f;
        float screenHeight = 420f;
        float sideColumnX = detachedLeft + worldWidth + gap;
        float bottomRowY = detachedTop + worldHeight + gap;

        Vector2 position;
        Vector2 size;

        switch (window)
        {
            case ProjectWindow:
                ImGui.SetNextWindowViewport(viewport.ID);
                position = hubOrigin;
                size = hubSize;
                break;
            case WorldViewWindow:
                position = new Vector2(detachedLeft, detachedTop);
                size = new Vector2(worldWidth, worldHeight);
                break;
            case ScreenWindow:
                position = new Vector2(detachedLeft, bottomRowY);
                size = new Vector2(worldWidth, screenHeight);
                break;
            case HierarchyWindow:
                position = new Vector2(sideColumnX, detachedTop);
                size = new Vector2(360f, 420f);
                break;
            case InspectorWindow:
                position = new Vector2(sideColumnX, detachedTop + 444f);
                size = new Vector2(420f, 576f);
                break;
            default:
                int index = Math.Max(0, _windows.IndexOf(window));
                float cascadeX = detachedLeft + 120f + (index % 5) * 44f;
                float cascadeY = detachedTop + 120f + (index % 4) * 36f;
                position = new Vector2(cascadeX, cascadeY);
                size = window switch
                {
                    ConsoleWindow => new Vector2(960f, 300f),
                    AnimationWindow => new Vector2(1020f, 520f),
                    BuildManagerWindow => new Vector2(760f, 520f),
                    BuildSettingsWindow => new Vector2(900f, 620f),
                    FilterEditorWindow => new Vector2(980f, 680f),
                    TilePaletteWindow => new Vector2(900f, 560f),
                    _ => new Vector2(820f, 520f)
                };
                break;
        }

        ImGui.SetNextWindowPos(position, cond);
        ImGui.SetNextWindowSize(size, cond);
    }

    private unsafe void DrawDockSpaceHost()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        var flags = ImGuiWindowFlags.MenuBar | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
        ImGui.Begin("DockSpace", flags);
        ImGui.PopStyleVar(3);

        bool resetLayout = false;
        DrawEditorMenuBar(ref resetLayout);

        _menuBarHeight = ImGui.GetCursorPosY();

        uint dockId = ImGui.GetID("VerityDockSpace");

        if (resetLayout || _pendingLayoutReset || (!_loadedDockLayoutFromSettings && ImGuiP.DockBuilderGetNode(dockId).Handle == null))
        {
            _pendingLayoutReset = false;
            foreach (var win in _windows)
                win.RefreshTitle();

            ImGuiP.DockBuilderRemoveNode(dockId);
            ImGuiP.DockBuilderAddNode(dockId, ImGuiDockNodeFlags.None);
            ImGuiP.DockBuilderSetNodeSize(dockId, viewport.Size);

            uint workspaceId = dockId;
            uint topWorkspaceId = dockId;
            uint leftId;
            uint rightId;
            uint bottomId;

            ImGuiP.DockBuilderSplitNode(workspaceId, ImGuiDir.Right, 0.25f, &rightId, &workspaceId);
            ImGuiP.DockBuilderSplitNode(workspaceId, ImGuiDir.Down, 0.3f, &bottomId, &topWorkspaceId);
            ImGuiP.DockBuilderSplitNode(topWorkspaceId, ImGuiDir.Left, 0.2f, &leftId, &topWorkspaceId);

            ImGuiP.DockBuilderDockWindow(GetWindowName<HierarchyWindow>(), leftId);
            ImGuiP.DockBuilderDockWindow(GetWindowName<InspectorWindow>(), rightId);
            ImGuiP.DockBuilderDockWindow(GetWindowName<ProjectWindow>(), bottomId);
            ImGuiP.DockBuilderDockWindow(GetWindowName<ConsoleWindow>(), bottomId);
            ImGuiP.DockBuilderDockWindow(GetWindowName<AnimationWindow>(), bottomId);
            ImGuiP.DockBuilderDockWindow(GetWindowName<WorldViewWindow>(), topWorkspaceId);
            ImGuiP.DockBuilderDockWindow(GetWindowName<ScreenWindow>(), topWorkspaceId);

            ImGuiP.DockBuilderFinish(dockId);
        }

        ImGui.DockSpace(dockId);
        if (_loadedDockLayoutFromSettings && ImGuiP.DockBuilderGetNode(dockId).Handle != null)
            _loadedDockLayoutFromSettings = false;
        ImGui.End();
    }

    private void DrawEditorMenuBar(ref bool resetLayout)
    {
        if (!ImGui.BeginMenuBar())
            return;

        var assetWindow = GetWindow<ProjectWindow>();
        if (ImGui.BeginMenu(L10n.Tr("menu_file")))
        {
            if (ImGui.MenuItem(L10n.Tr("menu_new_world")))
                assetWindow?.CreateWorldInProject();

            if (ImGui.BeginMenu(L10n.Tr("menu_open_world")))
            {
                if (AssetsPath != null && Directory.Exists(AssetsPath))
                {
                    foreach (var f in GetWorldAssetPaths())
                    {
                        if (ImGui.MenuItem(Path.GetRelativePath(AssetsPath, f)))
                            assetWindow?.LoadWorldByPath(f);
                    }
                }
                ImGui.EndMenu();
            }

            if (ImGui.MenuItem(L10n.Tr("menu_save_world")))
                assetWindow?.SaveActiveWorldAsAsset();

            ImGui.Separator();
            if (ImGui.MenuItem(L10n.Tr("menu_close_project")))
                RequestCloseProject();
            if (ImGui.MenuItem(L10n.Tr("menu_exit")))
                RequestExit();
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(L10n.Tr("menu_window")))
        {
            string windowModeLabel = L10n.Tr("menu_window_mode");
            string dockedLabel = L10n.Tr("window_mode_docked");
            string detachedLabel = L10n.Tr("window_mode_detached");
            string layoutsLabel = L10n.Tr("menu_layouts");
            string saveProjectLayoutLabel = L10n.Tr("menu_save_project_layout");
            string saveLayoutPresetLabel = L10n.Tr("menu_save_layout_preset");
            string loadProjectLayoutLabel = L10n.Tr("menu_load_project_layout");
            string loadLayoutPresetLabel = L10n.Tr("menu_load_layout_preset");
            string noLayoutPresetsLabel = L10n.Tr("menu_no_layout_presets");

            if (ImGui.BeginMenu(windowModeLabel))
            {
                if (ImGui.MenuItem(dockedLabel, "", _windowMode == EditorWindowMode.Docked))
                    SetWindowMode(EditorWindowMode.Docked);
                if (ImGui.MenuItem(detachedLabel, "", _windowMode == EditorWindowMode.Detached))
                    SetWindowMode(EditorWindowMode.Detached);
                ImGui.EndMenu();
            }

            ImGui.Separator();
            bool canEditDockLayout = _windowMode == EditorWindowMode.Docked;
            if (!canEditDockLayout)
                ImGui.BeginDisabled();

            if (ImGui.BeginMenu(layoutsLabel))
            {
                if (ImGui.MenuItem(saveProjectLayoutLabel))
                {
                    AutoSaveEditorState();
                ShowOverlayMessage(L10n.Tr("msg_project_layout_saved"));
                }

                if (ImGui.MenuItem(saveLayoutPresetLabel))
                    RequestSaveLayoutPreset();

                bool hasProjectLayout = !string.IsNullOrWhiteSpace(ProjectSettings.EditorDockLayout?.Ini) ||
                    (ProjectSettings.EditorDockLayout?.OpenWindowIds?.Count ?? 0) > 0;
                if (!hasProjectLayout)
                    ImGui.BeginDisabled();
                if (ImGui.MenuItem(loadProjectLayoutLabel))
                    LoadProjectDockLayout();
                if (!hasProjectLayout)
                    ImGui.EndDisabled();

                if (ImGui.BeginMenu(loadLayoutPresetLabel))
                {
                    var presetFiles = GetLayoutPresetFiles().ToList();
                    if (presetFiles.Count == 0)
                    {
                        ImGui.MenuItem(noLayoutPresetsLabel, "", false, false);
                    }
                    else
                    {
                        foreach (var presetFile in presetFiles)
                        {
                            string presetName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(presetFile));
                            if (ImGui.MenuItem(presetName))
                                LoadLayoutPreset(presetFile);
                        }
                    }
                    ImGui.EndMenu();
                }

                ImGui.EndMenu();
            }

            if (!canEditDockLayout)
                ImGui.EndDisabled();

            ImGui.Separator();
            foreach (var win in _windows)
            {
                if (_windowMode == EditorWindowMode.Detached && win is ProjectWindow)
                {
                    ImGui.MenuItem(win.Title, "", true, false);
                    continue;
                }

                if (ImGui.MenuItem(win.Title, "", win.IsOpen))
                {
                    bool nextOpen = !win.IsOpen;
                    win.IsOpen = nextOpen;
                    if (nextOpen)
                        _pendingFocusedWindow = win;
                }
            }

            ImGui.Separator();
            if (ImGui.MenuItem(L10n.Tr("menu_reset_layout")))
                resetLayout = true;

            ImGui.Separator();
            if (ImGui.BeginMenu(L10n.Tr("menu_language")))
            {
                foreach (string lang in L10n.AvailableLanguages)
                {
                    string displayName = L10n.GetLanguageDisplayName(lang);
                    if (ImGui.MenuItem(displayName, "", L10n.CurrentLanguage == lang))
                    {
                        L10n.LoadLanguage(lang);
                        SaveGlobalSettings();
                        resetLayout = true;
                    }
                }
                ImGui.Separator();
                if (ImGui.MenuItem(L10n.Tr("menu_add_language")))
                    _triggerAddLanguagePopup = true;
                ImGui.EndMenu();
            }
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu(L10n.Tr("menu_build")))
        {
            if (ImGui.MenuItem(L10n.Tr("window_buildmanager")))
                OpenWindow<BuildManagerWindow>();
            if (ImGui.MenuItem(L10n.Tr("window_buildsettings")))
                OpenWindow<BuildSettingsWindow>();
            ImGui.EndMenu();
        }

        float mid = ImGui.GetWindowWidth() * 0.5f;
        ImGui.SetCursorPosX(mid - 30);
        if (IsPlaying)
        {
            if (ImGui.Button(L10n.Tr("btn_stop"), new Vector2(60, 0)))
                ExitPlayMode();
        }
        else
        {
            if (HasScriptCompilationErrors)
                ImGui.BeginDisabled();
            if (ImGui.Button(L10n.Tr("btn_play"), new Vector2(60, 0)))
                EnterPlayMode();
            if (HasScriptCompilationErrors)
                ImGui.EndDisabled();
        }

        ImGui.EndMenuBar();
    }

    public void SaveEntityAsBlueprint(Entity entity, string? targetPath = null) {
        string dir = targetPath ?? AssetsPath ?? ""; if (string.IsNullOrEmpty(dir)) return;
        string safeName = string.Join("_", entity.Name.Split(Path.GetInvalidFileNameChars())); if (string.IsNullOrWhiteSpace(safeName)) safeName = "Entity";
        string path = Path.Combine(dir, $"{safeName}.blueprint"); int count = 1; while (File.Exists(path)) path = Path.Combine(dir, $"{safeName}_{count++}.blueprint");
        try
        {
            string json = Verity.Core.Serialization.SceneSerializer.SerializeEntity(entity);
            File.WriteAllText(path, json);
            AssetReferenceData assetReference = AssetPathUtility.CreateReference(path);
            MarkEntityAsBlueprintInstance(entity, assetReference);
        }
        catch { }
    }

    private static void MarkEntityAsBlueprintInstance(Entity root, AssetReferenceData assetReference)
    {
        Guid rootId = root.Id;

        static IEnumerable<Entity> EnumerateDescendantsAndSelf(Entity entity)
        {
            yield return entity;
            foreach (Transform child in entity.Transform.Children)
            {
                foreach (Entity descendant in EnumerateDescendantsAndSelf(child.Owner))
                    yield return descendant;
            }
        }

        foreach (Entity entity in EnumerateDescendantsAndSelf(root))
        {
            entity.BlueprintAssetPath = assetReference.Path;
            entity.BlueprintAssetGuid = assetReference.Guid;
            entity.BlueprintSourceEntityId = entity.Id;
            entity.BlueprintInstanceRootId = rootId;
        }
    }

    public Entity? InstantiateBlueprint(string path, Vector2? position = null, Entity? parent = null) {
        var world = WorldManager.ActiveWorld; if (world == null || !File.Exists(path) || AssetsPath == null) return null;
        if (!CanDeserializeScriptedAssets()) {
            ShowOverlayMessage(L10n.Tr("msg_cannot_load_script_asset_compilation_errors"), 3.0f);
            CoreDebug.LogError("[Editor] Cannot instantiate blueprint while user script compilation errors exist and no valid compiled assembly is available.");
            return null;
        }
        try { var entity = Verity.Core.Serialization.SceneSerializer.InstantiateBlueprintInstance(world, path, ScriptCompiler?.CompiledAssembly); if (entity != null) { if (position.HasValue) entity.Transform.Position = position.Value; if (parent != null) entity.Transform.SetParent(parent.Transform, false); else AttachToBlueprintDefaultParent(entity); BindEntityAssetsRecursive(entity); return entity; } } catch { } return null;
    }

    private List<BlueprintInstanceRefreshState> CaptureBlueprintInstanceRefreshStates(string blueprintPath)
    {
        string normalizedPath = AssetPathUtility.Normalize(blueprintPath);
        string guid = AssetPathUtility.EnsureMetaAndGetGuid(blueprintPath);
        var states = new List<BlueprintInstanceRefreshState>();

        foreach (var world in WorldManager.LoadedWorlds)
        {
            foreach (var entity in world.GetAllEntities())
            {
                if (!entity.IsBlueprintInstanceRoot)
                    continue;
                if (!BlueprintMatchesAsset(entity, normalizedPath, guid))
                    continue;

                states.Add(new BlueprintInstanceRefreshState(entity, SceneSerializer.CaptureBlueprintInstanceOverrides(entity)));
            }
        }

        return states;
    }

    private static bool BlueprintMatchesAsset(Entity entity, string normalizedPath, string guid)
    {
        if (!string.IsNullOrWhiteSpace(guid) &&
            string.Equals(entity.BlueprintAssetGuid, guid, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(
            AssetPathUtility.Normalize(entity.BlueprintAssetPath),
            normalizedPath,
            StringComparison.OrdinalIgnoreCase);
    }

    public void BindWorldAssets(World world)
    {
        LuaScriptManager.Initialize(ProjectPath, _scriptCompiler?.CompiledAssembly);
        foreach (var root in world.RootEntities)
            BindEntityAssetsRecursive(root);
    }

    public Sprite CreateSpriteReference(string path, string? spriteId = null)
    {
        string normalized = AssetPathUtility.Normalize(path);
        string guid = Path.IsPathRooted(path) ? AssetPathUtility.EnsureMetaAndGetGuid(path) : string.Empty;
        return new Sprite(normalized, guid, spriteId ?? string.Empty);
    }

    public SpriteImportSettings GetOrCreateSpriteImportSettings(string assetPath)
    {
        string fullPath = Path.GetFullPath(assetPath);
        var existing = AssetPathUtility.TryGetSpriteImportSettings(fullPath);
        if (existing != null)
        {
            var raw = TextureManager.GetRawPixels(fullPath);
            existing.Normalize(raw.Width, raw.Height);
            AssetPathUtility.SaveSpriteImportSettings(fullPath, existing);
            return existing;
        }

        var image = TextureManager.GetRawPixels(fullPath);
        var created = SpriteImportUtility.CreateDefaults(ProjectSettings, image.Width, image.Height);
        AssetPathUtility.SaveSpriteImportSettings(fullPath, created);
        return created;
    }

    public SpriteImportSettings? TryGetSpriteImportSettings(string assetPath, bool initializeIfMissing = true)
    {
        string fullPath = Path.GetFullPath(assetPath);
        var settings = AssetPathUtility.TryGetSpriteImportSettings(fullPath);
        if (settings != null || !initializeIfMissing)
            return settings;

        return GetOrCreateSpriteImportSettings(fullPath);
    }

    public SpriteSlice ResolveSpriteSlice(Sprite sprite, bool initializeIfMissing = true)
    {
        if (string.IsNullOrWhiteSpace(sprite.Path))
            return SpriteImportUtility.CreateDefaultSlice(1, 1, new Vector2(0.5f, 0.5f));

        string fullPath = ResolveAssetPath(sprite.Path, sprite.Guid);
        if (!File.Exists(fullPath))
            return SpriteImportUtility.CreateDefaultSlice(1, 1, new Vector2(0.5f, 0.5f));

        var raw = TextureManager.GetRawPixels(fullPath);
        if (initializeIfMissing)
            GetOrCreateSpriteImportSettings(fullPath);

        return AssetPathUtility.ResolveSpriteSlice(fullPath, sprite, raw.Width, raw.Height);
    }

    public Vector2 GetDefaultSpriteWorldSize(Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(sprite.Path))
            return Vector2.One;

        string fullPath = ResolveAssetPath(sprite.Path, sprite.Guid);
        if (!File.Exists(fullPath))
            return Vector2.One;

        var raw = TextureManager.GetRawPixels(fullPath);
        var settings = GetOrCreateSpriteImportSettings(fullPath);
        var slice = AssetPathUtility.ResolveSpriteSlice(fullPath, sprite, raw.Width, raw.Height);
        return SpriteImportUtility.ComputeWorldSize(settings, slice);
    }

    public Vector2 GetDefaultSpritePivot(Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(sprite.Path))
            return new Vector2(0.5f, 0.5f);

        string fullPath = ResolveAssetPath(sprite.Path, sprite.Guid);
        if (!File.Exists(fullPath))
            return new Vector2(0.5f, 0.5f);

        var raw = TextureManager.GetRawPixels(fullPath);
        if (AssetPathUtility.TryGetSpriteImportSettings(fullPath) == null)
            GetOrCreateSpriteImportSettings(fullPath);

        return AssetPathUtility.ResolveSpriteSlice(fullPath, sprite, raw.Width, raw.Height).Pivot;
    }

    public RenderTexture? LoadSpriteTexture(Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(sprite.Path))
            return null;

        string fullPath = ResolveAssetPath(sprite.Path, sprite.Guid);
        if (!File.Exists(fullPath))
            return null;

        var settings = GetOrCreateSpriteImportSettings(fullPath);
        return TextureManager.Load(fullPath, settings.Filter);
    }

    public void BindEntityAssetsRecursive(Entity entity) {
        var sr = entity.GetComponent<SpriteRenderer>(); if (sr != null && !string.IsNullOrWhiteSpace(sr.Sprite.Path)) { var fullPath = ResolveAssetPath(sr.Sprite.Path, sr.Sprite.Guid); if (File.Exists(fullPath)) { sr.Sprite = new Sprite(AssetPathUtility.Normalize(fullPath), string.IsNullOrWhiteSpace(sr.Sprite.Guid) ? AssetPathUtility.TryGetGuid(fullPath) : sr.Sprite.Guid, sr.Sprite.SpriteId); sr.Texture = LoadSpriteTexture(sr.Sprite); } }
        var animator = entity.GetComponent<Animator>(); if (animator != null && !string.IsNullOrWhiteSpace(animator.ControllerPath)) { string controllerFullPath = ResolveAssetPath(animator.ControllerPath, animator.ControllerGuid); if (File.Exists(controllerFullPath)) { animator.ControllerPath = AssetPathUtility.Normalize(controllerFullPath); animator.ControllerGuid = string.IsNullOrWhiteSpace(animator.ControllerGuid) ? AssetPathUtility.TryGetGuid(controllerFullPath) : animator.ControllerGuid; animator.Controller = Verity.Core.Animation.AnimatorControllerAsset.LoadFromFile(controllerFullPath); } }
        var audioSource = entity.GetComponent<AudioSource>(); if (audioSource?.Clip != null && !string.IsNullOrWhiteSpace(audioSource.Clip.Path)) { string clipFullPath = ResolveAssetPath(audioSource.Clip.Path, audioSource.Clip.Guid); if (File.Exists(clipFullPath)) { audioSource.Clip.Path = AssetPathUtility.Normalize(clipFullPath); audioSource.Clip.Guid = string.IsNullOrWhiteSpace(audioSource.Clip.Guid) ? AssetPathUtility.TryGetGuid(clipFullPath) : audioSource.Clip.Guid; audioSource.Clip.PostLoad(clipFullPath); } }
        foreach (var luaScript in entity.GetComponents<LuaScriptComponent>()) { if (!string.IsNullOrWhiteSpace(luaScript.ScriptPath)) { string scriptFullPath = ResolveAssetPath(luaScript.ScriptPath, luaScript.ScriptGuid); if (File.Exists(scriptFullPath)) { string normalizedScriptPath = AssetPathUtility.Normalize(scriptFullPath); bool pathChanged = !string.Equals(luaScript.ScriptPath, normalizedScriptPath, StringComparison.OrdinalIgnoreCase); if (string.IsNullOrWhiteSpace(luaScript.ScriptGuid)) luaScript.ScriptGuid = AssetPathUtility.TryGetGuid(scriptFullPath) ?? string.Empty; luaScript.ScriptPath = normalizedScriptPath; if (!pathChanged) luaScript.ReloadScript(); } else { luaScript.ReloadScript(); } } }
        entity.GetComponent<AudioManager>()?.SyncGroupMap();
        foreach (var child in entity.Transform.Children) BindEntityAssetsRecursive(child.Owner);
    }

    private void ProcessPendingAssetInvalidations()
    {
        List<string> textures;
        List<string> tiles;
        List<string> luas = [];
        long nowMs = Environment.TickCount64;

        lock (_assetInvalidationLock)
        {
            if (_pendingLuaHotReloadPaths.Count > 0)
            {
                foreach (string path in _pendingLuaHotReloadPaths.ToList())
                {
                    if (_pendingLuaHotReloadDeadlines.TryGetValue(path, out long dueMs) && dueMs > nowMs)
                        continue;

                    luas.Add(path);
                    _pendingLuaHotReloadPaths.Remove(path);
                    _pendingLuaHotReloadDeadlines.Remove(path);
                }
            }

            if (_pendingTextureRefreshes.Count == 0 && _pendingTileRefreshes.Count == 0 && luas.Count == 0) return;

            textures = [];
            foreach (string path in _pendingTextureRefreshes.ToList())
            {
                if (_pendingTextureRefreshDeadlines.TryGetValue(path, out long dueMs) && dueMs > nowMs)
                    continue;

                textures.Add(path);
                _pendingTextureRefreshes.Remove(path);
                _pendingTextureRefreshDeadlines.Remove(path);
            }

            tiles = [];
            foreach (string path in _pendingTileRefreshes.ToList())
            {
                if (_pendingTileRefreshDeadlines.TryGetValue(path, out long dueMs) && dueMs > nowMs)
                    continue;

                tiles.Add(path);
                _pendingTileRefreshes.Remove(path);
                _pendingTileRefreshDeadlines.Remove(path);
            }

            if (textures.Count == 0 && tiles.Count == 0 && luas.Count == 0)
                return;
        }

        if (luas.Count > 0)
        {
            string changedSummary = string.Join(", ", luas.Select(Path.GetFileName).OrderBy(static name => name));
            if (ReloadActiveLuaScripts(luas))
            {
                CoreDebug.Log($"[Editor] Lua hot reload successful{(IsPlaying ? " during Play Mode" : string.Empty)}: {changedSummary}");
                ShowOverlayMessage(L10n.Tr("msg_scripts_reloaded"));
            }
        }

        foreach (var path in textures)
        {
            if (TryMarkTextureRefresh(path))
            {
                RefreshTextureAsset(path);
            }
        }

        var tilePalette = GetWindow<TilePaletteWindow>();
        foreach (var path in tiles)
        {
            if (TryMarkTileRefresh(path))
            {
                TileAssetCache.Invalidate(path, ProjectPath);
                tilePalette?.InvalidateTileAsset(path);
            }
        }
    }

    private bool ReloadActiveLuaScripts(IReadOnlyList<string> changedPaths)
    {
        var world = WorldManager.ActiveWorld;
        if (world == null || changedPaths.Count == 0)
            return false;

        HashSet<string> changed = changedPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool reloadedAny = false;
        foreach (Entity entity in world.GetAllEntities())
        {
            foreach (var luaScript in entity.GetComponents<LuaScriptComponent>())
            {
                if (string.IsNullOrWhiteSpace(luaScript.ScriptPath))
                    continue;

                string resolvedPath = ResolveAssetPath(luaScript.ScriptPath, luaScript.ScriptGuid);
                if (!changed.Contains(Path.GetFullPath(resolvedPath)))
                    continue;

                if (File.Exists(resolvedPath))
                {
                    string normalizedScriptPath = AssetPathUtility.Normalize(resolvedPath);
                    bool pathChanged = !string.Equals(luaScript.ScriptPath, normalizedScriptPath, StringComparison.OrdinalIgnoreCase);
                    if (string.IsNullOrWhiteSpace(luaScript.ScriptGuid))
                        luaScript.ScriptGuid = AssetPathUtility.TryGetGuid(resolvedPath) ?? string.Empty;

                    luaScript.ScriptPath = normalizedScriptPath;
                    if (!pathChanged)
                        luaScript.ReloadScript();
                }
                else
                {
                    luaScript.ReloadScript();
                }

                reloadedAny = true;
            }
        }

        return reloadedAny;
    }

    private bool TryMarkTextureRefresh(string fullPath)
    {
        string normalized = Path.GetFullPath(fullPath);
        long signature = ComputeAssetRefreshSignature(normalized, includeMeta: true);
        if (_processedTextureRefreshSignatures.TryGetValue(normalized, out long previousSignature) && previousSignature == signature)
            return false;

        _processedTextureRefreshSignatures[normalized] = signature;
        return true;
    }

    private bool TryMarkTileRefresh(string fullPath)
    {
        string normalized = Path.GetFullPath(fullPath);
        long signature = ComputeAssetRefreshSignature(normalized, includeMeta: true);
        if (_processedTileRefreshSignatures.TryGetValue(normalized, out long previousSignature) && previousSignature == signature)
            return false;

        _processedTileRefreshSignatures[normalized] = signature;
        return true;
    }

    private static long ComputeAssetRefreshSignature(string fullPath, bool includeMeta)
    {
        long assetTicks = File.Exists(fullPath) ? File.GetLastWriteTimeUtc(fullPath).Ticks : -1;
        if (!includeMeta)
            return assetTicks;

        string metaPath = AssetPathUtility.GetMetaPath(fullPath);
        long metaTicks = File.Exists(metaPath) ? File.GetLastWriteTimeUtc(metaPath).Ticks : -1;
        return unchecked((assetTicks * 397L) ^ metaTicks);
    }

    private void ClearAssetRefreshTracking()
    {
        lock (_assetInvalidationLock)
        {
            _pendingTextureRefreshes.Clear();
            _pendingTileRefreshes.Clear();
            _pendingTextureRefreshDeadlines.Clear();
            _pendingTileRefreshDeadlines.Clear();
            _processedTextureRefreshSignatures.Clear();
            _processedTileRefreshSignatures.Clear();
            _pendingLuaHotReloadPaths.Clear();
            _pendingLuaHotReloadDeadlines.Clear();
        }
    }

    private void RefreshTextureAsset(string fullPath)
    {
        string normalized = Path.GetFullPath(fullPath);
        _textureManager.Unload(normalized);

        var world = WorldManager.ActiveWorld;
        if (world == null || ProjectPath == null) return;

        foreach (var entity in world.GetAllEntities())
        {
            var sr = entity.GetComponent<SpriteRenderer>();
            if (sr != null && !string.IsNullOrWhiteSpace(sr.Sprite.Path))
            {
                string spritePath = ResolveAssetPath(sr.Sprite.Path, sr.Sprite.Guid);
                if (string.Equals(spritePath, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    sr.Texture = File.Exists(normalized) ? LoadSpriteTexture(sr.Sprite) : null;
                }
            }

            var tilemapRenderer = entity.GetComponent<TilemapRenderer>();
            if (tilemapRenderer != null)
            {
                tilemapRenderer.ClearTextureCache();
            }

            var tilemap = entity.GetComponent<Tilemap>();
            if (tilemap != null)
            {
                tilemap.RenderDirty = true;
            }
        }
    }

    private string ResolveAssetPath(string relativeOrAbsolutePath, string? guid = null)
    {
        return AssetPathUtility.ResolvePath(ProjectPath, relativeOrAbsolutePath, guid);
    }

    private void InitializeAssetWatcher(string path)
    {
        if (_assetWatcher != null)
        {
            _assetWatcher.Changed -= OnAssetWatcherChanged;
            _assetWatcher.Created -= OnAssetWatcherChanged;
            _assetWatcher.Deleted -= OnAssetWatcherChanged;
            _assetWatcher.Renamed -= OnAssetWatcherRenamed;
            _assetWatcher.Dispose();
        }

        _assetWatcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            Filter = "*.*",
            EnableRaisingEvents = true
        };

        _assetWatcher.Changed += OnAssetWatcherChanged;
        _assetWatcher.Created += OnAssetWatcherChanged;
        _assetWatcher.Deleted += OnAssetWatcherChanged;
        _assetWatcher.Renamed += OnAssetWatcherRenamed;
    }

    private void HandleAssetWatcherChange(string changedPath)
    {
        if (AssetPathUtility.IsMetaFile(changedPath))
            changedPath = changedPath[..^5];

        AssetPathUtility.InvalidateAssetCache(changedPath);

        if (!File.Exists(changedPath))
        {
            string normalizedMissingPath = Path.GetFullPath(changedPath);
            lock (_assetInvalidationLock)
            {
                _processedTextureRefreshSignatures.Remove(normalizedMissingPath);
                _processedTileRefreshSignatures.Remove(normalizedMissingPath);
                _pendingTextureRefreshes.Remove(normalizedMissingPath);
                _pendingTileRefreshes.Remove(normalizedMissingPath);
                _pendingTextureRefreshDeadlines.Remove(normalizedMissingPath);
                _pendingTileRefreshDeadlines.Remove(normalizedMissingPath);
                _pendingLuaHotReloadPaths.Remove(normalizedMissingPath);
                _pendingLuaHotReloadDeadlines.Remove(normalizedMissingPath);
            }
        }

        string ext = Path.GetExtension(changedPath).ToLower();
        if (ext == ".verity")
            InvalidateWorldAssetCache();

        if (ext == ".style") {
            if (ProjectPath != null) {
                string relPath = Path.GetRelativePath(ProjectPath, changedPath).Replace("\\", "/");
                _renderPipeline.ClearStyleCache(relPath);
                if (!File.Exists(changedPath))
                    GetWindow<ProjectWindow>()?.RemoveDeletedStyleReferences(relPath);
            }
        } else if (ext == ".shader") {
            _renderPipeline.ClearShaderCache(changedPath);
        } else if (ext is ".png" or ".jpg" or ".jpeg") {
            lock (_assetInvalidationLock)
            {
                string normalized = Path.GetFullPath(changedPath);
                _pendingTextureRefreshes.Add(normalized);
                _pendingTextureRefreshDeadlines[normalized] = Environment.TickCount64 + AssetRefreshDebounceMs;
            }
        } else if (ext is ".tile" or ".animtile" or ".ruletile") {
            lock (_assetInvalidationLock)
            {
                string normalized = Path.GetFullPath(changedPath);
                _pendingTileRefreshes.Add(normalized);
                _pendingTileRefreshDeadlines[normalized] = Environment.TickCount64 + AssetRefreshDebounceMs;
            }
        }
    }

    private void OnAssetWatcherChanged(object sender, FileSystemEventArgs e)
    {
        HandleAssetWatcherChange(e.FullPath);
    }

    private void OnAssetWatcherRenamed(object sender, RenamedEventArgs e)
    {
        HandleAssetWatcherChange(e.OldFullPath);
        HandleAssetWatcherChange(e.FullPath);
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
        AutoSaveEditorState();
        CoreDebug.OnLog -= OnCoreLog;
        _device.Window.OnSdlEvent -= Verity.Input.Input.ProcessEvent;
        BuildManagerWindow.ShutdownPreviewServer();
        ClearAssetRefreshTracking();

        if (_assetWatcher != null)
        {
            _assetWatcher.Changed -= OnAssetWatcherChanged;
            _assetWatcher.Created -= OnAssetWatcherChanged;
            _assetWatcher.Deleted -= OnAssetWatcherChanged;
            _assetWatcher.Renamed -= OnAssetWatcherRenamed;
            _assetWatcher.Dispose();
        }

        if (_scriptCompiler != null)
        {
            _scriptCompiler.OnCompilationFinished -= OnScriptsCompiled;
            _scriptCompiler.Dispose();
        }

        LuaScriptManager.HotReloadRequested -= OnLuaScriptsHotReloadRequested;
        LuaScriptManager.SuspendHotReloadEvents = false;
        LuaScriptManager.Dispose();

        _renderPipeline.Dispose(); 
        _shader.Dispose(); 
        _textureManager.Dispose(); 
        _imgui.Dispose(); 
        AudioSystem.Shutdown();
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
