using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Verity.Core;
using Verity.Core.Audio;
using Verity.Core.ECS;
using Verity.Core.Engine;
using Verity.Core.Serialization;
using Verity.Core.Scripting;
using Verity.Core.UI;
using Verity.Core.World;
using Verity.Filter;
using Verity.Graphics;

namespace Verity.Game.Runtime;

internal sealed class RuntimeApp : IDisposable
{
    private readonly IRuntimeHost _runtimeHost;
    private readonly IRuntimeContentSource _contentSource;
    private readonly string _baseDir;
    private readonly string _fallbackFontFamily;
    private readonly Action<string, string> _writeRuntimeLog;
    private readonly bool _minimalBrowserMode;

    private readonly IRenderDevice _device;
    private readonly Shader2D _shader;
    private readonly TextureManager _textureManager;
    private readonly RenderPipeline _renderPipeline;
    private readonly ProfilerOverlay _profilerOverlay;
    private readonly GameLoop _gameLoop;
    private readonly Stopwatch _stopwatch;

    private readonly BuildSettings _buildSettings;
    private readonly ProjectSettings _projectSettings;
    private readonly Assembly? _userAssembly;
    private readonly Stopwatch _fpsSampleTimer = Stopwatch.StartNew();

    private long _lastTicks;
    private int _frameCount;
    private int _framesSinceLastFpsSample;
    private bool _firstRenderableFrameLogged;
    private bool _missingCameraLogged;
    private float _displayFps;
    private int _cachedEntityCount;
    private int _cachedTileCount;
    private int _lastDebugWorldStateVersion = int.MinValue;
    private string _cachedDebugState = "fps=0.0;entities=0;tiles=0;textures=0";

    public RuntimeApp(
        IRuntimeHost runtimeHost,
        IRuntimeContentSource contentSource,
        string baseDir,
        string fallbackFontFamily,
        Action<string, string> writeRuntimeLog,
        bool minimalBrowserMode = false)
    {
        _runtimeHost = runtimeHost;
        _contentSource = contentSource;
        _baseDir = baseDir;
        _fallbackFontFamily = fallbackFontFamily;
        _writeRuntimeLog = writeRuntimeLog;
        _minimalBrowserMode = minimalBrowserMode;

        _device = _runtimeHost.CreateGraphicsDevice("Verity Game", 1280, 720, true);
        _writeRuntimeLog("Runtime", $"GraphicsDevice created: {_device.Width}x{_device.Height}");

        _shader = Shader2D.Create(_device);
        _textureManager = new TextureManager(_device);
        _renderPipeline = new RenderPipeline(_device, _shader, _textureManager);
        _profilerOverlay = new ProfilerOverlay();

        RenderPipeline.BaseAssetsPath = _baseDir;
        SceneSerializer.AssetRootPath = _baseDir;
        UiSystem.AssetsRoot = _baseDir;
        UiRenderer.DefaultFontPath = string.Empty;
        UiRenderer.DefaultFontFamily = _fallbackFontFamily;
        _renderPipeline.SetWhitePixel(_textureManager.CreateWhitePixel());
        DefaultSprites.Initialize(_textureManager);

        if (_minimalBrowserMode)
        {
            _buildSettings = new BuildSettings();
            _projectSettings = new ProjectSettings();
            _gameLoop = new GameLoop { ProjectSettings = _projectSettings };
            _stopwatch = Stopwatch.StartNew();
            _lastTicks = _stopwatch.ElapsedTicks;
            _runtimeHost.AttachInput(_device);
            _runtimeHost.AudioRuntime.Initialize();
            _writeRuntimeLog("Runtime", "Minimal browser mode enabled.");
            return;
        }

        _userAssembly = LoadUserAssembly();
        _buildSettings = LoadBuildSettings();
        _projectSettings = LoadProjectSettings();

        _writeRuntimeLog("Build", $"WorldCount={_buildSettings.Worlds.Count}, StartWorldIndex={_buildSettings.StartWorldIndex}");

        InitializeFilters();
        InitializeProjectSettings();
        LoadStartupWorld();
        BindWorldAssets();

        LuaScriptManager.Initialize(_baseDir);

        Time.Reset();
        _gameLoop = new GameLoop { ProjectSettings = _projectSettings };
        _stopwatch = Stopwatch.StartNew();
        _lastTicks = _stopwatch.ElapsedTicks;

        _runtimeHost.AttachInput(_device);
        _runtimeHost.AudioRuntime.Initialize();
    }

