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
    private static string? baseDir_Static;

    private static void Main(string[] args) {
        var baseDir = AppContext.BaseDirectory;
        baseDir_Static = baseDir;
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
        var settingsPath = Path.Combine(baseDir, "Assets", "BuildSettings.json");
        if (File.Exists(settingsPath)) buildSettings = BuildSettings.Load(settingsPath);
        else {
            var json = ReadResourceString(assembly, $"{assemblyName}.Assets.BuildSettings.json");
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
                var worldResJson = ReadResourceString(assembly, resName);
                if (worldResJson != null) WorldLoader.LoadWorldFromJson(worldResJson, Path.GetFileNameWithoutExtension(worldRelPath), userAssembly);
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
                        var pendingJson = ReadResourceString(assembly, resName);
                        if (pendingJson != null) WorldLoader.LoadWorldFromJson(pendingJson, Path.GetFileNameWithoutExtension(worldFile), userAssembly);
                    }
                    if (WorldManager.ActiveWorld != null) {
                        foreach (var root in WorldManager.ActiveWorld.RootEntities)
                            FixTexturePathsRecursive(root, textureManager, assembly, assemblyName);
                    }
                }
            }

            long currentTicks = stopwatch.ElapsedTicks;
            float deltaTime = (float)(currentTicks - lastTicks) / Stopwatch.Frequency;
            lastTicks = currentTicks;

            Verity.Input.Input.NewLogicTick();
            device.PollEvents();
            gameLoop.TickLogic(deltaTime);

            var world = WorldManager.ActiveWorld;
            Camera? mainCam = world != null ? FindCameraRecursiveInWorld(world) : null;

            if (mainCam != null && world != null)
            {
                renderPipeline.RenderWorld(world, mainCam);
            }
            else 
            {
                device.Gl.Viewport(0, 0, device.Window.GetWidth(), device.Window.GetHeight());
                device.Clear(new Verity.Core.Color(0.2f, 0.2f, 0.2f, 1.0f));
            }
            
            device.SwapBuffers();
            Verity.Core.Debug.ClearDrawCommands();
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
            string relPath = sr.Sprite.Path;
            string fullPath = Path.IsPathRooted(relPath) ? relPath : Path.Combine(baseDir_Static ?? AppContext.BaseDirectory, relPath);
            if (File.Exists(fullPath)) sr.Texture = tm.Load(fullPath);
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
