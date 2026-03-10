using Verity.Editor;
using Verity.Editor.Windows;

using var app = new EditorApp();
app.AddWindow(new WorldViewWindow(app));
app.AddWindow(new ScreenWindow(app));
app.AddWindow(new HierarchyWindow(app));
app.AddWindow(new InspectorWindow(app));
app.AddWindow(new ConsoleWindow());
app.AddWindow(new ProjectWindow(app));
app.AddWindow(new BuildSettingsWindow(app));
app.AddWindow(new ProfilerWindow(app));
app.AddWindow(new FilterEditorWindow());
app.Run();