    public bool ShouldClose => _device.ShouldClose;

    public string GetDebugState()
    {
        return _cachedDebugState;
    }

    public string GetSceneDebugDump()
    {
        var world = WorldManager.ActiveWorld;
        if (world == null)
            return "<no active world>";

        try
        {
            return SceneSerializer.Serialize(world);
        }
        catch (Exception ex)
        {
            return $"<scene dump failed>\n{ex}";
        }
    }

    public void TickFrame()
    {
        HandlePendingWorldSwitch();

        long currentTicks = _stopwatch.ElapsedTicks;
        float deltaTime = (float)(currentTicks - _lastTicks) / Stopwatch.Frequency;
        _lastTicks = currentTicks;

        if (OperatingSystem.IsBrowser() && deltaTime > 0.1f)
            deltaTime = 0.1f;

        RuntimeProfiler.Enabled = ProfilerOverlay.ShowProfiler;
        _profilerOverlay.TickFrame();
        _device.PollEvents();
        _gameLoop.TickLogic(deltaTime);
        UiSystem.Update(_device.Width, _device.Height);

        var world = WorldManager.ActiveWorld;
        Camera? mainCam = world != null ? FindCameraRecursiveInWorld(world) : null;
        _frameCount++;
        _framesSinceLastFpsSample++;
        UpdateBrowserDebugState(world);

        long renderStart = Stopwatch.GetTimestamp();

        if (mainCam != null && world != null)
        {
            if (!_firstRenderableFrameLogged)
            {
                _writeRuntimeLog("Render", $"First renderable frame: world={world.Name}, cameraEntity={mainCam.Owner?.Name ?? "<unnamed>"}, canvases={UiSystem.ActiveCanvases.Count}");
                _firstRenderableFrameLogged = true;
            }
            if (_frameCount % 300 == 0)
                _writeRuntimeLog("Render", $"frame={_frameCount}, world={world.Name}, camera={mainCam.Owner?.Name ?? "<unnamed>"}, canvases={UiSystem.ActiveCanvases.Count}");

            _renderPipeline.RenderWorld(world, mainCam);
        }
        else
        {
            if (!_missingCameraLogged)
            {
                _writeRuntimeLog("Render", $"No active camera. world={(world?.Name ?? "<null>")}, canvases={UiSystem.ActiveCanvases.Count}");
                _missingCameraLogged = true;
            }
            _device.SetViewport(0, 0, _device.Width, _device.Height);
            _device.Clear(new Verity.Core.Color(0.2f, 0.2f, 0.2f, 1.0f));
        }

        foreach (var canvas in UiSystem.ActiveCanvases)
            UiRenderer.Render(_renderPipeline, canvas.Screen, (int)_device.Width, (int)_device.Height);

        _profilerOverlay.SetRenderTime(Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds);
        _profilerOverlay.Render(_renderPipeline, world, (int)_device.Width, (int)_device.Height);

        _device.SwapBuffers();
        Verity.Core.Debug.ClearDrawCommands();
    }

