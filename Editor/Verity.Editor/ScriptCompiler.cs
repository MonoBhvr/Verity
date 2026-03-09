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

        var syntaxTrees = csFiles.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f)).ToList();
        var references = GetMetadataReferences();

        var compilation = CSharpCompilation.Create(
            "VerityUserScripts_" + Guid.NewGuid().ToString("N"),
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithAllowUnsafe(true));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success)
        {
            foreach (var diag in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                Verity.Core.Debug.LogError($"[ScriptCompiler] {diag}");
            return false;
        }

        var newAssembly = Assembly.Load(ms.ToArray());
        lock (_lock)
        {
            _compiledAssembly = newAssembly;
            DiscoverComponentTypes();
        }

        OnCompilationFinished?.Invoke();
        return true;
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

    public void Dispose() { _watcher?.Dispose(); _debounceTimer?.Dispose(); }
}
