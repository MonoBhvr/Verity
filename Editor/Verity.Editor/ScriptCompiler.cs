using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Verity.Core.ECS;

namespace Verity.Editor;

public class ScriptCompiler : IDisposable
{
    private readonly string _assetsPath;
    private Assembly? _compiledAssembly;
    private readonly List<Type> _componentTypes = [];
    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private const int DebounceDelayMs = 500;

    public event Action? OnCompilationFinished;

    public IReadOnlyList<Type> ComponentTypes
    {
        get { lock (_lock) return _componentTypes.ToList(); }
    }

    public Assembly? CompiledAssembly
    {
        get { lock (_lock) return _compiledAssembly; }
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
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(_ => Compile(), null, DebounceDelayMs, Timeout.Infinite);
    }

    public bool Compile()
    {
        if (!Directory.Exists(_assetsPath)) return false;

        var csFiles = Directory.GetFiles(_assetsPath, "*.cs", SearchOption.AllDirectories);
        if (csFiles.Length == 0)
        {
            lock (_lock) { _componentTypes.Clear(); _compiledAssembly = null; }
            OnCompilationFinished?.Invoke();
            return true;
        }

        Verity.Core.Debug.Log($"[ScriptCompiler] Starting compilation of {csFiles.Length} files...");

        var fileContents = csFiles.ToDictionary(f => f, f => File.ReadAllText(f));
        var syntaxTrees = fileContents.Select(kvp => CSharpSyntaxTree.ParseText(kvp.Value, path: kvp.Key)).ToList();
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

            // [FIX] 컴파일 실패 시 기존 어셈블리를 그대로 유지하여 데이터 유실 방지
            // lock (_lock) { _compiledAssembly = null; _componentTypes.Clear(); }
            OnCompilationFinished?.Invoke();
            return false;
        }

        Verity.Core.Debug.Log("[ScriptCompiler] Compilation successful!");
        LoadAssembly(ms.ToArray());
        return true;
    }

    private CSharpCompilation CreateCompilation(IEnumerable<SyntaxTree> trees, IEnumerable<MetadataReference> refs)
    {
        return CSharpCompilation.Create(
            "VerityUserScripts_" + Guid.NewGuid().ToString("N"),
            trees,
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true));
    }

    private void LoadAssembly(byte[] rawAssembly)
    {
        var newAssembly = Assembly.Load(rawAssembly);
        lock (_lock)
        {
            _compiledAssembly = newAssembly;
            DiscoverComponentTypes();
        }
        OnCompilationFinished?.Invoke();
    }

    public bool CompileToFile(string outputPath)
    {
        if (!Directory.Exists(_assetsPath)) return false;
        var csFiles = Directory.GetFiles(_assetsPath, "*.cs", SearchOption.AllDirectories);
        if (csFiles.Length == 0) return true;

        var syntaxTrees = csFiles.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f)).ToList();
        var compilation = CSharpCompilation.Create("UserScripts", syntaxTrees, GetMetadataReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true));

        var result = compilation.Emit(outputPath);
        return result.Success;
    }

    private void DiscoverComponentTypes()
    {
        if (_compiledAssembly == null) return;
        lock (_lock)
        {
            _componentTypes.Clear();
            foreach (var type in _compiledAssembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface || !type.IsPublic) continue;
                if (typeof(Component).IsAssignableFrom(type) && type != typeof(Transform))
                {
                    _componentTypes.Add(type);
                    Verity.Core.Debug.Log($"[ScriptCompiler] Found: {type.Name}");
                }
            }
        }
    }

    private static List<MetadataReference> GetMetadataReferences()
    {
        var refs = new List<MetadataReference>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location)) continue;
            refs.Add(MetadataReference.CreateFromFile(assembly.Location));
        }
        return refs;
    }

    public List<Type> GetAllAddableComponentTypes()
    {
        var types = new List<Type>();
        var engineAssemblies = new[] { typeof(Component).Assembly, typeof(Verity.Graphics.Camera).Assembly };
        foreach (var asm in engineAssemblies)
        {
            foreach (var type in asm.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                
                // Explicitly exclude Transform and Camera as per user request
                if (type == typeof(Transform) || type == typeof(Verity.Graphics.Camera)) continue;
                
                if (typeof(Component).IsAssignableFrom(type)) types.Add(type);
            }
        }
        lock (_lock) { types.AddRange(_componentTypes); }
        return types.OrderBy(t => t.Name).ToList();
    }

    public List<Type> GetAllEnumTypes()
    {
        var types = new List<Type>();
        var assemblies = new HashSet<Assembly>
        {
            typeof(Component).Assembly,
            typeof(Verity.Graphics.Camera).Assembly,
            typeof(Verity.Input.Input).Assembly,
            typeof(Verity.Core.World.World).Assembly
        };

        foreach (var asm in assemblies)
        {
            foreach (var type in asm.GetTypes())
            {
                if (type.IsEnum && type.IsPublic) types.Add(type);
            }
        }

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
        return types.OrderBy(t => t.FullName).ToList();
    }

    public void Dispose() { _watcher?.Dispose(); _debounceTimer?.Dispose(); }
}
