namespace Verity.Editor.Windows;

public class BuildSettingsWindow : EditorWindow
{
    private readonly EditorApp _app;

    public BuildSettingsWindow(EditorApp app) : base(L10n.Tr("window_buildsettings"))
    {
        _app = app;
        IsOpen = false;
    }

    public override void OnGui()
    {
        BuildSettingsEditorUi.Draw(_app);
    }

    public override void RefreshTitle() { Title = L10n.Tr("window_buildsettings"); }
}