    private void UpdateBrowserDebugState(World? world)
    {
        double elapsedSeconds = _fpsSampleTimer.Elapsed.TotalSeconds;
        if (elapsedSeconds >= 0.5)
        {
            _displayFps = (float)(_framesSinceLastFpsSample / elapsedSeconds);
            _framesSinceLastFpsSample = 0;
            _fpsSampleTimer.Restart();
        }

        if (world == null)
        {
            _cachedEntityCount = 0;
            _cachedTileCount = 0;
            _lastDebugWorldStateVersion = int.MinValue;
        }
        else if (_lastDebugWorldStateVersion != world.StateVersion)
        {
            IReadOnlyList<Entity> entities = world.GetAllEntities();
            _cachedEntityCount = entities.Count;

            int tileCount = 0;
            foreach (Entity entity in entities)
            {
                Verity.Core.World.Tilemap? tilemap = entity.GetComponent<Verity.Core.World.Tilemap>();
                if (tilemap == null)
                    continue;

                tileCount += tilemap.GetAllTiles().Count();
            }

            _cachedTileCount = tileCount;
            _lastDebugWorldStateVersion = world.StateVersion;
        }

        _cachedDebugState = $"FPS: {_displayFps:0.0}\nEntities: {_cachedEntityCount}\nTiles: {_cachedTileCount}\nTextures: {_textureManager.CachedTextureCount}";
    }

    public void Dispose()
    {
        _runtimeHost.AudioRuntime.Shutdown();
        LuaScriptManager.Dispose();
        _renderPipeline.Dispose();
        _shader.Dispose();
        _textureManager.Dispose();
        _device.Dispose();
    }

    private Assembly? LoadUserAssembly()
    {
        string userScriptsPath = _contentSource.GetLoosePath("UserScripts.dll");
        if (File.Exists(userScriptsPath))
        {
            _writeRuntimeLog("Runtime", "UserScripts.dll loaded from disk.");
            return Assembly.LoadFrom(userScriptsPath);
        }

        byte[]? userScriptsBytes = _contentSource.TryReadBytes("UserScripts.dll");
        if (userScriptsBytes != null)
        {
            _writeRuntimeLog("Runtime", "UserScripts.dll loaded from resources.");
            return Assembly.Load(userScriptsBytes);
        }

        _writeRuntimeLog("Runtime", "UserScripts.dll not found.");
        return null;
    }

    private BuildSettings LoadBuildSettings()
    {
        string settingsPath = Path.Combine(_baseDir, "Assets", "BuildSettings.json");
        if (File.Exists(settingsPath))
        {
            _writeRuntimeLog("Build", $"Loading BuildSettings from disk: {settingsPath}");
            return BuildSettings.Load(settingsPath);
        }

        string? buildSettingsJson = _contentSource.TryReadText("Assets/BuildSettings.json");
        if (buildSettingsJson != null)
        {
            _writeRuntimeLog("Build", "Loading BuildSettings from embedded resources.");
            return BuildSettings.LoadFromJson(buildSettingsJson);
        }

        return new BuildSettings();
    }

    private void InitializeFilters()
    {
        string filtersPath = Path.Combine(_baseDir, "Assets", "Filters.json");
        if (File.Exists(filtersPath))
        {
            FilterManager.SavePath = filtersPath;
            FilterManager.Load();
        }
        else
        {
            string? filtersJson = _contentSource.TryReadText("Assets/Filters.json");
            if (filtersJson != null)
                FilterManager.LoadFromJson(filtersJson);
        }

        Verity.Input.Input.Enabled = true;
    }

    private ProjectSettings LoadProjectSettings()
    {
        string projectSettingsPath = Path.Combine(_baseDir, "Assets", "ProjectSettings.json");
        if (File.Exists(projectSettingsPath))
        {
            try
            {
                string json = File.ReadAllText(projectSettingsPath);
                return JsonSerializer.Deserialize(json, CoreJsonContext.Default.ProjectSettings) as ProjectSettings ?? new ProjectSettings();
            }
            catch
            {
            }
        }

        string? embeddedJson = _contentSource.TryReadText("Assets/ProjectSettings.json");
        if (embeddedJson != null)
            return JsonSerializer.Deserialize(embeddedJson, CoreJsonContext.Default.ProjectSettings) as ProjectSettings ?? new ProjectSettings();

        return new ProjectSettings();
    }

