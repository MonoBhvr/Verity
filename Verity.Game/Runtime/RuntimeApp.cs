using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using StbImageSharp;
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
    private sealed class BrowserWindowOutputPresenter
    {
        public static readonly BrowserWindowOutputPresenter Instance = new();

        private readonly MethodInfo? _beginMethod;
        private readonly MethodInfo? _presentMethod;
        private readonly MethodInfo? _endMethod;

        private BrowserWindowOutputPresenter()
        {
            Type? deviceType = Type.GetType("Verity.Game.Browser.BrowserRenderDevice, Verity.Game.Browser");
            if (deviceType == null)
                return;

            _beginMethod = deviceType.GetMethod("BeginWindowOutputs", BindingFlags.Instance | BindingFlags.Public);
            _presentMethod = deviceType.GetMethod("PresentWindowOutput", BindingFlags.Instance | BindingFlags.Public);
            _endMethod = deviceType.GetMethod("EndWindowOutputs", BindingFlags.Instance | BindingFlags.Public);
        }

        public bool IsAvailable => _beginMethod != null && _presentMethod != null && _endMethod != null;

        public void Begin(IRenderDevice device)
        {
            _beginMethod?.Invoke(device, null);
        }

        public void Present(
            IRenderDevice device,
            string key,
            string title,
            int x,
            int y,
            int width,
            int height,
            int order,
            string group,
            bool decorated,
            bool lockPosition,
            bool lockSize,
            RenderTexture texture)
        {
            _presentMethod?.Invoke(device, [
                key,
                title,
                x,
                y,
                width,
                height,
                order,
                group,
                decorated,
                lockPosition,
                lockSize,
                texture
            ]);
        }

        public void End(IRenderDevice device)
        {
            _endMethod?.Invoke(device, null);
        }
    }

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
    private readonly NativeMultiWindowRenderer? _multiWindowRenderer;
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

        _buildSettings = LoadBuildSettings();
        _projectSettings = _minimalBrowserMode ? new ProjectSettings() : LoadProjectSettings();

        string appTitle = string.IsNullOrWhiteSpace(_buildSettings.AppName) ? "Verity Game" : _buildSettings.AppName;
        int windowWidth = Math.Max(320, _buildSettings.WindowWidth);
        int windowHeight = Math.Max(180, _buildSettings.WindowHeight);
        bool showHostWindow = !_projectSettings.MultiWindowEnabled || OperatingSystem.IsBrowser();

        _device = _runtimeHost.CreateGraphicsDevice(appTitle, windowWidth, windowHeight, _buildSettings.WindowResizable, showHostWindow);
        _writeRuntimeLog("Runtime", $"GraphicsDevice created: {_device.Width}x{_device.Height}");

        _shader = Shader2D.Create(_device);
        _textureManager = new TextureManager(_device);
        _renderPipeline = new RenderPipeline(_device, _shader, _textureManager);
        if (_device is GraphicsDevice graphicsDevice && !OperatingSystem.IsBrowser())
            _multiWindowRenderer = new NativeMultiWindowRenderer(graphicsDevice, _renderPipeline, _projectSettings, _writeRuntimeLog);

        _profilerOverlay = new ProfilerOverlay();

        RenderPipeline.BaseAssetsPath = _baseDir;
        SceneSerializer.AssetRootPath = _baseDir;
        UiSystem.AssetsRoot = _baseDir;
        UiRenderer.DefaultFontPath = string.Empty;
        UiRenderer.DefaultFontFamily = _fallbackFontFamily;
        _renderPipeline.SetWhitePixel(_textureManager.CreateWhitePixel());
        DefaultSprites.Initialize(_textureManager);
        ApplyWindowBranding(appTitle);

        if (_minimalBrowserMode)
        {
            _gameLoop = new GameLoop { ProjectSettings = _projectSettings };
            _stopwatch = Stopwatch.StartNew();
            _lastTicks = _stopwatch.ElapsedTicks;
            _runtimeHost.AttachInput(_device);
            _runtimeHost.AudioRuntime.Initialize();
            _writeRuntimeLog("Runtime", "Minimal browser mode enabled.");
            return;
        }

        _userAssembly = LoadUserAssembly();

        _writeRuntimeLog("Build", $"WorldCount={_buildSettings.Worlds.Count}, StartWorldIndex={_buildSettings.StartWorldIndex}");

        InitializeFilters();
        InitializeProjectSettings();
        LoadStartupWorld();
        NormalizeCameraOutputsForProjectSettings();
        BindWorldAssets();

        LuaScriptManager.Initialize(_baseDir, _userAssembly);

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
        _device.PollEvents();
        int logicTicksThisFrame = _gameLoop.TickLogic(deltaTime);

        if (logicTicksThisFrame > 0)
            _profilerOverlay.TickFrame();
        uint viewportWidth = _device.Width;
        uint viewportHeight = _device.Height;
        if (UiSystem.ActiveCanvases.Count > 0)
            UiSystem.Update(viewportWidth, viewportHeight);

        var world = WorldManager.ActiveWorld;
        bool windowOutputsActive = HasVisibleWindowOutputs(world);
        List<Camera> mainWindowCameras = world == null || windowOutputsActive
            ? new List<Camera>()
            : CameraSelection.EnumerateActiveOutputs(world)
                .Where(static output => output.Target == CameraOutputTarget.MainWindow)
                .OrderBy(static output => output.Order)
                .Select(static output => output.Camera)
                .Where(static camera => camera is { Enabled: true })
                .Cast<Camera>()
                .ToList();
        Camera? mainCam = mainWindowCameras.Count > 0 ? mainWindowCameras[0] : (windowOutputsActive ? null : CameraSelection.GetDefaultCamera(world));
        Time.AdvanceFrame();
        _frameCount++;
        _framesSinceLastFpsSample++;
        UpdateBrowserDebugState(world);

        long renderStart = Stopwatch.GetTimestamp();
        if (world != null)
            _renderPipeline.RenderCameraOutputs(world, includeWindowOutputs: _projectSettings.MultiWindowEnabled);

        if (mainCam != null && world != null)
        {
            if (!_firstRenderableFrameLogged)
            {
                _writeRuntimeLog("Render", $"First renderable frame: world={world.Name}, cameraEntity={mainCam.Owner?.Name ?? "<unnamed>"}, canvases={UiSystem.ActiveCanvases.Count}");
                _firstRenderableFrameLogged = true;
            }
            if (_frameCount % 300 == 0)
                _writeRuntimeLog("Render", $"frame={_frameCount}, world={world.Name}, camera={mainCam.Owner?.Name ?? "<unnamed>"}, canvases={UiSystem.ActiveCanvases.Count}");

            if (mainWindowCameras.Count > 0)
            {
                for (int i = 0; i < mainWindowCameras.Count; i++)
                    _renderPipeline.RenderWorld(world, mainWindowCameras[i], clearTarget: i == 0);
            }
            else
            {
                _renderPipeline.RenderWorld(world, mainCam);
            }
        }
        else if (windowOutputsActive)
        {
            _device.SetViewport(0, 0, viewportWidth, viewportHeight);
            _device.Clear(new Verity.Core.Color(0.05f, 0.05f, 0.06f, 1.0f));
        }
        else
        {
            if (!_missingCameraLogged)
            {
                _writeRuntimeLog("Render", $"No active camera. world={(world?.Name ?? "<null>")}, canvases={UiSystem.ActiveCanvases.Count}");
                _missingCameraLogged = true;
            }
                _device.SetViewport(0, 0, viewportWidth, viewportHeight);
                _device.Clear(new Verity.Core.Color(0.2f, 0.2f, 0.2f, 1.0f));
        }

        foreach (var canvas in UiSystem.ActiveCanvases)
            UiRenderer.Render(_renderPipeline, canvas.Screen, (int)viewportWidth, (int)viewportHeight);

        _profilerOverlay.SetRenderTime(Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds);
        _profilerOverlay.Render(_renderPipeline, world, (int)viewportWidth, (int)viewportHeight);

        _device.SwapBuffers();
        if (_projectSettings.MultiWindowEnabled && world != null)
            PresentWindowOutputs(world);

        Verity.Core.Debug.ClearDrawCommands();
    }

    private void PresentWindowOutputs(World world)
    {
        if (OperatingSystem.IsBrowser() && TryGetBrowserWindowOutputPresenter(out var webPresenter))
        {
            webPresenter.Begin(_device);
            foreach (var output in CameraSelection.EnumerateActiveOutputs(world)
                         .Where(static output => output.Target == CameraOutputTarget.Window && output.WindowVisible)
                         .OrderBy(static output => output.Order))
            {
                string key = output.ResolveOutputName();
                if (string.IsNullOrWhiteSpace(key) || !_renderPipeline.TryGetCameraOutputTexture(key, out var texture))
                    continue;

                int width = Math.Max(1, (int)MathF.Round(output.WindowSize.X));
                int height = Math.Max(1, (int)MathF.Round(output.WindowSize.Y));
                if (width <= 1 || height <= 1)
                {
                    width = texture.Width;
                    height = texture.Height;
                }

                webPresenter.Present(
                    _device,
                    key,
                    string.IsNullOrWhiteSpace(output.OutputName) ? output.Owner.Name : output.OutputName.Trim(),
                    (int)MathF.Round(output.WindowPosition.X),
                    (int)MathF.Round(output.WindowPosition.Y),
                    width,
                    height,
                    output.Order,
                    output.WindowGroup.Trim(),
                    output.WindowDecorated,
                    output.WindowLockPosition,
                    output.WindowLockSize,
                    texture);
            }
            webPresenter.End(_device);
            return;
        }

        if (_multiWindowRenderer != null)
            _multiWindowRenderer?.Render(world);
    }

    private static bool TryGetBrowserWindowOutputPresenter(out BrowserWindowOutputPresenter presenter)
    {
        presenter = BrowserWindowOutputPresenter.Instance;
        return presenter.IsAvailable;
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
        _multiWindowRenderer?.Dispose();
        _renderPipeline.Dispose();
        _shader.Dispose();
        _textureManager.Dispose();
        _device.Dispose();
    }

    private void ApplyWindowBranding(string appTitle)
    {
        if (_device is not GraphicsDevice graphicsDevice)
            return;

        graphicsDevice.SetWindowTitle(appTitle);

        if (string.IsNullOrWhiteSpace(_buildSettings.AppIconPath))
            return;

        try
        {
            byte[]? imageBytes = null;
            string resolvedPath = AssetPathUtility.ResolvePath(_baseDir, _buildSettings.AppIconPath, _buildSettings.AppIconGuid);
            if (File.Exists(resolvedPath))
                imageBytes = File.ReadAllBytes(resolvedPath);
            else
                imageBytes = _contentSource.TryReadBytes(_buildSettings.AppIconPath);

            if (imageBytes == null)
                return;

            ImageResult image = ImageResult.FromMemory(imageBytes, ColorComponents.RedGreenBlueAlpha);
            graphicsDevice.SetWindowIcon(image.Data, image.Width, image.Height);
        }
        catch (Exception ex)
        {
            _writeRuntimeLog("Runtime", $"Failed to apply app icon: {ex.Message}");
        }
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
        NormalizeCameraOutputsForProjectSettings();
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

    private void NormalizeCameraOutputsForProjectSettings()
    {
        var world = WorldManager.ActiveWorld;
        if (world == null || !_projectSettings.MultiWindowEnabled)
            return;

        foreach (var output in CameraSelection.EnumerateActiveOutputs(world))
        {
            if (output.Target == CameraOutputTarget.MainWindow)
                output.Target = CameraOutputTarget.Window;
        }
    }

    private bool HasVisibleWindowOutputs(World? world)
    {
        return _projectSettings.MultiWindowEnabled &&
               world != null &&
               CameraSelection.EnumerateActiveOutputs(world)
                   .Any(static output => output.Target == CameraOutputTarget.Window && output.WindowVisible);
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

}
