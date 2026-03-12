using Verity.Editor;
using Verity.Editor.Windows;

string? initialProject = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--project" && i + 1 < args.Length)
    {
        initialProject = args[i + 1];
        break;
    }
}

using var app = new EditorApp();
app.AddWindow(new WorldViewWindow(app));
app.AddWindow(new ScreenWindow(app));
app.AddWindow(new HierarchyWindow(app));
app.AddWindow(new InspectorWindow(app));
app.AddWindow(new ConsoleWindow());
app.AddWindow(new ProjectWindow(app));
app.AddWindow(new BuildSettingsWindow(app));
app.AddWindow(new ProfilerWindow(app));
app.AddWindow(new FilterEditorWindow(app));

if (!string.IsNullOrEmpty(initialProject))
{
    if (!app.OpenProject(initialProject))
    {
        return; // Exit if failed to open (already locked)
    }
}

app.Run();