    private void InitializeProjectSettings()
    {
        Verity.Graphics.SortingLayer.SyncWithSettings(_projectSettings.SortingLayers);
        UiSystem.ProjectSettings = _projectSettings;
        UiRenderer.DefaultFontPath = _projectSettings.DefaultUiFontPath;
        UiRenderer.DefaultFontFamily = string.IsNullOrWhiteSpace(_projectSettings.DefaultUiFontPath)
            ? _fallbackFontFamily
            : string.Empty;
    }

    private void LoadStartupWorld()
    {
        string? worldRelPath = _buildSettings.Worlds.Count > 0
            ? _buildSettings.Worlds[Math.Clamp(_buildSettings.StartWorldIndex, 0, _buildSettings.Worlds.Count - 1)]
            : null;

        _writeRuntimeLog("Build", $"SelectedWorld={(string.IsNullOrWhiteSpace(worldRelPath) ? "<none>" : worldRelPath)}");

        if (!string.IsNullOrEmpty(worldRelPath))
            LoadWorldFromRelativePath(worldRelPath);

        if (WorldManager.ActiveWorld != null)
            return;

        string? fallbackJson = _contentSource.TryReadText("scene.json");
        if (fallbackJson != null)
        {
            _writeRuntimeLog("World", "Loading fallback scene.json from resources.");
            WorldLoader.LoadWorldFromJson(fallbackJson, "Main", _userAssembly);
            return;
        }

        _writeRuntimeLog("World", "No world loaded. Creating Empty World.");
        WorldManager.SetActiveWorld(WorldManager.CreateWorld("Empty World"));
        OpenStartupUiRoles(WorldManager.ActiveWorld);
    }

    private void HandlePendingWorldSwitch()
    {
        if (WorldLoader.PendingWorldName == null)
            return;

        string nextName = WorldLoader.PendingWorldName;
        WorldLoader.PendingWorldName = null;

        string? worldFile = _buildSettings.Worlds.FirstOrDefault(w => Path.GetFileNameWithoutExtension(w) == nextName);
        if (worldFile == null)
            return;

        LoadWorldFromRelativePath(worldFile);
        BindWorldAssets();
    }

    private void LoadWorldFromRelativePath(string worldRelPath)
    {
        string fullPath = Path.Combine(_baseDir, "Assets", worldRelPath);
        if (File.Exists(fullPath))
        {
            _writeRuntimeLog("World", $"Loading world from disk: {fullPath}");
            WorldLoader.LoadWorld(fullPath, _userAssembly);
            OpenStartupUiRoles(WorldManager.ActiveWorld);
            return;
        }

        string? worldJson = _contentSource.TryReadText(Path.Combine("Assets", worldRelPath));
        if (worldJson != null)
        {
            _writeRuntimeLog("World", $"Loading world from resources: {worldRelPath}");
            WorldLoader.LoadWorldFromJson(worldJson, Path.GetFileNameWithoutExtension(worldRelPath), _userAssembly);
            OpenStartupUiRoles(WorldManager.ActiveWorld);
            return;
        }

        _writeRuntimeLog("World", $"World asset not found: {worldRelPath}");
    }

    private void BindWorldAssets()
    {
        if (WorldManager.ActiveWorld == null)
            return;

        _writeRuntimeLog("World", $"ActiveWorld={WorldManager.ActiveWorld.Name}, RootEntities={WorldManager.ActiveWorld.RootEntities.Count}");
        foreach (var root in WorldManager.ActiveWorld.RootEntities)
            BindAssetsRecursive(root);
    }

