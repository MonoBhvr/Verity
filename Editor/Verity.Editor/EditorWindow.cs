namespace Verity.Editor;

public abstract class EditorWindow
{
    public string Title { get; }
    public bool IsOpen { get; set; } = true;

    protected EditorWindow(string title)
    {
        Title = title;
    }

    public abstract void OnGui();
}
