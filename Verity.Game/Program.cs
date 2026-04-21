using System;
using System.Reflection;
using System.Text.Json;
using Verity.Core;
using Verity.Core.ECS;
using Verity.Core.Engine;
using Verity.Core.UI;
using Verity.Core.Serialization;
using Verity.Core.World;
using Verity.Graphics;
using Verity.Filter;
using Verity.Input;
using Verity.Core.Audio;
using Verity.Core.Scripting;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Verity.Game;

internal class Program
{
    private static string? baseDir_Static;
    private static StreamWriter? logWriter_Static;
    private static bool RuntimeConsoleEnabled =>
#if VERITY_RUNTIME_CONSOLE
        true;
#else
        false;
#endif
    private static bool RuntimeDiagnosticsEnabled =>
#if VERITY_RUNTIME_DIAGNOSTICS
        true;
#else
        false;
#endif

    private static void Main(string[] args) {
        if (RuntimeConsoleEnabled)
            EnsureDebugConsole();
        var executableBaseDir = AppContext.BaseDirectory;
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyName = assembly.GetName().Name ?? "Verity.Game";
        var baseDir = PrepareRuntimeContentRoot(assembly, assemblyName, executableBaseDir);
        baseDir_Static = baseDir;
        if (RuntimeDiagnosticsEnabled)
            ConfigureRuntimeLogging(executableBaseDir, baseDir);

        WriteRuntimeLog("Runtime", $"Verity Engine v{VerityCore.Version}");
        WriteRuntimeLog("Runtime", $"ExecutableBaseDir={executableBaseDir}");
        WriteRuntimeLog("Runtime", $"ContentBaseDir={baseDir}");

        var device = GraphicsDevice.Create("Verity Game", 1280, 720);
        WriteRuntimeLog("Runtime", $"GraphicsDevice created: {device.Window.GetWidth()}x{device.Window.GetHeight()}");
        var shader = Shader2D.Create(device);
        var textureManager = new TextureManager(device);
        var renderPipeline = new RenderPipeline(device, shader, textureManager);
        var profilerOverlay = new ProfilerOverlay();
        RenderPipeline.BaseAssetsPath = baseDir;
        SceneSerializer.AssetRootPath = baseDir;
        UiSystem.AssetsRoot = baseDir;
        UiRenderer.DefaultFontPath = string.Empty;
        UiRenderer.DefaultFontFamily = FindRuntimeUiFontFamily();
        renderPipeline.SetWhitePixel(textureManager.CreateWhitePixel());
        DefaultSprites.Initialize(textureManager);
        
        Assembly? userAssembly = null;
        var dllPath = Path.Combine(baseDir, "UserScripts.dll");
        if (File.Exists(dllPath)) {
            userAssembly = Assembly.LoadFrom(dllPath);
            WriteRuntimeLog("Runtime", "UserScripts.dll loaded from disk.");
        }
        else {
            var resourceName = $"{assemblyName}.UserScripts.dll";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null) {
                byte[] data = new byte[stream.Length];
                stream.ReadExactly(data, 0, data.Length);
                userAssembly = Assembly.Load(data);
                WriteRuntimeLog("Runtime", "UserScripts.dll loaded from resources.");
            }
            else WriteRuntimeLog("Runtime", "UserScripts.dll not found.");
        }

        BuildSettings? buildSettings = null;
        var settingsPath = Path.Combine(baseDir, "Assets", "BuildSettings.json");
        if (File.Exists(settingsPath)) {
            WriteRuntimeLog("Build", $"Loading BuildSettings from disk: {settingsPath}");
            buildSettings = BuildSettings.Load(settingsPath);
        }
        else {
            var json = ReadResourceString(assembly, $"{assemblyName}.Assets.BuildSettings.json");
            if (json != null) {
                WriteRuntimeLog("Build", "Loading BuildSettings from embedded resources.");
                buildSettings = BuildSettings.LoadFromJson(json);
            }
        }
        buildSettings ??= new BuildSettings();
        WriteRuntimeLog("Build", $"WorldCount={buildSettings.Worlds.Count}, StartWorldIndex={buildSettings.StartWorldIndex}");

