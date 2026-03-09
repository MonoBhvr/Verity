using System.Reflection;
using System.Text.Json;
using Verity.Core;
using Verity.Core.ECS;
using Verity.Core.Engine;
using Verity.Core.Serialization;
using Verity.Core.World;
using Verity.Graphics;
using Verity.Input;
using System.Diagnostics;
using System.Drawing;

namespace Verity.Game;

internal class Program
{
    private static void Main(string[] args) {
        var baseDir = AppContext.BaseDirectory;
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyName = assembly.GetName().Name ?? "Verity.Game";

        Console.WriteLine($"[Runtime] Verity Engine v{VerityCore.Version}");

        var device = GraphicsDevice.Create("Verity Game", 1280, 720);
        var shader = Shader2D.Create(device);
        var textureManager = new TextureManager(device);
        var renderPipeline = new RenderPipeline(device, shader, textureManager);
        renderPipeline.BaseAssetsPath = baseDir;
        renderPipeline.SetWhitePixel(textureManager.CreateWhitePixel());
        DefaultSprites.Initialize(textureManager);
        
        Assembly? userAssembly = null;
        var dllPath = Path.Combine(baseDir, "UserScripts.dll");
        if (File.Exists(dllPath)) {
            userAssembly = Assembly.LoadFrom(dllPath);
            Console.WriteLine("[Runtime] UserScripts.dll loaded from disk.");
        }
        else {
            var resourceName = $"{assemblyName}.UserScripts.dll";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null) {
                byte[] data = new byte[stream.Length];
                stream.ReadExactly(data, 0, data.Length);
                userAssembly = Assembly.Load(data);
                Console.WriteLine("[Runtime] UserScripts.dll loaded from resources.");
            }
        }

        BuildSettings? buildSettings = null;
        var settingsPath = Path.Combine(baseDir, "BuildSettings.json");
        if (File.Exists(settingsPath)) buildSettings = BuildSettings.Load(settingsPath);
        else {
            var json = ReadResourceString(assembly, $"{assemblyName}.BuildSettings.json");
            if (json != null) buildSettings = BuildSettings.LoadFromJson(json);
        }
        buildSettings ??= new BuildSettings();

        string? worldRelPath = (buildSettings.Worlds.Count > 0) 
            ? buildSettings.Worlds[Math.Clamp(buildSettings.StartWorldIndex, 0, buildSettings.Worlds.Count - 1)] 
            : null;

        if (!string.IsNullOrEmpty(worldRelPath))
        {
            var fullPath = Path.Combine(baseDir, "Assets", worldRelPath);
            if (File.Exists(fullPath)) WorldLoader.LoadWorld(fullPath, userAssembly);
            else {
                var resName = $"{assemblyName}.Assets.{worldRelPath.Replace("/", ".").Replace("\\", ".")}";
                var json = ReadResourceString(assembly, resName);
                if (json != null) WorldLoader.LoadWorldFromJson(json, Path.GetFileNameWithoutExtension(worldRelPath), userAssembly);
            }
        }

        if (WorldManager.ActiveWorld == null)
        {
            var fallbackJson = ReadResourceString(assembly, $"{assemblyName}.scene.json");
            if (fallbackJson != null) WorldLoader.LoadWorldFromJson(fallbackJson, "Main", userAssembly);
            else WorldManager.SetActiveWorld(WorldManager.CreateWorld("Empty World"));
        }

        if (WorldManager.ActiveWorld != null)
        {
            foreach (var root in WorldManager.ActiveWorld.RootEntities)
                FixTexturePathsRecursive(root, textureManager, assembly, assemblyName);
        }

        Time.Reset();
        var gameLoop = new GameLoop();
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        long lastTicks = stopwatch.ElapsedTicks;

        device.Window.OnSdlEvent += Verity.Input.Input.ProcessEvent;

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
                    if (File.Exists(fullPath)) WorldLoader.LoadWorld(fullPath, userAssembly);
                    else {
                        var resName = $"{assemblyName}.Assets.{worldFile.Replace("/", ".").Replace("\\", ".")}";
                        var json = ReadResourceString(assembly, resName);
                        if (json != null) WorldLoader.LoadWorldFromJson(json, Path.GetFileNameWithoutExtension(worldFile), userAssembly);
                    }
                    foreach (var root in WorldManager.ActiveWorld!.RootEntities)
                        FixTexturePathsRecursive(root, textureManager, assembly, assemblyName);
                }
            }

            var world = WorldManager.ActiveWorld;
            if (world == null) 
            {
                device.PollEvents();
                device.Clear(Verity.Core.Color.Black);
                device.SwapBuffers();
                continue;
            }

            long currentTicks = stopwatch.ElapsedTicks;
            float deltaTime = (float)(currentTicks - lastTicks) / Stopwatch.Frequency;
            lastTicks = currentTicks;

            // --- INPUT HANDLING ---
            Verity.Input.Input.BeginFrame();
            device.PollEvents();
            // EndFrame must be called AFTER logic to transition key states for next frame
            // But we must also ensure key presses are detected in the current frame's Update.

            gameLoop.TickLogic(deltaTime);
            
            Verity.Input.Input.EndFrame();
            // ----------------------

            uint winWidth = device.Window.GetWidth();
            uint winHeight = device.Window.GetHeight();
            device.Gl.Viewport(0, 0, winWidth, winHeight);
            
            Camera? mainCam = FindCameraRecursiveInWorld(world);
            if (mainCam != null)
            {
                mainCam.SetViewportSize((int)winWidth, (int)winHeight);
                renderPipeline.RenderWorld(world, mainCam);
            }
            else device.Clear(new Verity.Core.Color(0.2f, 0.2f, 0.2f, 1.0f));
            
            device.SwapBuffers();
        }
        
        renderPipeline.Dispose();
        shader.Dispose();
        textureManager.Dispose();
        device.Dispose();
    }

    private static string? ReadResourceString(Assembly asm, string name)
    {
        using var stream = asm.GetManifestResourceStream(name);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    private static void FixTexturePathsRecursive(Entity entity, TextureManager tm, Assembly asm, string asmName)
    {
        var sr = entity.GetComponent<SpriteRenderer>();
        if (sr != null && !string.IsNullOrWhiteSpace(sr.Sprite.Path))
        {
            // Use logic that handles "Assets/" prefix correctly
            string relPath = sr.Sprite.Path;
            string fullPath = Path.IsPathRooted(relPath) ? relPath : Path.Combine(AppContext.BaseDirectory, relPath);

            if (File.Exists(fullPath)) {
                sr.Texture = tm.Load(fullPath);
            }
            else {
                // Try resource
                var resName = $"{asmName}.{sr.Sprite.Path.Replace("/", ".").Replace("\\", ".")}";
                using var stream = asm.GetManifestResourceStream(resName);
                if (stream != null) {
                    byte[] data = new byte[stream.Length];
                    stream.ReadExactly(data, 0, data.Length);
                    sr.Texture = tm.LoadFromMemory(data, sr.Sprite.Path);
                }
            }
        }
        foreach (var child in entity.Transform.Children) FixTexturePathsRecursive(child.Owner, tm, asm, asmName);
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
}
