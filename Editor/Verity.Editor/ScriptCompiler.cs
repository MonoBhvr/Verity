using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Verity.Core.ECS;

namespace Verity.Editor;

public class ScriptCompiler : IDisposable
{
    private const string GeneratedScriptGlobals = """
global using Verity.Core;
global using Verity.Core.ECS;
global using Verity.Core.UI;
global using Verity.Graphics;
global using Verity.Input;
global using Vector2 = Verity.Core.Vector2;
global using Vector3 = Verity.Core.Vector3;
global using Color = Verity.Core.Color;
""";

    private readonly string _assetsPath;
    private Assembly? _compiledAssembly;
    private AssemblyLoadContext? _compiledAssemblyLoadContext;
    private readonly List<Type> _componentTypes = [];
    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private const int DebounceDelayMs = 500;

    public event Action? OnCompilationFinished;

    private bool _isPaused;
    private bool _needsCompile;
    private bool _hasCompilationErrors;

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (_isPaused == value) return;
            _isPaused = value;
            if (!_isPaused && _needsCompile)
            {
                _needsCompile = false;
                Compile();
            }
        }
    }

    public IReadOnlyList<Type> ComponentTypes
    {
        get { lock (_lock) return _componentTypes.ToList(); }
    }

    public Assembly? CompiledAssembly
    {
        get { lock (_lock) return _compiledAssembly; }
    }

    public bool HasCompilationErrors
    {
        get { lock (_lock) return _hasCompilationErrors; }
    }

    public ScriptCompiler(string assetsPath)
    {
        _assetsPath = assetsPath;
        if (!string.IsNullOrEmpty(_assetsPath) && Directory.Exists(_assetsPath))
        {
            InitializeWatcher();
        }
    }

    private void InitializeWatcher()
    {
        if (string.IsNullOrEmpty(_assetsPath)) return;
        var absoluteAssetsPath = Path.GetFullPath(_assetsPath);
        if (!Directory.Exists(absoluteAssetsPath)) return;

        _watcher = new FileSystemWatcher(absoluteAssetsPath, "*.cs")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileChanged;

        _watcher.EnableRaisingEvents = true;
        Verity.Core.Debug.Log($"[ScriptCompiler] Watching: {absoluteAssetsPath}");
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        _needsCompile = true;
        if (IsPaused) return;

        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(_ => { if (!IsPaused) Compile(); }, null, DebounceDelayMs, Timeout.Infinite);
    }

    public bool Compile()
    {
        if (IsPaused) { _needsCompile = true; return false; }
        if (!Directory.Exists(_assetsPath))
        {
            lock (_lock)
                _hasCompilationErrors = false;
            return false;
        }

        var csFiles = Directory.GetFiles(_assetsPath, "*.cs", SearchOption.AllDirectories);
        if (csFiles.Length == 0)
        {
            lock (_lock)
            {
                UnloadCompiledAssembly();
                _componentTypes.Clear();
                _hasCompilationErrors = false;
            }
            OnCompilationFinished?.Invoke();
            return true;
        }

        Verity.Core.Debug.Log($"[ScriptCompiler] Starting compilation of {csFiles.Length} files...");

        var fileContents = csFiles.ToDictionary(f => f, f => File.ReadAllText(f));
        var syntaxTrees = fileContents.Select(kvp => CSharpSyntaxTree.ParseText(kvp.Value, path: kvp.Key)).ToList();
        syntaxTrees.Insert(0, CSharpSyntaxTree.ParseText(GeneratedScriptGlobals, path: "__Verity.ScriptGlobals.g.cs"));
        var references = GetMetadataReferences();

        var compilation = CreateCompilation(syntaxTrees, references);

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success)
        {
            var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            var brokenFiles = errors.Select(d => d.Location.SourceTree?.FilePath).Where(p => p != null).Distinct().ToHashSet();

            Verity.Core.Debug.LogError($"[ScriptCompiler] Compilation failed with {errors.Count} errors in {brokenFiles.Count} files.");
            
            foreach (var diag in errors)
            {
                var lineSpan = diag.Location.GetLineSpan();
                var fileName = Path.GetFileName(lineSpan.Path);
                Verity.Core.Debug.LogError($"  - {fileName}({lineSpan.StartLinePosition.Line + 1},{lineSpan.StartLinePosition.Character + 1}): {diag.GetMessage()}");
            }

            lock (_lock)
                _hasCompilationErrors = true;

            return false;
        }

        ms.Seek(0, SeekOrigin.Begin);
        var loadContext = new AssemblyLoadContext($"Verity.UserScripts_{Guid.NewGuid():N}", isCollectible: true);
        Assembly assembly;
        ms.Seek(0, SeekOrigin.Begin);
        try
        {
            assembly = loadContext.LoadFromStream(ms);
        }
        catch
        {
            loadContext.Unload();
            throw;
        }

        lock (_lock)
        {
            UnloadCompiledAssembly();
            _compiledAssemblyLoadContext = loadContext;
            _compiledAssembly = assembly;
            _hasCompilationErrors = false;
            _componentTypes.Clear();
            foreach (var type in assembly.GetTypes())
            {
                if (typeof(Component).IsAssignableFrom(type) && !type.IsAbstract && type.IsPublic)
                {
                    _componentTypes.Add(type);
                }
            }
        }

        Verity.Core.Debug.Log($"[ScriptCompiler] Compilation successful. Loaded {assembly.FullName}");
        OnCompilationFinished?.Invoke();
        return true;
    }

    public bool CompileToFile(string outputPath)
    {
        if (!Directory.Exists(_assetsPath))
        {
            lock (_lock)
                _hasCompilationErrors = false;
            return false;
        }
        var csFiles = Directory.GetFiles(_assetsPath, "*.cs", SearchOption.AllDirectories);
        if (csFiles.Length == 0)
        {
            lock (_lock)
                _hasCompilationErrors = false;
            return true;
        }

        var fileContents = csFiles.ToDictionary(f => f, f => File.ReadAllText(f));
        var syntaxTrees = fileContents.Select(kvp => CSharpSyntaxTree.ParseText(kvp.Value, path: kvp.Key)).ToList();
        syntaxTrees.Insert(0, CSharpSyntaxTree.ParseText(GeneratedScriptGlobals, path: "__Verity.ScriptGlobals.g.cs"));
        var references = GetMetadataReferences();

        var compilation = CreateCompilation(syntaxTrees, references);
        var result = compilation.Emit(outputPath);

        if (!result.Success)
        {
            foreach (var diag in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                Verity.Core.Debug.LogError($"[ScriptCompiler] Export Error: {diag.GetMessage()}");

            lock (_lock)
                _hasCompilationErrors = true;

            return false;
        }

        lock (_lock)
            _hasCompilationErrors = false;

        return true;
    }

    private CSharpCompilation CreateCompilation(List<SyntaxTree> syntaxTrees, List<MetadataReference> references)
    {
        return CSharpCompilation.Create(
            $"Verity.UserScripts_{Guid.NewGuid():N}",
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithAllowUnsafe(true)
        );
    }

    private List<MetadataReference> GetMetadataReferences()
    {
        var references = new List<MetadataReference>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location)) continue;
            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        return references;
    }

    private void UnloadCompiledAssembly()
    {
        _compiledAssembly = null;
        _compiledAssemblyLoadContext?.Unload();
        _compiledAssemblyLoadContext = null;
    }

    public List<Type> GetAllAddableComponentTypes()
    {
        var types = new List<Type>();
        
        // 1. Engine types
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            string name = asm.GetName().Name ?? "";
            if (name.StartsWith("Verity.Core") || name.StartsWith("Verity.Graphics"))
            {
                try {
                    foreach (var type in asm.GetTypes())
                    {
                        if (typeof(Component).IsAssignableFrom(type) && !type.IsAbstract && type.IsPublic && type != typeof(Transform))
                            types.Add(type);
                    }
                } catch { }
            }
        }

        // 2. User types
        lock (_lock)
        {
            foreach (var type in _componentTypes)
            {
                if (!types.Contains(type)) types.Add(type);
            }
        }

        return types.OrderBy(t => t.Name).ToList();
    }

    public List<Type> GetAllEnumTypes()
    {
        var types = new List<Type>();
        
        // From User Assembly
        lock (_lock)
        {
            if (_compiledAssembly != null)
            {
                foreach (var type in _compiledAssembly.GetTypes())
                {
                    if (type.IsEnum && type.IsPublic) types.Add(type);
                }
            }
        }

        // From Engine Assemblies
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            string name = asm.GetName().Name ?? "";
            if (name.StartsWith("Verity.Core"))
            {
                try {
                    foreach (var type in asm.GetTypes())
                    {
                        if (type.IsEnum && type.IsPublic) types.Add(type);
                    }
                } catch { }
            }
        }

        return types.OrderBy(t => t.FullName).ToList();
    }

    public List<Type> GetUserScripts()
    {
        var types = new List<Type>();
        lock (_lock)
        {
            if (_compiledAssembly != null)
            {
                foreach (var type in _compiledAssembly.GetTypes())
                {
                    if (typeof(Script).IsAssignableFrom(type) && !type.IsAbstract && type.IsPublic) types.Add(type);
                }
            }
        }
        return types.OrderBy(t => t.FullName).ToList();
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
        lock (_lock)
        {
            _componentTypes.Clear();
            UnloadCompiledAssembly();
        }
    }
}
