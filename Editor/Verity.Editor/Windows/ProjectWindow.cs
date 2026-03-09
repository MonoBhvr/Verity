using System.Diagnostics;
using System.Reflection;
using Hexa.NET.ImGui;
using Verity.Core.Serialization;
using Verity.Core.World;
using Verity.Core.Engine;
using Verity.Graphics;

namespace Verity.Editor.Windows;

public class ProjectWindow : EditorWindow
{
    private readonly EditorApp _app;
    private string? _contextDirectory;
    
    private string _inputBuffer = "";
    private string? _targetPath;
    private ModalMode _activeMode = ModalMode.None;
    private CreationType _creationType = CreationType.Folder;
    private bool _shouldOpenPopup = false;

    private enum ModalMode { None, Create, Rename }
    private enum CreationType { Script, World, Folder }

    public ProjectWindow(EditorApp app) : base("Asset") { _app = app; }

    public override void OnGui()
    {
        if (_app.AssetsPath == null) return;
        Directory.CreateDirectory(_app.AssetsPath);
        _contextDirectory ??= _app.AssetsPath;

        if (_shouldOpenPopup) { ImGui.OpenPopup("AssetInputModal"); _shouldOpenPopup = false; }
        DrawInputModal();

        ImGui.TextDisabled($"Project: {_app.CurrentProjectName}"); ImGui.Separator();

        if (ImGui.BeginChild("AssetTreeContainer", new System.Numerics.Vector2(0, 0), ImGuiChildFlags.None, ImGuiWindowFlags.NoMove))
        {
            DrawDirectoryNode(_app.AssetsPath, true);
            if (ImGui.BeginPopupContextWindow("AssetBgContext", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
            {
                var target = ResolveContextDirectory();
                if (ImGui.BeginMenu("Create"))
                {
                    if (ImGui.MenuItem("World")) OpenCreatePopup(target, CreationType.World);
                    if (ImGui.MenuItem("Script")) OpenCreatePopup(target, CreationType.Script);
                    if (ImGui.MenuItem("Folder")) OpenCreatePopup(target, CreationType.Folder);
                    ImGui.EndMenu();
                }
                ImGui.Separator();
                if (ImGui.MenuItem("Show in Explorer") && _app.AssetsPath != null) Process.Start("explorer.exe", _app.AssetsPath.Replace("/", "\\"));
                ImGui.EndPopup();
            }
            ImGui.EndChild();
        }
    }

    private unsafe void DrawDirectoryNode(string path, bool isRoot = false)
    {
        var normalizedPath = path.Replace("\\", "/");
        var name = isRoot ? "Assets" : Path.GetFileName(path);
        ImGui.PushID(normalizedPath);
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (isRoot) flags |= ImGuiTreeNodeFlags.DefaultOpen;
        bool opened = ImGui.TreeNodeEx("##node", flags, name);
        if (ImGui.IsItemClicked()) { _contextDirectory = path; EditorSelection.SelectedAssetPath = null; }
        if (!isRoot && ImGui.BeginDragDropSource()) { EditorSelection.DraggedAssetPath = normalizedPath; ImGui.SetDragDropPayload("ASSET_PATH", null, 0); ImGui.Text($"Move Folder: {name}"); ImGui.EndDragDropSource(); }
        if (ImGui.BeginDragDropTarget()) { unsafe { var payload = ImGui.AcceptDragDropPayload("ASSET_PATH"); if (payload.Handle != null && EditorSelection.DraggedAssetPath != null) { MoveAsset(EditorSelection.DraggedAssetPath, normalizedPath); EditorSelection.DraggedAssetPath = null; } } ImGui.EndDragDropTarget(); }
        if (ImGui.BeginPopupContextItem("FolderContext")) { _contextDirectory = path; if (ImGui.MenuItem("Show in Explorer")) Process.Start("explorer.exe", path.Replace("/", "\\")); ImGui.Separator(); if (!isRoot) { if (ImGui.MenuItem("Rename")) OpenRenamePopup(path); ImGui.Separator(); } if (ImGui.BeginMenu("Create")) { if (ImGui.MenuItem("World")) OpenCreatePopup(path, CreationType.World); if (ImGui.MenuItem("Script")) OpenCreatePopup(path, CreationType.Script); if (ImGui.MenuItem("Folder")) OpenCreatePopup(path, CreationType.Folder); ImGui.EndMenu(); } ImGui.EndPopup(); }
        if (opened) { foreach (var d in Directory.GetDirectories(path).OrderBy(Path.GetFileName)) DrawDirectoryNode(d, false); foreach (var f in Directory.GetFiles(path).OrderBy(Path.GetFileName)) { var normalizedFile = f.Replace("\\", "/"); var fileName = Path.GetFileName(f); ImGui.PushID(normalizedFile); bool selected = string.Equals(EditorSelection.SelectedAssetPath, normalizedFile, StringComparison.OrdinalIgnoreCase); if (ImGui.Selectable(fileName, selected, ImGuiSelectableFlags.SpanAllColumns)) { EditorSelection.SelectedAssetPath = normalizedFile; _contextDirectory = Path.GetDirectoryName(f); } if (ImGui.BeginDragDropSource()) { EditorSelection.DraggedAssetPath = normalizedFile; ImGui.SetDragDropPayload("ASSET_PATH", null, 0); ImGui.Text($"Move File: {fileName}"); ImGui.EndDragDropSource(); } if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(0)) OnAssetDoubleClicked(normalizedFile); if (ImGui.BeginPopupContextItem("FileContext")) { EditorSelection.SelectedAssetPath = normalizedFile; if (ImGui.MenuItem("Show in Explorer")) Process.Start("explorer.exe", $"/select,\"{f.Replace("/", "\\")}\""); if (ImGui.MenuItem("Rename")) OpenRenamePopup(normalizedFile); if (ImGui.MenuItem("Delete")) { File.Delete(f); } ImGui.EndPopup(); } ImGui.PopID(); } ImGui.TreePop(); }
        ImGui.PopID();
    }

    private void MoveAsset(string source, string targetDir)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(targetDir)) return;
        source = source.Replace("\\", "/"); targetDir = targetDir.Replace("\\", "/");
        if (source == targetDir) return;
        var sourceDir = Path.GetDirectoryName(source);
        if (sourceDir != null && sourceDir.Replace("\\", "/") == targetDir) return;
        try { var dest = Path.Combine(targetDir, Path.GetFileName(source)); if (File.Exists(source)) File.Move(source, dest); else if (Directory.Exists(source)) Directory.Move(source, dest); if (EditorSelection.SelectedAssetPath == source) EditorSelection.SelectedAssetPath = dest; } catch (Exception e) { Verity.Core.Debug.LogError($"[Asset] Move Failed: {e.Message}"); }
    }

    private void OpenCreatePopup(string dir, CreationType type) { _activeMode = ModalMode.Create; _creationType = type; _targetPath = dir; _inputBuffer = type switch { CreationType.Script => "NewScript", CreationType.World => "NewWorld", _ => "NewFolder" }; _shouldOpenPopup = true; }
    private void OpenRenamePopup(string path) { _activeMode = ModalMode.Rename; _targetPath = path; _inputBuffer = Path.GetFileNameWithoutExtension(path); _shouldOpenPopup = true; }

    private unsafe void DrawInputModal()
    {
        var viewport = ImGui.GetMainViewport(); var center = new System.Numerics.Vector2(viewport.Pos.X + viewport.Size.X * 0.5f, viewport.Pos.Y + viewport.Size.Y * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal("AssetInputModal", null, ImGuiWindowFlags.AlwaysAutoResize)) { ImGui.Text(_activeMode == ModalMode.Create ? $"Create {_creationType}" : "Rename Asset"); ImGui.Separator(); if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere(); ImGui.InputText("Name", ref _inputBuffer, 64); var btnSize = new System.Numerics.Vector2(120, 0); if (ImGui.Button("OK", btnSize) || ImGui.IsKeyPressed(ImGuiKey.Enter)) { if (_activeMode == ModalMode.Create) FinalizeCreate(); else if (_activeMode == ModalMode.Rename) FinalizeRename(); ImGui.CloseCurrentPopup(); } ImGui.SameLine(); if (ImGui.Button("Cancel", btnSize) || ImGui.IsKeyPressed(ImGuiKey.Escape)) ImGui.CloseCurrentPopup(); ImGui.EndPopup(); }
    }

    private void FinalizeCreate()
    {
        if (_targetPath == null || string.IsNullOrWhiteSpace(_inputBuffer)) return;
        try { switch (_creationType) { case CreationType.Script: File.WriteAllText(Path.Combine(_targetPath, _inputBuffer + ".cs"), $"using Verity.Core.ECS;\n\npublic class {_inputBuffer} : Script\n{{\n    public override void Start() {{ }}\n    public override void Update() {{ }}\n}}"); break; case CreationType.World: var w = new World(_inputBuffer); var c = w.CreateEntity("Main Camera"); c.AddComponent<Camera>(); var p = Path.Combine(_targetPath, _inputBuffer + ".verity"); File.WriteAllText(p, SceneSerializer.Serialize(w)); LoadWorldByPath(p); break; case CreationType.Folder: Directory.CreateDirectory(Path.Combine(_targetPath, _inputBuffer)); break; } } catch (Exception e) { Verity.Core.Debug.LogError(e.Message); }
    }

    private void FinalizeRename()
    {
        if (_targetPath == null || string.IsNullOrWhiteSpace(_inputBuffer)) return;
        try { var dir = Path.GetDirectoryName(_targetPath)!; var next = Path.Combine(dir, _inputBuffer + Path.GetExtension(_targetPath)).Replace("\\", "/"); if (File.Exists(_targetPath)) File.Move(_targetPath, next); else if (Directory.Exists(_targetPath)) Directory.Move(_targetPath, next); if (EditorSelection.SelectedAssetPath == _targetPath) EditorSelection.SelectedAssetPath = next; } catch (Exception e) { Verity.Core.Debug.LogError(e.Message); }
    }

    private void OnAssetDoubleClicked(string path) { if (path.EndsWith(".verity")) LoadWorldByPath(path); else if (path.EndsWith(".cs")) Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }
    public void LoadWorldByPath(string path) {
        if (!File.Exists(path)) return;
        var w = WorldManager.CreateOrReplaceWorld(Path.GetFileNameWithoutExtension(path));
        SceneSerializer.Deserialize(w, File.ReadAllText(path), _app.ScriptCompiler?.CompiledAssembly);

        // Re-bind textures for all sprite renderers
        foreach (var entity in w.GetAllEntities()) {
            var sr = entity.GetComponent<SpriteRenderer>();
            if (sr != null && !string.IsNullOrWhiteSpace(sr.Sprite.Path)) {
                var fullPath = Path.Combine(_app.ProjectPath!, sr.Sprite.Path);
                if (File.Exists(fullPath)) {
                    sr.Texture = _app.TextureManager.Load(fullPath);
                }
            }
        }

        WorldManager.SetActiveWorld(w);
    }

    public void CreateWorldInProject() => OpenCreatePopup(_app.AssetsPath!, CreationType.World);
    public void SaveActiveWorldAsAsset() { if (WorldManager.ActiveWorld == null || _app.AssetsPath == null) return; var p = Path.Combine(_app.AssetsPath, $"{WorldManager.ActiveWorld.Name}.verity"); File.WriteAllText(p, SceneSerializer.Serialize(WorldManager.ActiveWorld)); }

    public void BuildAndRun() { /* Removed per user request */ }

    public void PublishSingleFile()
    {
        if (_app.IsBuilding || _app.ProjectPath == null) return;
        Task.Run(() => {
            _app.IsBuilding = true;
            try {
                _app.BuildStatus = "Preparing publish directory...";
                var publishDir = Path.Combine(_app.ProjectPath, "Build");
                if (Directory.Exists(publishDir)) try { Directory.Delete(publishDir, true); } catch {}
                Directory.CreateDirectory(publishDir);

                var projectRoot = ResolveProjectRoot();
                if (projectRoot == null) { Verity.Core.Debug.LogError("[Publish] Could not find solution root."); return; }
                var gameProjDir = Path.Combine(projectRoot, "Verity.Game");

                // 1. Sync Assets to Game Project temporarily for embedding
                _app.BuildStatus = "Syncing Assets to Game Engine...";
                var gameAssets = Path.Combine(gameProjDir, "Assets");
                if (Directory.Exists(gameAssets)) Directory.Delete(gameAssets, true);
                CopyDirectory(_app.AssetsPath!, gameAssets);

                // 2. Sync BuildSettings.json
                _app.BuildStatus = "Syncing Build Settings...";
                var settingsSrc = Path.Combine(_app.ProjectPath, "BuildSettings.json");
                if (File.Exists(settingsSrc)) File.Copy(settingsSrc, Path.Combine(gameProjDir, "BuildSettings.json"), true);

                // 3. Compile Scripts
                _app.BuildStatus = "Compiling Script Library...";
                var gameDll = Path.Combine(gameProjDir, "UserScripts.dll");
                _app.ScriptCompiler?.CompileToFile(gameDll);

                // 4. Run dotnet publish
                _app.BuildStatus = "Running .NET Publish (May take a minute)...";
                var psi = new ProcessStartInfo("dotnet", $"publish \"{Path.Combine(gameProjDir, "Verity.Game.csproj")}\" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o \"{publishDir}\"") {
                    CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
                };
                var proc = Process.Start(psi);
                while (!proc!.StandardOutput.EndOfStream) {
                    var line = proc.StandardOutput.ReadLine();
                    if (line != null) _app.BuildStatus = line.Length > 40 ? line.Substring(0, 40) + "..." : line;
                }
                proc.WaitForExit();

                if (proc.ExitCode == 0) {
                    _app.BuildStatus = "Done!";
                    Process.Start("explorer.exe", publishDir);
                } else {
                    Verity.Core.Debug.LogError("[Publish] Publish failed. See console.");
                }
            } catch (Exception e) { Verity.Core.Debug.LogError($"[Publish] Error: {e.Message}"); }
            finally { _app.IsBuilding = false; }
        });
    }

    private string? ResolveProjectRoot() {
        var curr = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(curr)) { if (File.Exists(Path.Combine(curr, "Verity.sln"))) return curr; var p = Directory.GetParent(curr); if (p == null) break; curr = p.FullName; }
        return null;
    }

    private static void CopyDirectory(string source, string dest) {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories)) {
            var rel = Path.GetRelativePath(source, f); var d = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(d)!); try { File.Copy(f, d, true); } catch {}
        }
    }

    private string ResolveContextDirectory() => _contextDirectory ?? _app.AssetsPath ?? AppContext.BaseDirectory;
}