        // Initialize Input Filters
        var filtersPath = Path.Combine(baseDir, "Assets", "Filters.json");
        if (File.Exists(filtersPath)) {
            FilterManager.SavePath = filtersPath;
            FilterManager.Load();
        } else {
            var filtersJson = ReadResourceString(assembly, $"{assemblyName}.Assets.Filters.json");
            if (filtersJson != null) FilterManager.LoadFromJson(filtersJson);
        }
        Verity.Input.Input.Enabled = true;

        // Initialize Project Settings (Gravity, Layers, etc.)
        ProjectSettings? projectSettings = null;
        var projSettingsPath = Path.Combine(baseDir, "Assets", "ProjectSettings.json");
        if (File.Exists(projSettingsPath)) {
            try {
                var json = File.ReadAllText(projSettingsPath);
                projectSettings = JsonSerializer.Deserialize<ProjectSettings>(json, new JsonSerializerOptions { 
                    Converters = { new Verity.Core.Serialization.Vector2Converter(), new Verity.Core.Serialization.ColorConverter() }
                });
            } catch { }
        } else {
            var json = ReadResourceString(assembly, $"{assemblyName}.Assets.ProjectSettings.json");
            if (json != null) projectSettings = JsonSerializer.Deserialize<ProjectSettings>(json, new JsonSerializerOptions { 
                Converters = { new Verity.Core.Serialization.Vector2Converter(), new Verity.Core.Serialization.ColorConverter() }
            });
        }
        projectSettings ??= new ProjectSettings();
        Verity.Graphics.SortingLayer.SyncWithSettings(projectSettings.SortingLayers);
        UiSystem.ProjectSettings = projectSettings;
        UiRenderer.DefaultFontPath = projectSettings.DefaultUiFontPath;
        UiRenderer.DefaultFontFamily = string.IsNullOrWhiteSpace(projectSettings.DefaultUiFontPath)
            ? FindRuntimeUiFontFamily()
            : string.Empty;

        string? worldRelPath = (buildSettings.Worlds.Count > 0) 
            ? buildSettings.Worlds[Math.Clamp(buildSettings.StartWorldIndex, 0, buildSettings.Worlds.Count - 1)] 
            : null;
        WriteRuntimeLog("Build", $"SelectedWorld={(string.IsNullOrWhiteSpace(worldRelPath) ? "<none>" : worldRelPath)}");

        if (!string.IsNullOrEmpty(worldRelPath))
        {
            var fullPath = Path.Combine(baseDir, "Assets", worldRelPath);
            if (File.Exists(fullPath)) {
                WriteRuntimeLog("World", $"Loading world from disk: {fullPath}");
                WorldLoader.LoadWorld(fullPath, userAssembly);
                OpenStartupUiRoles(projectSettings, WorldManager.ActiveWorld);
            }
            else {
                var resName = $"{assemblyName}.Assets.{worldRelPath.Replace("/", ".").Replace("\\", ".")}";
                var worldResJson = ReadResourceString(assembly, resName);
                if (worldResJson != null) {
                    WriteRuntimeLog("World", $"Loading world from resources: {resName}");
                    WorldLoader.LoadWorldFromJson(worldResJson, Path.GetFileNameWithoutExtension(worldRelPath), userAssembly);
                    OpenStartupUiRoles(projectSettings, WorldManager.ActiveWorld);
                }
                else WriteRuntimeLog("World", $"World asset not found: {worldRelPath}");
            }
        }

        if (WorldManager.ActiveWorld == null)
        {
            var fallbackJson = ReadResourceString(assembly, $"{assemblyName}.scene.json");
            if (fallbackJson != null) {
                WriteRuntimeLog("World", "Loading fallback scene.json from resources.");
                WorldLoader.LoadWorldFromJson(fallbackJson, "Main", userAssembly);
            }
            else {
                WriteRuntimeLog("World", "No world loaded. Creating Empty World.");
                WorldManager.SetActiveWorld(WorldManager.CreateWorld("Empty World"));
                OpenStartupUiRoles(projectSettings, WorldManager.ActiveWorld);
            }
        }