    private void OpenStartupUiRoles(World? world)
    {
        if (world == null || _projectSettings.StartupUiRoles == null)
            return;

        foreach (string role in _projectSettings.StartupUiRoles)
        {
            if (string.IsNullOrWhiteSpace(role))
                continue;

            if (UiSystem.FindCanvasByRole(role, world) != null)
                continue;

            try
            {
                UiSystem.ShowRole(role, world);
                _writeRuntimeLog("UI", $"Opened startup UI role: {role}");
            }
            catch (Exception e)
            {
                _writeRuntimeLog("UI", $"Failed to open startup UI role '{role}': {e.Message}");
            }
        }
    }

    private void BindAssetsRecursive(Entity entity)
    {
        var sr = entity.GetComponent<SpriteRenderer>();
        if (sr != null && !string.IsNullOrWhiteSpace(sr.Sprite.Path))
        {
            string fullPath = AssetPathUtility.ResolvePath(_baseDir, sr.Sprite.Path, sr.Sprite.Guid);
            if (File.Exists(fullPath))
            {
                var settings = AssetPathUtility.TryGetSpriteImportSettings(fullPath);
                sr.Texture = _textureManager.Load(fullPath, settings?.Filter ?? SpriteTextureFilter.Point);
            }
            else
            {
                byte[]? data = _contentSource.TryReadBytes(sr.Sprite.Path);
                if (data != null)
                    sr.Texture = _textureManager.LoadFromMemory(data, sr.Sprite.Path);
            }
        }

        var animator = entity.GetComponent<Animator>();
        if (animator != null && !string.IsNullOrWhiteSpace(animator.ControllerPath))
        {
            string fullPath = AssetPathUtility.ResolvePath(_baseDir, animator.ControllerPath, animator.ControllerGuid);
            if (File.Exists(fullPath))
            {
                animator.ControllerPath = AssetPathUtility.Normalize(fullPath);
                if (string.IsNullOrWhiteSpace(animator.ControllerGuid))
                    animator.ControllerGuid = AssetPathUtility.TryGetGuid(fullPath);
                animator.Controller = Verity.Core.Animation.AnimatorControllerAsset.LoadFromFile(fullPath);
            }
            else
            {
                string? controllerJson = _contentSource.TryReadText(animator.ControllerPath);
                if (!string.IsNullOrWhiteSpace(controllerJson))
                    animator.Controller = Verity.Core.Animation.AnimatorControllerAsset.FromJson(controllerJson);
            }
        }

        var audioSource = entity.GetComponent<AudioSource>();
        if (audioSource?.Clip != null && !string.IsNullOrWhiteSpace(audioSource.Clip.Path))
        {
            string fullPath = AssetPathUtility.ResolvePath(_baseDir, audioSource.Clip.Path, audioSource.Clip.Guid);
            if (File.Exists(fullPath))
            {
                audioSource.Clip.Path = AssetPathUtility.Normalize(fullPath);
                if (string.IsNullOrWhiteSpace(audioSource.Clip.Guid))
                    audioSource.Clip.Guid = AssetPathUtility.TryGetGuid(fullPath);
                audioSource.Clip.PostLoad(fullPath);
            }
        }

        entity.GetComponent<AudioManager>()?.SyncGroupMap();

        foreach (var child in entity.Transform.Children)
            BindAssetsRecursive(child.Owner);
    }

    private static Camera? FindCameraRecursiveInWorld(World world)
    {
        foreach (var root in world.RootEntities)
        {
            var cam = FindCameraRecursive(root);
            if (cam != null)
                return cam;
        }

        return null;
    }

    private static Camera? FindCameraRecursive(Entity entity)
    {
        if (!entity.Active)
            return null;

        var cam = entity.GetComponent<Camera>();
        if (cam != null && cam.Enabled)
            return cam;

        foreach (var child in entity.Transform.Children)
        {
            cam = FindCameraRecursive(child.Owner);
            if (cam != null)
                return cam;
        }

        return null;
    }
}
