namespace Verity.Editor;

public abstract class EditorWindow
{
    public string Title { get; protected set; }
    public bool IsOpen { get; set; } = true;
    public string WindowId { get; private set; } = string.Empty;
    public string ImGuiName => string.IsNullOrWhiteSpace(WindowId) ? Title : $"{Title}###{WindowId}";

    protected EditorWindow(string title)
    {
        Title = title;
    }

    internal void SetWindowId(string windowId)
    {
        WindowId = windowId;
    }

    public abstract void OnGui();
    public virtual void RefreshTitle() { }
}