        if (WorldManager.ActiveWorld != null)
        {
            WriteRuntimeLog("World", $"ActiveWorld={WorldManager.ActiveWorld.Name}, RootEntities={WorldManager.ActiveWorld.RootEntities.Count}");
            foreach (var root in WorldManager.ActiveWorld.RootEntities)
                BindAssetsRecursive(root, textureManager, assembly, assemblyName);
        }

        LuaScriptManager.Initialize(baseDir);

        Time.Reset();
        var gameLoop = new GameLoop { ProjectSettings = projectSettings };
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        long lastTicks = stopwatch.ElapsedTicks;

        device.Window.OnSdlEvent += Verity.Input.Input.ProcessEvent;

        // Initialize Audio System
        AudioSystem.Initialize();

        int frameCount = 0;
        bool firstRenderableFrameLogged = false;
        bool missingCameraLogged = false;

        while (!device.ShouldClose)
        {
            if (WorldLoader.PendingWorldName != null)
            {
                var nextName = WorldLoader.PendingWorldName;
                WorldLoader.PendingWorldName = null;
                var worldFile = buildSettings.Worlds.FirstOrDefault(w => Path.GetFileNameWithoutExtension(w) == nextName);
                if (worldFile != null)
                {
                    var fullPath = Path.Combine(baseDir, "Assets", worldFile);
                    if (File.Exists(fullPath))
                    {
                        WorldLoader.LoadWorld(fullPath, userAssembly);
                        OpenStartupUiRoles(projectSettings, WorldManager.ActiveWorld);
                    }
                    else {
                        var resName = $"{assemblyName}.Assets.{worldFile.Replace("/", ".").Replace("\\", ".")}";
                        var pendingJson = ReadResourceString(assembly, resName);
                        if (pendingJson != null)
                        {
                            WorldLoader.LoadWorldFromJson(pendingJson, Path.GetFileNameWithoutExtension(worldFile), userAssembly);
                            OpenStartupUiRoles(projectSettings, WorldManager.ActiveWorld);
                        }
                    }
                    if (WorldManager.ActiveWorld != null) {
                        WriteRuntimeLog("World", $"Switched to world={WorldManager.ActiveWorld.Name}, RootEntities={WorldManager.ActiveWorld.RootEntities.Count}");
                        foreach (var root in WorldManager.ActiveWorld.RootEntities)
                            BindAssetsRecursive(root, textureManager, assembly, assemblyName);
                    }
                }
            }

            long currentTicks = stopwatch.ElapsedTicks;
            float deltaTime = (float)(currentTicks - lastTicks) / Stopwatch.Frequency;
            lastTicks = currentTicks;

            RuntimeProfiler.Enabled = ProfilerOverlay.ShowProfiler;
            profilerOverlay.TickFrame();
            Verity.Input.Input.NewLogicTick();
            device.PollEvents();
            gameLoop.TickLogic(deltaTime);
            UiSystem.Update(device.Window.GetWidth(), device.Window.GetHeight());

            var world = WorldManager.ActiveWorld;
            Camera? mainCam = world != null ? FindCameraRecursiveInWorld(world) : null;
            frameCount++;

            long renderStart = Stopwatch.GetTimestamp();

            if (mainCam != null && world != null)
            {
                if (!firstRenderableFrameLogged)
                {
                    WriteRuntimeLog("Render", $"First renderable frame: world={world.Name}, cameraEntity={mainCam.Owner?.Name ?? "<unnamed>"}, canvases={UiSystem.ActiveCanvases.Count}");
                    firstRenderableFrameLogged = true;
                }
                if (frameCount % 300 == 0)
                    WriteRuntimeLog("Render", $"frame={frameCount}, world={world.Name}, camera={mainCam.Owner?.Name ?? "<unnamed>"}, canvases={UiSystem.ActiveCanvases.Count}");

                renderPipeline.RenderWorld(world, mainCam);
            }
            else 
            {
                if (!missingCameraLogged)
                {
                    WriteRuntimeLog("Render", $"No active camera. world={(world?.Name ?? "<null>")}, canvases={UiSystem.ActiveCanvases.Count}");
                    missingCameraLogged = true;
                }
                device.Gl.Viewport(0, 0, device.Window.GetWidth(), device.Window.GetHeight());
                device.Clear(new Verity.Core.Color(0.2f, 0.2f, 0.2f, 1.0f));
            }

            foreach (var canvas in UiSystem.ActiveCanvases)
            {
                UiRenderer.Render(renderPipeline, canvas.Screen, (int)device.Window.GetWidth(), (int)device.Window.GetHeight());
            }

            profilerOverlay.SetRenderTime(Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds);
            profilerOverlay.Render(renderPipeline, world, (int)device.Window.GetWidth(), (int)device.Window.GetHeight());

            device.SwapBuffers();
            Verity.Core.Debug.ClearDrawCommands();
        }
        
