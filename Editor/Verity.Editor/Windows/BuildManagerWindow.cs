using System.Diagnostics;
using System.Net;
using System.Text;
using Hexa.NET.ImGui;

namespace Verity.Editor.Windows;

public sealed class BuildManagerWindow : EditorWindow
{
    private const string RuntimeContentDirectoryName = "RuntimeContent";

    private enum BuildTarget
    {
        Windows,
        Web
    }

    private enum BuildConfiguration
    {
        Debug,
        Release
    }

    private readonly EditorApp _app;
    private BuildTarget _target = BuildTarget.Windows;
    private BuildConfiguration _configuration = BuildConfiguration.Debug;
    private int _webPort = 8765;
    private bool _openWebWorkloadPrompt;
    private bool _showWebWorkloadPrompt;
    private bool _pendingWebBuildAfterInstall;
    private string _webWorkloadPromptMessage = "wasm-tools workload is required for web builds.";

    public BuildManagerWindow(EditorApp app) : base(L10n.Tr("window_buildmanager"))
    {
        _app = app;
        IsOpen = false;
    }

    public override void OnGui()
    {
        if (_app.ProjectPath == null)
        {
            ImGui.Text(L10n.Tr("msg_no_project_loaded"));
            return;
        }

        var settings = _app.BuildSettings;

        ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 1f, 1f), L10n.Tr("label_build_target"));
        int targetIndex = (int)_target;
        if (ImGui.Combo(L10n.Tr("label_build_target"), ref targetIndex, $"{L10n.Tr("label_build_target_windows")}\0{L10n.Tr("label_build_target_web")}\0"))
            _target = (BuildTarget)targetIndex;

        int configurationIndex = (int)_configuration;
        if (ImGui.Combo(L10n.Tr("label_build_configuration"), ref configurationIndex, $"Debug\0Release\0"))
            _configuration = (BuildConfiguration)configurationIndex;

        if (_target == BuildTarget.Web)
        {
            ImGui.InputInt(L10n.Tr("label_web_run_port"), ref _webPort);
            _webPort = Math.Clamp(_webPort, 1024, 65535);
        }

        ImGui.Separator();
        ImGui.TextDisabled($"{L10n.Tr("label_app_name")}: {settings.AppName}");
        ImGui.TextDisabled($"{L10n.Tr("label_window_resolution")}: {settings.WindowWidth} x {settings.WindowHeight}");
        ImGui.TextDisabled($"{L10n.Tr("label_window_resizable")}: {(settings.WindowResizable ? L10n.Tr("label_yes") : L10n.Tr("label_no"))}");

        ImGui.Dummy(new System.Numerics.Vector2(0, 8));
        if (ImGui.Button(L10n.Tr("btn_open_build_settings"), new System.Numerics.Vector2(-1, 0)))
            _app.OpenWindow<BuildSettingsWindow>();

        ImGui.Separator();

        bool canInteract = !_app.IsBuilding;
        if (!canInteract)
            ImGui.BeginDisabled();

        if (ImGui.Button(L10n.Tr("btn_build"), new System.Numerics.Vector2(-1, 36)))
            StartBuild(runAfterBuild: false);
        if (ImGui.Button(L10n.Tr("btn_build_and_run"), new System.Numerics.Vector2(-1, 36)))
            StartBuild(runAfterBuild: true);
        if (ImGui.Button(L10n.Tr("btn_run_existing_build"), new System.Numerics.Vector2(-1, 36)))
            RunBuildOutput(GetPublishDirectory());
        if (ImGui.Button(L10n.Tr("btn_open_build_folder"), new System.Numerics.Vector2(-1, 36)))
            OpenBuildFolder(GetPublishDirectory());

        if (!canInteract)
            ImGui.EndDisabled();

        ImGui.Separator();
        ImGui.TextWrapped(_app.IsBuilding ? _app.BuildStatus : L10n.Tr("label_build_idle"));

        DrawWebWorkloadPrompt();
    }

    public override void RefreshTitle() => Title = L10n.Tr("window_buildmanager");

    private void StartBuild(bool runAfterBuild)
    {
        if (_app.IsBuilding || _app.ProjectPath == null)
            return;

        if (!_app.SaveActiveAssetForBuild())
            return;

        string publishDir = GetPublishDirectory();

        Task.Run(() =>
        {
            _app.IsBuilding = true;
            try
            {
                _app.BuildStatus = L10n.Tr("msg_publish_preparing_dir");
                if (Directory.Exists(publishDir))
                {
                    try { Directory.Delete(publishDir, true); } catch { }
                }

                Directory.CreateDirectory(publishDir);
                string? projectRoot = ResolveProjectRoot();
                if (projectRoot == null)
                {
                    Verity.Core.Debug.LogError("[BuildManager] Could not find solution root.");
                    return;
                }

                string gameProjDir = Path.Combine(projectRoot, "Verity.Game");
                string browserProjDir = Path.Combine(projectRoot, "Verity.Game.Browser");

                SyncRuntimeStaging(gameProjDir);

                if (_target == BuildTarget.Web && !EnsureWebWorkload(browserProjDir, runAfterBuild))
                    return;

                _app.BuildStatus = L10n.Tr("msg_publish_running_dotnet");
                string publishArgs = BuildPublishArguments(gameProjDir, browserProjDir, publishDir);
                if (!RunDotnetCommand(publishArgs, browserProjDir, 60, out string commandOutput))
                {
                    if (!string.IsNullOrWhiteSpace(commandOutput))
                    {
                        if (_target == BuildTarget.Web && commandOutput.Contains("NETSDK1147", StringComparison.OrdinalIgnoreCase))
                        {
                            QueueWebWorkloadPrompt(runAfterBuild, "Web build requires the .NET `wasm-tools` workload. Install it now?");
                            Verity.Core.Debug.LogError("[BuildManager] Web publish failed: wasm-tools workload is missing.");
                        }
                        else
                            Verity.Core.Debug.LogError($"[BuildManager] Publish failed:\n{commandOutput}");
                    }
                    else
                        Verity.Core.Debug.LogError("[BuildManager] Publish failed with no output.");
                    return;
                }

                if (_target == BuildTarget.Web)
                    PostProcessWebBuild(publishDir);

                _app.BuildStatus = L10n.Tr("msg_done");
                if (runAfterBuild)
                    RunBuildOutput(publishDir);
            }
            catch (Exception e)
            {
                Verity.Core.Debug.LogError($"[BuildManager] Error: {e.Message}");
            }
            finally
            {
                _app.IsBuilding = false;
            }
        });
    }

    private string BuildPublishArguments(string gameProjDir, string browserProjDir, string publishDir)
    {
        return _target switch
        {
            BuildTarget.Windows => _configuration == BuildConfiguration.Debug
                ? $"publish \"{Path.Combine(gameProjDir, "Verity.Game.csproj")}\" -c Debug -r win-x64 --self-contained true -p:PublishSingleFile=false -p:RuntimeShowConsole=true -p:RuntimeDiagnostics=true -p:DebugSymbols=true -p:DebugType=portable -o \"{publishDir}\""
                : $"publish \"{Path.Combine(gameProjDir, "Verity.Game.csproj")}\" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:RuntimeShowConsole=false -p:RuntimeDiagnostics=false -p:DebugSymbols=false -p:DebugType=None -o \"{publishDir}\"",
            BuildTarget.Web => $"publish \"{Path.Combine(browserProjDir, "Verity.Game.Browser.csproj")}\" -c {_configuration} -r browser-wasm -o \"{publishDir}\"",
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private void SyncRuntimeStaging(string gameProjDir)
    {
        string runtimeContentDir = Path.Combine(gameProjDir, RuntimeContentDirectoryName);
        _app.BuildStatus = L10n.Tr("msg_publish_syncing_assets");
        string gameAssets = Path.Combine(runtimeContentDir, "Assets");
        if (Directory.Exists(gameAssets))
            Directory.Delete(gameAssets, true);
        CopyDirectory(_app.AssetsPath!, gameAssets);

        _app.BuildStatus = L10n.Tr("msg_publish_syncing_build_settings");
        string settingsSrc = Path.Combine(_app.AssetsPath!, "BuildSettings.json");
        string settingsDest = Path.Combine(gameAssets, "BuildSettings.json");
        if (File.Exists(settingsSrc))
            File.Copy(settingsSrc, settingsDest, true);
        else if (File.Exists(settingsDest))
            File.Delete(settingsDest);

        _app.BuildStatus = L10n.Tr("msg_publish_compiling_scripts");
        Directory.CreateDirectory(runtimeContentDir);
        string gameDll = Path.Combine(runtimeContentDir, "UserScripts.dll");
        _app.ScriptCompiler?.CompileToFile(gameDll);
    }

    private void PostProcessWebBuild(string publishDir)
    {
        string? webRoot = ResolveWebPublishRoot(publishDir);
        if (webRoot == null)
            return;

        string indexPath = Path.Combine(webRoot, "index.html");
        if (File.Exists(indexPath))
        {
            string html = File.ReadAllText(indexPath);
            string appName = string.IsNullOrWhiteSpace(_app.BuildSettings.AppName) ? "Verity Browser Runtime" : _app.BuildSettings.AppName;
            html = ReplaceTagContents(html, "title", WebUtility.HtmlEncode(appName));

            string? faviconFileName = CopyWebIconIfPresent(webRoot);
            if (!string.IsNullOrWhiteSpace(faviconFileName) && !html.Contains("rel=\"icon\"", StringComparison.OrdinalIgnoreCase))
                html = html.Replace("</head>", $"  <link rel=\"icon\" href=\"./{faviconFileName}\" />\n</head>", StringComparison.OrdinalIgnoreCase);

            File.WriteAllText(indexPath, html);
        }
    }

    private string? CopyWebIconIfPresent(string wwwroot)
    {
        string iconPath = _app.BuildSettings.AppIconPath;
        if (string.IsNullOrWhiteSpace(iconPath) || _app.ProjectPath == null)
            return null;

        string fullIconPath = Path.IsPathRooted(iconPath)
            ? iconPath
            : Path.Combine(_app.ProjectPath, iconPath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(fullIconPath))
            return null;

        string fileName = "app-icon" + Path.GetExtension(fullIconPath);
        File.Copy(fullIconPath, Path.Combine(wwwroot, fileName), true);
        return fileName;
    }

    private void RunBuildOutput(string publishDir)
    {
        try
        {
            switch (_target)
            {
                case BuildTarget.Windows:
                    string? exe = Directory.GetFiles(publishDir, "*.exe", SearchOption.TopDirectoryOnly)
                        .OrderByDescending(path => Path.GetFileName(path).StartsWith("Verity.Game", StringComparison.OrdinalIgnoreCase))
                        .FirstOrDefault();
                    if (exe == null)
                    {
                        Verity.Core.Debug.LogError("[BuildManager] No Windows executable found in build output.");
                        return;
                    }
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = publishDir });
                    break;

                case BuildTarget.Web:
                    string? webRoot = ResolveWebPublishRoot(publishDir);
                    if (webRoot == null)
                    {
                        Verity.Core.Debug.LogError("[BuildManager] Web build output missing web root (index.html/_framework).");
                        return;
                    }
                    WebBuildPreviewServer.Start(webRoot, _webPort);
                    Process.Start(new ProcessStartInfo($"http://localhost:{_webPort}/index.html") { UseShellExecute = true });
                    break;
            }
        }
        catch (Exception e)
        {
            Verity.Core.Debug.LogError($"[BuildManager] Failed to run build output: {e.Message}");
        }
    }

    private void OpenBuildFolder(string publishDir)
    {
        Directory.CreateDirectory(publishDir);
        Process.Start("explorer.exe", publishDir.Replace("/", "\\"));
    }

    private string GetPublishDirectory()
    {
        string targetDir = _target == BuildTarget.Windows ? "Windows" : "Web";
        return Path.Combine(_app.ProjectPath!, "Build", targetDir, _configuration.ToString());
    }

    private static string? ResolveWebPublishRoot(string publishDir)
    {
        string nestedWwwroot = Path.Combine(publishDir, "wwwroot");
        if (LooksLikeWebPublishRoot(nestedWwwroot))
            return nestedWwwroot;

        if (LooksLikeWebPublishRoot(publishDir))
            return publishDir;

        return null;
    }

    private static bool LooksLikeWebPublishRoot(string path)
    {
        if (!Directory.Exists(path))
            return false;

        return File.Exists(Path.Combine(path, "index.html")) ||
               Directory.Exists(Path.Combine(path, "_framework"));
    }

    private bool EnsureWebWorkload(string browserProjDir, bool runAfterBuild)
    {
        _app.BuildStatus = "Restoring wasm workload...";
        string args = $"workload restore \"{Path.Combine(browserProjDir, "Verity.Game.Browser.csproj")}\"";
        if (!RunDotnetCommand(args, browserProjDir, 60, out string commandOutput))
        {
            if (commandOutput.Contains("NETSDK1147", StringComparison.OrdinalIgnoreCase) ||
                commandOutput.Contains("wasm-tools", StringComparison.OrdinalIgnoreCase))
            {
                QueueWebWorkloadPrompt(runAfterBuild, "Web build requires the .NET `wasm-tools` workload. Install it now?");
            }
            else if (!string.IsNullOrWhiteSpace(commandOutput))
            {
                Verity.Core.Debug.LogError($"[BuildManager] Web workload restore failed:\n{commandOutput}");
            }
            else
            {
                Verity.Core.Debug.LogError("[BuildManager] Web workload restore failed.");
            }

            return false;
        }

        _app.BuildStatus = "Checking wasm-tools...";
        if (HasInstalledWasmTools(browserProjDir))
            return true;

        QueueWebWorkloadPrompt(runAfterBuild, "Web build requires the .NET `wasm-tools` workload. Install it now?");
        return false;
    }

    private bool HasInstalledWasmTools(string workingDirectory)
    {
        if (!RunDotnetCommand("workload list", workingDirectory, 60, out string commandOutput))
            return false;

        return commandOutput.Contains("wasm-tools", StringComparison.OrdinalIgnoreCase);
    }

    private bool RunDotnetCommand(string arguments, string workingDirectory, int statusLimit, out string commandOutput)
    {
        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory
        };

        var process = Process.Start(psi);
        if (process == null)
        {
            commandOutput = string.Empty;
            Verity.Core.Debug.LogError("[BuildManager] Failed to start dotnet process.");
            return false;
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            lock (stdout)
            {
                stdout.AppendLine(e.Data);
            }
            _app.BuildStatus = e.Data.Length > statusLimit ? e.Data[..statusLimit] + "..." : e.Data;
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            lock (stderr)
            {
                stderr.AppendLine(e.Data);
            }
            _app.BuildStatus = e.Data.Length > statusLimit ? e.Data[..statusLimit] + "..." : e.Data;
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        commandOutput = string.Concat(
            stdout.ToString(),
            stderr.Length > 0 && stdout.Length > 0 ? "\n" : string.Empty,
            stderr.ToString()).Trim();

        if (process.ExitCode != 0)
        {
            commandOutput = string.IsNullOrWhiteSpace(commandOutput)
                ? $"dotnet exited with code {process.ExitCode}."
                : $"{commandOutput}\n(exit code: {process.ExitCode})";
        }

        return process.ExitCode == 0;
    }

    private void QueueWebWorkloadPrompt(bool runAfterBuild, string message)
    {
        _pendingWebBuildAfterInstall = runAfterBuild;
        _webWorkloadPromptMessage = message;
        _showWebWorkloadPrompt = true;
        _openWebWorkloadPrompt = true;
    }

    private void DrawWebWorkloadPrompt()
    {
        if (_openWebWorkloadPrompt)
        {
            ImGui.OpenPopup("WebWorkloadPrompt");
            _openWebWorkloadPrompt = false;
        }

        bool open = _showWebWorkloadPrompt;
        if (!ImGui.BeginPopupModal("WebWorkloadPrompt", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            _showWebWorkloadPrompt = open;
            return;
        }

        ImGui.TextWrapped(_webWorkloadPromptMessage);
        ImGui.Dummy(new System.Numerics.Vector2(0, 8));

        if (ImGui.Button("Install wasm-tools", new System.Numerics.Vector2(180, 0)))
        {
            _showWebWorkloadPrompt = false;
            ImGui.CloseCurrentPopup();
            StartWebWorkloadInstall(_pendingWebBuildAfterInstall);
        }

        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("ctx_cancel"), new System.Numerics.Vector2(120, 0)))
        {
            _showWebWorkloadPrompt = false;
            _pendingWebBuildAfterInstall = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void StartWebWorkloadInstall(bool rerunBuildAfterInstall)
    {
        if (_app.IsBuilding)
            return;

        Task.Run(() =>
        {
            _app.IsBuilding = true;
            bool installSucceeded = false;
            try
            {
                string? projectRoot = ResolveProjectRoot();
                if (projectRoot == null)
                {
                    Verity.Core.Debug.LogError("[BuildManager] Could not find solution root.");
                    return;
                }

                _app.BuildStatus = "Installing wasm-tools...";
                installSucceeded = RunDotnetCommand("workload install wasm-tools", projectRoot, 60, out string commandOutput);
                if (!installSucceeded)
                {
                    if (!string.IsNullOrWhiteSpace(commandOutput))
                        Verity.Core.Debug.LogError($"[BuildManager] wasm-tools install failed:\n{commandOutput}");
                    else
                        Verity.Core.Debug.LogError("[BuildManager] wasm-tools install failed.");
                }
            }
            catch (Exception e)
            {
                Verity.Core.Debug.LogError($"[BuildManager] wasm-tools install error: {e.Message}");
            }
            finally
            {
                _app.IsBuilding = false;
            }

            if (installSucceeded)
                StartBuild(rerunBuildAfterInstall);
        });
    }

    private static string? ResolveProjectRoot()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "Verity.sln")))
                return current;

            var parent = Directory.GetParent(current);
            if (parent == null)
                break;

            current = parent.FullName;
        }

        return null;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            try { File.Copy(file, target, true); } catch { }
        }
    }

    private static string ReplaceTagContents(string html, string tagName, string replacement)
    {
        string openTag = $"<{tagName}>";
        string closeTag = $"</{tagName}>";
        int openIndex = html.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
        int closeIndex = html.IndexOf(closeTag, StringComparison.OrdinalIgnoreCase);
        if (openIndex < 0 || closeIndex <= openIndex)
            return html;

        int contentStart = openIndex + openTag.Length;
        return html[..contentStart] + replacement + html[closeIndex..];
    }

    private static class WebBuildPreviewServer
    {
        private static readonly object Sync = new();
        private static HttpListener? _listener;
        private static CancellationTokenSource? _cts;
        private static string? _root;
        private static int _port;

        public static void Start(string rootPath, int port)
        {
            lock (Sync)
            {
                if (_listener != null && string.Equals(_root, rootPath, StringComparison.OrdinalIgnoreCase) && _port == port)
                    return;

                Stop();

                _root = rootPath;
                _port = port;
                _cts = new CancellationTokenSource();
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/");
                _listener.Start();
                _ = Task.Run(() => RunLoop(_listener, rootPath, _cts.Token));
            }
        }

        private static async Task RunLoop(HttpListener listener, string rootPath, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = await listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context, rootPath), token);
                }
                catch when (token.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    context?.Response.Close();
                }
            }
        }

        private static void HandleRequest(HttpListenerContext context, string rootPath)
        {
            try
            {
                string relative = context.Request.Url?.AbsolutePath.TrimStart('/') ?? string.Empty;
                if (string.IsNullOrWhiteSpace(relative))
                    relative = "index.html";

                string filePath = Path.Combine(rootPath, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(filePath))
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }

                context.Response.ContentType = GetContentType(Path.GetExtension(filePath));
                byte[] bytes = File.ReadAllBytes(filePath);
                context.Response.ContentLength64 = bytes.Length;
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                context.Response.OutputStream.Flush();
                context.Response.Close();
            }
            catch
            {
                try { context.Response.StatusCode = 500; context.Response.Close(); } catch { }
            }
        }

        private static string GetContentType(string extension) => extension.ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".json" => "application/json",
            ".wasm" => "application/wasm",
            ".css" => "text/css; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".ico" => "image/x-icon",
            _ => "application/octet-stream"
        };

        private static void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _cts = null;
            _listener = null;
            _root = null;
            _port = 0;
        }
    }
}