        AudioSystem.Shutdown();
        LuaScriptManager.Dispose();
        renderPipeline.Dispose();
        shader.Dispose();
        textureManager.Dispose();
        device.Dispose();
        logWriter_Static?.Dispose();
    }

    private static void ConfigureRuntimeLogging(string executableBaseDir, string contentBaseDir)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string logPath = TryGetPreferredLogPath(executableBaseDir, contentBaseDir);
        logWriter_Static = new StreamWriter(File.Open(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };

        Verity.Core.Debug.OnLog += static (message, level) =>
        {
            string prefix = level switch
            {
                LogLevel.Warning => "Warn",
                LogLevel.Error => "Error",
                _ => "Info"
            };
            WriteRuntimeLog(prefix, message);
        };

        AppDomain.CurrentDomain.UnhandledException += static (_, eventArgs) =>
        {
            WriteRuntimeLog("Fatal", eventArgs.ExceptionObject?.ToString() ?? "Unhandled exception");
        };

        WriteRuntimeLog("Runtime", $"Logging to {logPath}");
    }

    private static string TryGetPreferredLogPath(string executableBaseDir, string contentBaseDir)
    {
        string[] candidates =
        [
            Path.Combine(executableBaseDir, "runtime.log"),
            Path.Combine(contentBaseDir, "runtime.log")
        ];

        foreach (string candidate in candidates)
        {
            try
            {
                string? directory = Path.GetDirectoryName(candidate);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                using FileStream stream = File.Open(candidate, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
                return candidate;
            }
            catch
            {
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "runtime.log");
    }

    private static void WriteRuntimeLog(string category, string message)
    {
        if (!RuntimeDiagnosticsEnabled && !RuntimeConsoleEnabled)
            return;

        string line = $"[{DateTime.Now:HH:mm:ss}] [{category}] {message}";
        Console.WriteLine(line);
        logWriter_Static?.WriteLine(line);
    }

    private static void EnsureDebugConsole()
    {
        if (GetConsoleWindow() != IntPtr.Zero)
            return;

        AllocConsole();
    }

    private static string? ReadResourceString(Assembly asm, string name)
    {
        using var stream = asm.GetManifestResourceStream(name);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string PrepareRuntimeContentRoot(Assembly assembly, string assemblyName, string executableBaseDir)
    {
        if (HasLooseRuntimeContent(executableBaseDir))
            return executableBaseDir;

        string[] resourceNames = assembly.GetManifestResourceNames();
        if (!resourceNames.Any(name => name.StartsWith($"{assemblyName}.Assets.", StringComparison.Ordinal)))
            return executableBaseDir;

        string cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Verity",
            "RuntimeCache",
            GetRuntimeContentVersion(assembly));
        string markerPath = Path.Combine(cacheRoot, ".verity-runtime-cache");

        if (Directory.Exists(cacheRoot) && File.Exists(markerPath))
            return cacheRoot;

        if (Directory.Exists(cacheRoot))
            Directory.Delete(cacheRoot, true);

        Directory.CreateDirectory(cacheRoot);

        foreach (string resourceName in resourceNames)
        {
            if (!TryMapResourceToRelativePath(resourceName, assemblyName, out string relativePath))
                continue;

            using Stream? resourceStream = assembly.GetManifestResourceStream(resourceName);
            if (resourceStream == null)
                continue;

            string outputPath = Path.Combine(cacheRoot, relativePath);
            string? outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDir))
                Directory.CreateDirectory(outputDir);

            using FileStream fileStream = File.Create(outputPath);
            resourceStream.CopyTo(fileStream);
        }

        File.WriteAllText(markerPath, assembly.GetName().Version?.ToString() ?? "runtime");
        return cacheRoot;
    }

    private static bool HasLooseRuntimeContent(string baseDir)
    {
        return Directory.Exists(Path.Combine(baseDir, "Assets")) ||
               File.Exists(Path.Combine(baseDir, "scene.json")) ||
               File.Exists(Path.Combine(baseDir, "UserScripts.dll"));
    }

    private static string GetRuntimeContentVersion(Assembly assembly)
    {
        try
        {
            string? processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
            {
                var info = new FileInfo(processPath);
                return $"{info.Length}_{info.LastWriteTimeUtc.Ticks}";
            }
        }
        catch
        {
        }

        return assembly.ManifestModule.ModuleVersionId.ToString("N");
    }

    private static bool TryMapResourceToRelativePath(string resourceName, string assemblyName, out string relativePath)
    {
        relativePath = string.Empty;

        if (resourceName.Equals($"{assemblyName}.scene.json", StringComparison.Ordinal))
        {
            relativePath = "scene.json";
            return true;
        }

        if (resourceName.Equals($"{assemblyName}.UserScripts.dll", StringComparison.Ordinal))
        {
            relativePath = "UserScripts.dll";
            return true;
        }

        if (resourceName.Equals($"{assemblyName}.BuildSettings.json", StringComparison.Ordinal))
        {
            relativePath = Path.Combine("Assets", "BuildSettings.json");
            return true;
        }

        string assetsPrefix = $"{assemblyName}.Assets.";
        if (!resourceName.StartsWith(assetsPrefix, StringComparison.Ordinal))
            return false;

        string suffix = resourceName[assetsPrefix.Length..];
        if (!TryConvertManifestSuffixToAssetPath(suffix, out string assetRelativePath))
            return false;

        relativePath = Path.Combine("Assets", assetRelativePath);
        return true;
    }

    private static bool TryConvertManifestSuffixToAssetPath(string suffix, out string assetRelativePath)
    {
        string[] knownExtensions =
        [
            ".fontasset.meta",
            ".uiprefab.meta",
            ".uistyle.meta",
            ".animtile.meta",
            ".ruletile.meta",
            ".blueprint.meta",
            ".controller.meta",
            ".shader.meta",
            ".style.meta",
            ".verity.meta",
            ".json.meta",
            ".png.meta",
            ".jpg.meta",
            ".jpeg.meta",
            ".bmp.meta",
            ".wav.meta",
            ".ogg.meta",
            ".mp3.meta",
            ".ttf.meta",
            ".otf.meta",
            ".tile.meta",
            ".ui.meta",
            ".fontasset",
            ".uiprefab",
            ".uistyle",
            ".animtile",
            ".ruletile",
            ".blueprint",
            ".controller",
            ".shader",
            ".style",
            ".verity",
            ".json",
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp",
            ".wav",
            ".ogg",
            ".mp3",
            ".ttf",
            ".otf",
            ".tile",
            ".ui"
        ];

        foreach (string extension in knownExtensions)
        {
            if (!suffix.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                continue;

            string stem = suffix[..^extension.Length];
            assetRelativePath = stem.Replace('.', Path.DirectorySeparatorChar) + extension;
            return true;
        }

        assetRelativePath = suffix.Replace('.', Path.DirectorySeparatorChar);
        return true;
    }

    private static string FindRuntimeUiFontFamily()
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

    private static void OpenStartupUiRoles(ProjectSettings projectSettings, World? world)
    {
        if (world == null || projectSettings.StartupUiRoles == null)
            return;

        foreach (string role in projectSettings.StartupUiRoles)
        {
            if (string.IsNullOrWhiteSpace(role))
                continue;

            if (UiSystem.FindCanvasByRole(role, world) != null)
                continue;

            try
            {
                UiSystem.ShowRole(role, world);
                WriteRuntimeLog("UI", $"Opened startup UI role: {role}");
            }
            catch (Exception e)
            {
                WriteRuntimeLog("UI", $"Failed to open startup UI role '{role}': {e.Message}");
            }
        }
    }
    private static void BindAssetsRecursive(Entity entity, TextureManager tm, Assembly asm, string asmName)
    {
        var sr = entity.GetComponent<SpriteRenderer>();
        if (sr != null && !string.IsNullOrWhiteSpace(sr.Sprite.Path))
        {
            string relPath = sr.Sprite.Path;
            string fullPath = AssetPathUtility.ResolvePath(baseDir_Static ?? AppContext.BaseDirectory, relPath, sr.Sprite.Guid);
            if (File.Exists(fullPath))
            {
                var settings = AssetPathUtility.TryGetSpriteImportSettings(fullPath);
                sr.Texture = tm.Load(fullPath, settings?.Filter ?? SpriteTextureFilter.Point);
            }
            else {
                var resName = $"{asmName}.{sr.Sprite.Path.Replace("/", ".").Replace("\\", ".")}";
                using var stream = asm.GetManifestResourceStream(resName);
                if (stream != null) {
                    byte[] data = new byte[stream.Length];
                    stream.ReadExactly(data, 0, data.Length);
                    sr.Texture = tm.LoadFromMemory(data, sr.Sprite.Path);
                }
            }
        }

        var animator = entity.GetComponent<Animator>();
        if (animator != null && !string.IsNullOrWhiteSpace(animator.ControllerPath))
        {
            string relPath = animator.ControllerPath;
            string fullPath = AssetPathUtility.ResolvePath(baseDir_Static ?? AppContext.BaseDirectory, relPath, animator.ControllerGuid);
            if (File.Exists(fullPath))
            {
                animator.ControllerPath = AssetPathUtility.Normalize(fullPath);
                if (string.IsNullOrWhiteSpace(animator.ControllerGuid))
                    animator.ControllerGuid = AssetPathUtility.TryGetGuid(fullPath);
                animator.Controller = Verity.Core.Animation.AnimatorControllerAsset.LoadFromFile(fullPath);
            }
            else
            {
                var resName = $"{asmName}.{animator.ControllerPath.Replace("/", ".").Replace("\\", ".")}";
                var controllerJson = ReadResourceString(asm, resName);
                if (!string.IsNullOrWhiteSpace(controllerJson))
                    animator.Controller = Verity.Core.Animation.AnimatorControllerAsset.FromJson(controllerJson);
            }
        }

        var audioSource = entity.GetComponent<Verity.Core.Audio.AudioSource>();
        if (audioSource?.Clip != null && !string.IsNullOrWhiteSpace(audioSource.Clip.Path))
        {
            string relPath = audioSource.Clip.Path;
            string fullPath = AssetPathUtility.ResolvePath(baseDir_Static ?? AppContext.BaseDirectory, relPath, audioSource.Clip.Guid);
            if (File.Exists(fullPath))
            {
                audioSource.Clip.Path = AssetPathUtility.Normalize(fullPath);
                if (string.IsNullOrWhiteSpace(audioSource.Clip.Guid))
                    audioSource.Clip.Guid = AssetPathUtility.TryGetGuid(fullPath);
                audioSource.Clip.PostLoad(fullPath);
            }
        }

        entity.GetComponent<Verity.Core.Audio.AudioManager>()?.SyncGroupMap();

        foreach (var child in entity.Transform.Children) BindAssetsRecursive(child.Owner, tm, asm, asmName);
    }
    private static Camera? FindCameraRecursiveInWorld(World world)
    {
        foreach (var root in world.RootEntities) {
            var cam = FindCameraRecursive(root);
            if (cam != null) return cam;
        }
        return null;
    }
    private static Camera? FindCameraRecursive(Entity entity)
    {
        if (!entity.Active) return null;
        var cam = entity.GetComponent<Camera>();
        if (cam != null && cam.Enabled) return cam;
        foreach (var child in entity.Transform.Children) {
            cam = FindCameraRecursive(child.Owner);
            if (cam != null) return cam;
        }
        return null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetConsoleWindow();
}


