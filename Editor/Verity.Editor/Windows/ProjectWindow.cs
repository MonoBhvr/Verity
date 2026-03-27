using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hexa.NET.ImGui;
using Irodori.Backend.OpenGL;
using Irodori.Texture;
using Verity.Core;
using Verity.Core.Engine;
using Verity.Core.Serialization;
using Verity.Core.UI;
using Verity.Core.World;
using Verity.Graphics;

namespace Verity.Editor.Windows;

public unsafe class ProjectWindow : EditorWindow
{
    private readonly EditorApp _app;
    private string? _currentDirectory;
    private string? _contextDirectory;
    private string? _selectedFolderPath;

    private string _inputBuffer = "";
    private string? _targetPath;
    private string? _targetSpriteAssetPath;
    private string? _targetSpriteId;
    private string? _creationShaderPath;
    private string _pathBarBuffer = "";
    private ModalMode _activeMode = ModalMode.None;
    private CreationType _creationType = CreationType.Folder;
    private bool _shouldOpenPopup = false;

    private float _thumbnailSize = 52f;
    private float _leftPanelWidth = 240f;
    private string? _cachedBrowserDirectory;
    private string[] _cachedBrowserDirectories = Array.Empty<string>();
    private string[] _cachedBrowserFiles = Array.Empty<string>();
    private readonly Dictionary<string, string[]> _cachedTreeDirectories = new(StringComparer.OrdinalIgnoreCase);

    private enum ModalMode
    {
        None,
        Create,
        RenameAsset,
        RenameSprite
    }

    private enum BrowserViewMode
    {
        Details,
        Tiles,
        Icons
    }

    private enum BrowserItemKind
    {
        Folder,
        File,
        Sprite
    }

    public enum CreationType
    {
        Script,
        World,
        Folder,
        Shader,
        Style,
        UiScreen,
        UiStyle,
        Tile,
        AnimatedTile,
        RuleTile
    }

    private readonly struct BrowserPreview
    {
        public BrowserPreview(TextureObjectUploaded? texture, Vector2 uvMin, Vector2 uvMax, int width, int height)
        {
            Texture = texture;
            UvMin = uvMin;
            UvMax = uvMax;
            Width = width;
            Height = height;
        }

        public TextureObjectUploaded? Texture { get; }
        public Vector2 UvMin { get; }
        public Vector2 UvMax { get; }
        public int Width { get; }
        public int Height { get; }
    }

    private readonly struct BrowserItem
    {
        public BrowserItem(BrowserItemKind kind, string assetPath, string title, string subtitle, string? spriteId = null)
        {
            Kind = kind;
            AssetPath = assetPath;
            Title = title;
            Subtitle = subtitle;
            SpriteId = spriteId ?? string.Empty;
        }

        public BrowserItemKind Kind { get; }
        public string AssetPath { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public string SpriteId { get; }
        public bool IsSprite => Kind == BrowserItemKind.Sprite;
        public bool IsFolder => Kind == BrowserItemKind.Folder;
    }

    public ProjectWindow(EditorApp app) : base(L10n.Tr("window_project"))
    {
        _app = app;
    }

    public override void OnGui()
    {
        if (_app.AssetsPath == null)
            return;

        Directory.CreateDirectory(_app.AssetsPath);
        EnsureBrowserState();
        HandleShortcuts();

        if (_shouldOpenPopup)
        {
            ImGui.OpenPopup("AssetInputModal");
            _shouldOpenPopup = false;
        }

        DrawInputModal();

        ImGui.TextDisabled($"{L10n.Tr("window_project")}: {_app.CurrentProjectName}");
        ImGui.Separator();

        ImGui.Columns(2, "ProjectBrowserColumns", true);
        ImGui.SetColumnWidth(0, _leftPanelWidth);

        DrawFolderTreePanel();
        ImGui.NextColumn();
        DrawBrowserToolbar();
        DrawBrowserPanel();
        DrawZoomFooter();

        _leftPanelWidth = ImGui.GetColumnWidth(0);
        ImGui.Columns(1);
    }

    public override void RefreshTitle()
    {
        Title = L10n.Tr("window_project");
    }

    private void EnsureBrowserState()
    {
        string assetsPath = NormalizePath(_app.AssetsPath!);
        if (string.IsNullOrWhiteSpace(_currentDirectory) || !Directory.Exists(_currentDirectory) || !IsWithinAssets(_currentDirectory))
            _currentDirectory = assetsPath;

        if (string.IsNullOrWhiteSpace(_contextDirectory) || !Directory.Exists(_contextDirectory) || !IsWithinAssets(_contextDirectory))
            _contextDirectory = _currentDirectory;

        if (!string.IsNullOrWhiteSpace(_selectedFolderPath) && (!Directory.Exists(_selectedFolderPath) || !IsWithinAssets(_selectedFolderPath)))
            _selectedFolderPath = null;

        string currentDisplayPath = ToProjectDisplayPath(_currentDirectory);
        if (string.IsNullOrWhiteSpace(_pathBarBuffer) || !ImGui.IsAnyItemActive())
            _pathBarBuffer = currentDisplayPath;
    }

    private void DrawFolderTreePanel()
    {
        if (!ImGui.BeginChild("ProjectFolderTree", Vector2.Zero, ImGuiChildFlags.Borders))
        {
            ImGui.EndChild();
            return;
        }

        DrawDirectoryNode(_app.AssetsPath!, true);
        ImGui.EndChild();
    }

    private void DrawBrowserToolbar()
    {
        bool canNavigateUp = _app.AssetsPath != null &&
                             _currentDirectory != null &&
                             !string.Equals(NormalizePath(_currentDirectory), NormalizePath(_app.AssetsPath), StringComparison.OrdinalIgnoreCase);
        if (!canNavigateUp)
            ImGui.BeginDisabled();
        if (ImGui.Button("^##ProjectUp", new Vector2(24, 0)) && canNavigateUp)
            NavigateUp();
        if (!canNavigateUp)
            ImGui.EndDisabled();

        ImGui.SameLine();
        DrawPathBar();
        ImGui.Separator();
    }

    private void DrawPathBar()
    {
        if (_currentDirectory == null || _app.AssetsPath == null)
            return;

        ImGui.SetNextItemWidth(-1);
        string displayPath = _pathBarBuffer;
        bool submitted = ImGui.InputText("##ProjectPathBar", ref displayPath, 512, ImGuiInputTextFlags.EnterReturnsTrue);
        if (displayPath != _pathBarBuffer)
            _pathBarBuffer = displayPath;

        if (!submitted)
            return;

        string typed = _pathBarBuffer.Trim().Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(typed))
        {
            _pathBarBuffer = ToProjectDisplayPath(_currentDirectory);
            return;
        }

        string assetsPath = NormalizePath(_app.AssetsPath);
        string target = typed.Equals("Assets", StringComparison.OrdinalIgnoreCase)
            ? assetsPath
            : NormalizePath(Path.Combine(assetsPath, typed.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ? typed["Assets/".Length..] : typed));

        if (Directory.Exists(target) && IsWithinAssets(target))
            NavigateToDirectory(target, true);
        else
            _pathBarBuffer = ToProjectDisplayPath(_currentDirectory);
    }

    private void DrawBrowserPanel()
    {
        if (!ImGui.BeginChild("ProjectBrowser", new Vector2(0, -30f), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar))
        {
            ImGui.EndChild();
            return;
        }

        var items = BuildBrowserItems();
        switch (GetBrowserViewMode())
        {
            case BrowserViewMode.Details:
                DrawBrowserItemsDetails(items);
                break;
            case BrowserViewMode.Tiles:
                DrawBrowserItemsTiles(items);
                break;
            default:
                DrawBrowserItemsIcons(items);
                break;
        }

        Vector2 remaining = ImGui.GetContentRegionAvail();
        float fillHeight = MathF.Max(remaining.Y, 80f);
        float fillWidth = MathF.Max(remaining.X, 1f);
        ImGui.InvisibleButton("##ProjectBrowserBackground", new Vector2(fillWidth, fillHeight));

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            _selectedFolderPath = null;
            _contextDirectory = _currentDirectory;
            EditorSelection.ClearAssetSelection();
            EditorSelection.SelectedEntity = null;
        }

        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var assetPayload = ImGui.AcceptDragDropPayload("ASSET_PATH");
                if (assetPayload.Handle != null && EditorSelection.DraggedAssetPath != null && EditorSelection.DraggedSpriteAsset == null)
                {
                    MoveAsset(EditorSelection.DraggedAssetPath, _currentDirectory!);
                    EditorSelection.ClearAssetDrag();
                }

                var entityPayload = ImGui.AcceptDragDropPayload("HIERARCHY_ENTITIES");
                if (entityPayload.Handle != null)
                {
                    foreach (var ent in EditorSelection.SelectedEntities)
                        _app.SaveEntityAsBlueprint(ent, _currentDirectory!);
                    EditorSelection.DraggedEntity = null;
                }
            }

            ImGui.EndDragDropTarget();
        }

        if (ImGui.BeginPopupContextItem("ProjectBrowserContext"))
        {
            _contextDirectory = _currentDirectory;
            _creationShaderPath = null;
            DrawCreateMenu(_currentDirectory!);
            ImGui.Separator();
            if (ImGui.MenuItem(L10n.Tr("menu_show_in_explorer")))
                Process.Start("explorer.exe", _currentDirectory!.Replace("/", "\\"));
            if (ImGui.MenuItem("Reload"))
                ReloadProjectBrowser();
            ImGui.EndPopup();
        }
        ImGui.EndChild();
    }

    private void DrawZoomFooter()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.14f, 0.14f, 0.17f, 1f));
        if (!ImGui.BeginChild("ProjectBrowserFooter", new Vector2(0, 26f), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.PopStyleColor();
            ImGui.EndChild();
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        Vector2 min = ImGui.GetWindowPos();
        Vector2 size = ImGui.GetWindowSize();
        Vector2 max = new(min.X + size.X, min.Y + size.Y);
        drawList.AddLine(min, new Vector2(max.X, min.Y), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.10f)), 1f);

        string zoomLabel = L10n.Tr("field_Zoom");
        float sliderWidth = 104f;
        float availableWidth = ImGui.GetContentRegionAvail().X;
        float contentHeight = ImGui.GetContentRegionAvail().Y;
        float frameHeight = ImGui.GetFrameHeight();
        Vector2 labelSize = ImGui.CalcTextSize(zoomLabel);
        float labelY = MathF.Max(0f, (contentHeight - labelSize.Y) * 0.5f);
        float sliderY = MathF.Max(0f, (contentHeight - frameHeight) * 0.5f);

        float startX = ImGui.GetCursorPosX();
        ImGui.SetCursorPos(new Vector2(startX + 8f, labelY));
        ImGui.TextDisabled(zoomLabel);

        float labelWidth = labelSize.X;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4f, 0f));
        ImGui.SetCursorPos(new Vector2(MathF.Max(startX + labelWidth + 20f, availableWidth - sliderWidth - 6f), sliderY));
        ImGui.SetNextItemWidth(sliderWidth);
        ImGui.SliderFloat("##BrowserZoom", ref _thumbnailSize, 32f, 96f, string.Empty);
        ImGui.PopStyleVar();
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    private BrowserViewMode GetBrowserViewMode()
    {
        if (_thumbnailSize <= 42f)
            return BrowserViewMode.Details;
        if (_thumbnailSize <= 68f)
            return BrowserViewMode.Tiles;
        return BrowserViewMode.Icons;
    }

    private List<BrowserItem> BuildBrowserItems()
    {
        var items = new List<BrowserItem>();
        foreach (string directory in GetBrowserDirectories(_currentDirectory!))
        {
            string normalized = NormalizePath(directory);
            items.Add(new BrowserItem(
                BrowserItemKind.Folder,
                normalized,
                Path.GetFileName(directory),
                ToProjectDisplayPath(normalized)));
        }

        foreach (string file in GetBrowserFiles(_currentDirectory!))
        {
            string normalized = NormalizePath(file);
            string subtitle = Path.GetExtension(file).ToUpperInvariant();
            SpriteImportSettings? spriteImport = null;
            if (IsImageAsset(file))
            {
                spriteImport = _app.TryGetSpriteImportSettings(normalized, false);
                if (spriteImport is { SpriteMode: SpriteImportMode.Multiple, Slices.Count: > 0 })
                    subtitle = L10n.Tr("label_slice_count", spriteImport.Slices.Count);
            }

            items.Add(new BrowserItem(
                BrowserItemKind.File,
                normalized,
                Path.GetFileName(file),
                subtitle));

            if (spriteImport is { SpriteMode: SpriteImportMode.Multiple, Slices.Count: > 0 })
            {
                foreach (var slice in spriteImport.Slices)
                {
                    items.Add(new BrowserItem(
                        BrowserItemKind.Sprite,
                        normalized,
                        slice.Name,
                        $"{slice.Width} x {slice.Height}",
                        slice.Id));
                }
            }
        }

        return items;
    }

    private void DrawBrowserItemsDetails(List<BrowserItem> items)
    {
        foreach (var item in items)
            DrawBrowserItemAsRow(item);
    }

    private void DrawBrowserItemsTiles(List<BrowserItem> items)
    {
        float cardWidth = MathF.Max(220f, _thumbnailSize * 3.2f);
        int columns = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / cardWidth));
        if (!ImGui.BeginTable("ProjectBrowserTiles", columns, ImGuiTableFlags.SizingFixedFit))
            return;

        foreach (var item in items)
        {
            ImGui.TableNextColumn();
            DrawBrowserItemAsTile(item, cardWidth - 8f);
        }

        ImGui.EndTable();
    }

    private void DrawBrowserItemsIcons(List<BrowserItem> items)
    {
        float cardWidth = MathF.Max(96f, _thumbnailSize + 32f);
        int columns = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / cardWidth));
        if (!ImGui.BeginTable("ProjectBrowserIcons", columns, ImGuiTableFlags.SizingFixedFit))
            return;

        foreach (var item in items)
        {
            ImGui.TableNextColumn();
            DrawBrowserItemAsIcon(item, cardWidth - 8f);
        }

        ImGui.EndTable();
    }

    private void DrawFolderBrowserItem(string path)
    {
        string normalized = NormalizePath(path);
        string name = Path.GetFileName(path);
        bool selected = string.Equals(_selectedFolderPath, normalized, StringComparison.OrdinalIgnoreCase);
        var row = BeginBrowserRow($"folder_{normalized}", selected, 0f);

        if (row.Clicked)
            SelectFolder(normalized);
        if (row.DoubleClicked)
            NavigateToDirectory(normalized, true);

        if (ImGui.BeginDragDropSource())
        {
            EditorSelection.BeginAssetDrag(normalized);
            ImGui.SetDragDropPayload("ASSET_PATH", null, 0);
            ImGui.Text(L10n.Tr("msg_move_folder", name));
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var assetPayload = ImGui.AcceptDragDropPayload("ASSET_PATH");
                if (assetPayload.Handle != null && EditorSelection.DraggedAssetPath != null && EditorSelection.DraggedSpriteAsset == null)
                {
                    MoveAsset(EditorSelection.DraggedAssetPath, normalized);
                    EditorSelection.ClearAssetDrag();
                }

                var entityPayload = ImGui.AcceptDragDropPayload("HIERARCHY_ENTITIES");
                if (entityPayload.Handle != null)
                {
                    foreach (var ent in EditorSelection.SelectedEntities)
                        _app.SaveEntityAsBlueprint(ent, normalized);
                    EditorSelection.DraggedEntity = null;
                }
            }

            ImGui.EndDragDropTarget();
        }

        if (ImGui.BeginPopupContextItem("FolderBrowserContext"))
        {
            SelectFolder(normalized);
            _creationShaderPath = null;
            if (ImGui.MenuItem(L10n.Tr("menu_show_in_explorer")))
                Process.Start("explorer.exe", normalized.Replace("/", "\\"));
            if (ImGui.MenuItem(L10n.Tr("btn_reload")))
                ReloadProjectBrowser();
            ImGui.Separator();
            if (ImGui.MenuItem(L10n.Tr("btn_rename")))
                OpenRenamePopup(normalized);
            if (ImGui.MenuItem(L10n.Tr("btn_delete")))
                DeleteAsset(normalized);
            ImGui.Separator();
            DrawCreateMenu(normalized);
            ImGui.EndPopup();
        }

        DrawFolderPreview(row.PreviewPosition, row.PreviewSize);
        DrawBrowserRowText(row, name, ToProjectDisplayPath(normalized));
        EndBrowserRow();
    }

    private void DrawFileBrowserItem(string path)
    {
        string normalized = NormalizePath(path);
        string fileName = Path.GetFileName(path);
        bool selected = string.Equals(EditorSelection.SelectedAssetPath, normalized, StringComparison.OrdinalIgnoreCase) && EditorSelection.SelectedSpriteAsset == null;
        string subtitle = Path.GetExtension(path).ToUpperInvariant();

        SpriteImportSettings? spriteImport = null;
        if (IsImageAsset(path))
        {
            spriteImport = _app.TryGetSpriteImportSettings(normalized, false);
            if (spriteImport is { SpriteMode: SpriteImportMode.Multiple, Slices.Count: > 0 })
                subtitle = L10n.Tr("label_slice_count", spriteImport.Slices.Count);
        }

        var row = BeginBrowserRow($"file_{normalized}", selected, 0f);

        if (row.Clicked)
            SelectAsset(normalized);
        if (row.DoubleClicked)
            OnAssetDoubleClicked(normalized);

        if (ImGui.BeginDragDropSource())
        {
            EditorSelection.BeginAssetDrag(normalized);
            ImGui.SetDragDropPayload("ASSET_PATH", null, 0);
            ImGui.Text(L10n.Tr("msg_move_file", fileName));
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginPopupContextItem("FileBrowserContext"))
        {
            SelectAsset(normalized);
            string parentDir = NormalizePath(Path.GetDirectoryName(path)!);
            _contextDirectory = parentDir;
            _creationShaderPath = path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) && _app.ProjectPath != null
                ? Path.GetRelativePath(_app.ProjectPath, path).Replace("\\", "/")
                : null;

            if (ImGui.MenuItem(L10n.Tr("menu_show_in_explorer")))
                Process.Start("explorer.exe", $"/select,\"{path.Replace("/", "\\")}\"");
            if (ImGui.MenuItem(L10n.Tr("btn_reload")))
                ReloadProjectBrowser();
            if (ImGui.MenuItem(L10n.Tr("btn_rename")))
                OpenRenamePopup(normalized);
            if (ImGui.MenuItem(L10n.Tr("btn_delete")))
                DeleteAsset(normalized);
            ImGui.Separator();
            DrawCreateMenu(parentDir);
            ImGui.EndPopup();
        }

        DrawFilePreview(normalized, row.PreviewPosition, row.PreviewSize);
        DrawBrowserRowText(row, fileName, subtitle);
        EndBrowserRow();

        if (spriteImport is { SpriteMode: SpriteImportMode.Multiple, Slices.Count: > 0 })
        {
            foreach (var slice in spriteImport.Slices)
                DrawSpriteBrowserItem(normalized, slice);
        }
    }

    private void DrawSpriteBrowserItem(string assetPath, SpriteSlice slice)
    {
        string normalized = NormalizePath(assetPath);
        bool selected = EditorSelection.SelectedSpriteAsset.HasValue &&
                        string.Equals(NormalizePath(EditorSelection.SelectedSpriteAsset.Value.Path), normalized, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(EditorSelection.SelectedSpriteAsset.Value.SpriteId, slice.Id, StringComparison.OrdinalIgnoreCase);

        string subtitle = $"{slice.Width} x {slice.Height}";
        var row = BeginBrowserRow($"sprite_{normalized}_{slice.Id}", selected, 24f);

        if (row.Clicked)
            SelectSprite(normalized, slice.Id);

        if (ImGui.BeginDragDropSource())
        {
            var sprite = _app.CreateSpriteReference(normalized, slice.Id);
            EditorSelection.BeginSpriteDrag(sprite);
            ImGui.SetDragDropPayload("ASSET_PATH", null, 0);
            ImGui.Text($"{Path.GetFileName(assetPath)} / {slice.Name}");
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginPopupContextItem("SpriteBrowserContext"))
        {
            SelectSprite(normalized, slice.Id);
            if (ImGui.MenuItem(L10n.Tr("btn_rename")))
                OpenRenamePopup(_app.CreateSpriteReference(normalized, slice.Id));
            if (ImGui.MenuItem(L10n.Tr("ctx_duplicate")))
                DuplicateSelectedSpriteAsset();
            bool canDelete = TryGetSelectedSpriteTarget(out _, out var settings, out _) && settings.Slices.Count > 1;
            if (!canDelete)
                ImGui.BeginDisabled();
            if (ImGui.MenuItem(L10n.Tr("btn_delete")) && canDelete)
                DeleteSelectedSpriteAsset();
            if (!canDelete)
                ImGui.EndDisabled();
            ImGui.Separator();
            if (ImGui.MenuItem(L10n.Tr("menu_show_in_explorer")))
                Process.Start("explorer.exe", $"/select,\"{assetPath.Replace("/", "\\")}\"");
            if (ImGui.MenuItem(L10n.Tr("btn_reload")))
                ReloadProjectBrowser();
            ImGui.EndPopup();
        }

        DrawSpritePreview(_app.CreateSpriteReference(normalized, slice.Id), row.PreviewPosition, row.PreviewSize);
        DrawBrowserRowText(row, slice.Name, subtitle);
        EndBrowserRow();
    }

    private void DrawBrowserItemAsRow(BrowserItem item)
    {
        switch (item.Kind)
        {
            case BrowserItemKind.Folder:
                DrawFolderBrowserItem(item.AssetPath);
                break;
            case BrowserItemKind.File:
                DrawFileBrowserItem(item.AssetPath);
                break;
            case BrowserItemKind.Sprite:
                var settings = _app.TryGetSpriteImportSettings(item.AssetPath, false);
                var slice = settings?.Slices.FirstOrDefault(value => string.Equals(value.Id, item.SpriteId, StringComparison.OrdinalIgnoreCase));
                if (slice != null)
                    DrawSpriteBrowserItem(item.AssetPath, slice);
                break;
        }
    }

    private void DrawBrowserItemAsTile(BrowserItem item, float width)
    {
        float height = MathF.Max(58f, _thumbnailSize + 12f);
        DrawBrowserItemCard(item, width, height, previewOnTop: false);
    }

    private void DrawBrowserItemAsIcon(BrowserItem item, float width)
    {
        float height = MathF.Max(_thumbnailSize + 44f, 108f);
        DrawBrowserItemCard(item, width, height, previewOnTop: true);
    }

    private void DrawBrowserItemCard(BrowserItem item, float width, float height, bool previewOnTop)
    {
        ImGui.PushID($"card_{item.Kind}_{item.AssetPath}_{item.SpriteId}");
        Vector2 size = new(MathF.Max(40f, width), height);
        ImGui.InvisibleButton("##card", size);
        bool hovered = ImGui.IsItemHovered();
        bool clicked = hovered && ImGui.IsMouseReleased(ImGuiMouseButton.Left);
        bool doubleClicked = hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);

        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();
        if (IsBrowserItemSelected(item))
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.Header), 6f);
        else if (hovered)
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.HeaderHovered), 6f);

        float previewSize = previewOnTop ? MathF.Min(_thumbnailSize, width - 16f) : MathF.Min(_thumbnailSize, height - 12f);
        Vector2 previewPos = previewOnTop
            ? new Vector2(min.X + (width - previewSize) * 0.5f, min.Y + 8f)
            : new Vector2(min.X + 8f, min.Y + (height - previewSize) * 0.5f);

        DrawBrowserItemPreview(item, previewPos, previewSize);

        if (previewOnTop)
        {
            DrawCardTextCentered(min, width, previewPos.Y + previewSize + 6f, item.Title, item.Subtitle);
        }
        else
        {
            float textX = previewPos.X + previewSize + 10f;
            drawList.AddText(new Vector2(textX, min.Y + 8f), ImGui.GetColorU32(ImGuiCol.Text), item.Title);
            drawList.AddText(new Vector2(textX, min.Y + MathF.Max(26f, height - 18f)), ImGui.GetColorU32(ImGuiCol.TextDisabled), item.Subtitle);
        }

        if (clicked)
            ActivateBrowserItem(item);
        if (doubleClicked)
            OpenBrowserItem(item);

        if (ImGui.BeginDragDropSource())
        {
            BeginBrowserItemDrag(item);
            ImGui.SetDragDropPayload("ASSET_PATH", null, 0);
            ImGui.Text(item.Title);
            ImGui.EndDragDropSource();
        }

        DrawBrowserItemContextMenu(item);
        ImGui.PopID();
    }

    private void DrawCardTextCentered(Vector2 min, float width, float textY, string title, string subtitle)
    {
        var drawList = ImGui.GetWindowDrawList();
        string clippedTitle = title.Length > 18 ? title[..16] + ".." : title;
        string clippedSubtitle = subtitle.Length > 18 ? subtitle[..16] + ".." : subtitle;
        Vector2 titleSize = ImGui.CalcTextSize(clippedTitle);
        Vector2 subtitleSize = ImGui.CalcTextSize(clippedSubtitle);
        drawList.AddText(new Vector2(min.X + (width - titleSize.X) * 0.5f, textY), ImGui.GetColorU32(ImGuiCol.Text), clippedTitle);
        drawList.AddText(new Vector2(min.X + (width - subtitleSize.X) * 0.5f, textY + 16f), ImGui.GetColorU32(ImGuiCol.TextDisabled), clippedSubtitle);
    }

    private bool IsBrowserItemSelected(BrowserItem item)
    {
        return item.Kind switch
        {
            BrowserItemKind.Folder => string.Equals(_selectedFolderPath, item.AssetPath, StringComparison.OrdinalIgnoreCase),
            BrowserItemKind.File => string.Equals(EditorSelection.SelectedAssetPath, item.AssetPath, StringComparison.OrdinalIgnoreCase) && EditorSelection.SelectedSpriteAsset == null,
            BrowserItemKind.Sprite => EditorSelection.SelectedSpriteAsset.HasValue &&
                                     string.Equals(NormalizePath(EditorSelection.SelectedSpriteAsset.Value.Path), item.AssetPath, StringComparison.OrdinalIgnoreCase) &&
                                     string.Equals(EditorSelection.SelectedSpriteAsset.Value.SpriteId, item.SpriteId, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private void ActivateBrowserItem(BrowserItem item)
    {
        switch (item.Kind)
        {
            case BrowserItemKind.Folder:
                SelectFolder(item.AssetPath);
                break;
            case BrowserItemKind.File:
                SelectAsset(item.AssetPath);
                break;
            case BrowserItemKind.Sprite:
                SelectSprite(item.AssetPath, item.SpriteId);
                break;
        }
    }

    private void OpenBrowserItem(BrowserItem item)
    {
        switch (item.Kind)
        {
            case BrowserItemKind.Folder:
                NavigateToDirectory(item.AssetPath, true);
                break;
            case BrowserItemKind.File:
                OnAssetDoubleClicked(item.AssetPath);
                break;
        }
    }

    private void BeginBrowserItemDrag(BrowserItem item)
    {
        if (item.Kind == BrowserItemKind.Sprite)
        {
            EditorSelection.BeginSpriteDrag(_app.CreateSpriteReference(item.AssetPath, item.SpriteId));
            return;
        }

        EditorSelection.BeginAssetDrag(item.AssetPath);
    }

    private void DrawBrowserItemPreview(BrowserItem item, Vector2 position, float size)
    {
        switch (item.Kind)
        {
            case BrowserItemKind.Folder:
                DrawFolderPreview(position, size);
                break;
            case BrowserItemKind.File:
                DrawFilePreview(item.AssetPath, position, size);
                break;
            case BrowserItemKind.Sprite:
                DrawSpritePreview(_app.CreateSpriteReference(item.AssetPath, item.SpriteId), position, size);
                break;
        }
    }

    private void DrawBrowserItemContextMenu(BrowserItem item)
    {
        if (!ImGui.BeginPopupContextItem("BrowserItemContext"))
            return;

        ActivateBrowserItem(item);
        switch (item.Kind)
        {
            case BrowserItemKind.Folder:
                _creationShaderPath = null;
                if (ImGui.MenuItem(L10n.Tr("menu_show_in_explorer")))
                    Process.Start("explorer.exe", item.AssetPath.Replace("/", "\\"));
                if (ImGui.MenuItem(L10n.Tr("btn_reload")))
                    ReloadProjectBrowser();
                ImGui.Separator();
                if (ImGui.MenuItem(L10n.Tr("btn_rename")))
                    OpenRenamePopup(item.AssetPath);
                if (ImGui.MenuItem(L10n.Tr("btn_delete")))
                    DeleteAsset(item.AssetPath);
                ImGui.Separator();
                DrawCreateMenu(item.AssetPath);
                break;
            case BrowserItemKind.File:
                string parentDir = NormalizePath(Path.GetDirectoryName(item.AssetPath)!);
                _contextDirectory = parentDir;
                _creationShaderPath = item.AssetPath.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) && _app.ProjectPath != null
                    ? Path.GetRelativePath(_app.ProjectPath, item.AssetPath).Replace("\\", "/")
                    : null;
                if (ImGui.MenuItem(L10n.Tr("menu_show_in_explorer")))
                    Process.Start("explorer.exe", $"/select,\"{item.AssetPath.Replace("/", "\\")}\"");
                if (ImGui.MenuItem(L10n.Tr("btn_reload")))
                    ReloadProjectBrowser();
                if (ImGui.MenuItem(L10n.Tr("btn_rename")))
                    OpenRenamePopup(item.AssetPath);
                if (ImGui.MenuItem(L10n.Tr("btn_delete")))
                    DeleteAsset(item.AssetPath);
                ImGui.Separator();
                DrawCreateMenu(parentDir);
                break;
            case BrowserItemKind.Sprite:
                if (ImGui.MenuItem(L10n.Tr("btn_rename")))
                    OpenRenamePopup(_app.CreateSpriteReference(item.AssetPath, item.SpriteId));
                if (ImGui.MenuItem(L10n.Tr("ctx_duplicate")))
                    DuplicateSelectedSpriteAsset();
                bool canDelete = TryGetSelectedSpriteTarget(out _, out var settings, out _) && settings.Slices.Count > 1;
                if (!canDelete)
                    ImGui.BeginDisabled();
                if (ImGui.MenuItem(L10n.Tr("btn_delete")) && canDelete)
                    DeleteSelectedSpriteAsset();
                if (!canDelete)
                    ImGui.EndDisabled();
                ImGui.Separator();
                if (ImGui.MenuItem(L10n.Tr("menu_show_in_explorer")))
                    Process.Start("explorer.exe", $"/select,\"{item.AssetPath.Replace("/", "\\")}\"");
                if (ImGui.MenuItem(L10n.Tr("btn_reload")))
                    ReloadProjectBrowser();
                break;
        }

        ImGui.EndPopup();
    }

    private (bool Clicked, bool DoubleClicked, Vector2 PreviewPosition, float PreviewSize, Vector2 Min, Vector2 Max) BeginBrowserRow(string id, bool selected, float indent)
    {
        float rowHeight = MathF.Max(42f, _thumbnailSize + 14f);
        ImGui.PushID(id);
        float rowWidth = MathF.Max(1f, ImGui.GetContentRegionAvail().X);
        ImGui.InvisibleButton("##row", new Vector2(rowWidth, rowHeight));
        bool hovered = ImGui.IsItemHovered();
        bool clicked = hovered && ImGui.IsMouseReleased(ImGuiMouseButton.Left);
        bool doubleClicked = hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);

        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        float previewSize = MathF.Min(_thumbnailSize, rowHeight - 10f);
        Vector2 previewPosition = new(min.X + 8f + indent, min.Y + (rowHeight - previewSize) * 0.5f);
        var drawList = ImGui.GetWindowDrawList();

        if (selected)
        {
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.Header), 4f);
        }
        else if (hovered)
        {
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.HeaderHovered), 4f);
        }

        if (indent > 0f)
        {
            uint lineColor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.12f));
            float branchX = min.X + 16f;
            drawList.AddLine(new Vector2(branchX, min.Y), new Vector2(branchX, max.Y), lineColor, 1f);
            drawList.AddLine(new Vector2(branchX, min.Y + rowHeight * 0.5f), new Vector2(previewPosition.X - 4f, min.Y + rowHeight * 0.5f), lineColor, 1f);
        }

        return (clicked, doubleClicked, previewPosition, previewSize, min, max);
    }

    private void DrawBrowserRowText((bool Clicked, bool DoubleClicked, Vector2 PreviewPosition, float PreviewSize, Vector2 Min, Vector2 Max) row, string title, string subtitle)
    {
        var drawList = ImGui.GetWindowDrawList();
        float textX = row.PreviewPosition.X + row.PreviewSize + 10f;
        float titleY = row.Min.Y + 7f;
        float subtitleY = row.Min.Y + MathF.Max(24f, row.Max.Y - row.Min.Y - 20f);
        drawList.AddText(new Vector2(textX, titleY), ImGui.GetColorU32(ImGuiCol.Text), title);
        drawList.AddText(new Vector2(textX, subtitleY), ImGui.GetColorU32(ImGuiCol.TextDisabled), subtitle);
    }

    private static void EndBrowserRow()
    {
        ImGui.PopID();
    }

    private void DrawFolderPreview(Vector2 position, float size)
    {
        var drawList = ImGui.GetWindowDrawList();
        uint mainColor = ImGui.GetColorU32(new Vector4(0.94f, 0.78f, 0.29f, 1f));
        uint tabColor = ImGui.GetColorU32(new Vector4(1f, 0.85f, 0.38f, 1f));
        Vector2 tabMin = position + new Vector2(2f, 4f);
        Vector2 tabMax = position + new Vector2(size * 0.48f, size * 0.34f);
        Vector2 bodyMin = position + new Vector2(2f, size * 0.24f);
        Vector2 bodyMax = position + new Vector2(size - 2f, size - 4f);
        drawList.AddRectFilled(tabMin, tabMax, tabColor, 4f);
        drawList.AddRectFilled(bodyMin, bodyMax, mainColor, 5f);
        drawList.AddRect(bodyMin, bodyMax, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.18f)), 5f);
    }

    private void DrawFilePreview(string path, Vector2 position, float size)
    {
        if (IsImageAsset(path) && TryGetFullTexturePreview(path, out var preview))
        {
            DrawTexturePreview(preview, position, size);
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        uint fill = ImGui.GetColorU32(new Vector4(0.22f, 0.24f, 0.28f, 1f));
        uint outline = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.12f));
        drawList.AddRectFilled(position, position + new Vector2(size, size), fill, 4f);
        drawList.AddRect(position, position + new Vector2(size, size), outline, 4f);

        string ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(ext))
            ext = "FILE";
        drawList.AddText(position + new Vector2(6f, size * 0.34f), ImGui.GetColorU32(ImGuiCol.Text), ext.Length > 4 ? ext[..4] : ext);
    }

    private void DrawSpritePreview(Sprite sprite, Vector2 position, float size)
    {
        if (TryGetSpritePreview(sprite, out var preview))
        {
            DrawTexturePreview(preview, position, size);
            return;
        }

        DrawFilePreview(sprite.Path, position, size);
    }

    private void DrawTexturePreview(BrowserPreview preview, Vector2 position, float size)
    {
        if (preview.Texture is not OpenGlTexture glTex)
            return;

        float aspect = MathF.Max(0.01f, preview.Width / (float)Math.Max(1, preview.Height));
        float drawWidth = size;
        float drawHeight = size;
        if (aspect >= 1f)
            drawHeight = size / aspect;
        else
            drawWidth = size * aspect;

        Vector2 drawPos = new(
            position.X + (size - drawWidth) * 0.5f,
            position.Y + (size - drawHeight) * 0.5f);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddImage(new ImTextureRef(null, new ImTextureID((nint)glTex.Id)), drawPos, drawPos + new Vector2(drawWidth, drawHeight), preview.UvMin, preview.UvMax);
    }

    private bool TryGetFullTexturePreview(string assetPath, out BrowserPreview preview)
    {
        preview = default;
        try
        {
            var sprite = _app.CreateSpriteReference(assetPath);
            var texture = _app.LoadSpriteTexture(sprite);
            if (texture == null)
                return false;

            preview = new BrowserPreview(texture, new Vector2(0, 1), new Vector2(1, 0), texture.Width, texture.Height);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetSpritePreview(Sprite sprite, out BrowserPreview preview)
    {
        preview = default;
        if (string.IsNullOrWhiteSpace(sprite.Path))
            return false;

        try
        {
            string fullPath = ResolveAbsoluteAssetPath(sprite.Path);
            if (!File.Exists(fullPath))
                return false;

            var texture = _app.LoadSpriteTexture(sprite);
            if (texture == null)
                return false;

            var slice = _app.ResolveSpriteSlice(sprite);
            Vector2 uvMin = new(slice.X / (float)Math.Max(1, texture.Width), 1f - (slice.Y / (float)Math.Max(1, texture.Height)));
            Vector2 uvMax = new((slice.X + slice.Width) / (float)Math.Max(1, texture.Width), 1f - ((slice.Y + slice.Height) / (float)Math.Max(1, texture.Height)));
            preview = new BrowserPreview(texture, uvMin, uvMax, slice.Width, slice.Height);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void DrawDirectoryNode(string path, bool isRoot)
    {
        string normalized = NormalizePath(path);
        string label = isRoot ? "Assets" : Path.GetFileName(path);
        ImGui.PushID(normalized);

        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (isRoot)
            flags |= ImGuiTreeNodeFlags.DefaultOpen;
        if (string.Equals(_currentDirectory, normalized, StringComparison.OrdinalIgnoreCase))
            flags |= ImGuiTreeNodeFlags.Selected;

        bool opened = ImGui.TreeNodeEx("##folder", flags, label);
        if (ImGui.IsItemClicked())
            NavigateToDirectory(normalized, true);

        if (!isRoot && ImGui.BeginDragDropSource())
        {
            EditorSelection.BeginAssetDrag(normalized);
            ImGui.SetDragDropPayload("ASSET_PATH", null, 0);
            ImGui.Text(L10n.Tr("msg_move_folder", label));
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var assetPayload = ImGui.AcceptDragDropPayload("ASSET_PATH");
                if (assetPayload.Handle != null && EditorSelection.DraggedAssetPath != null && EditorSelection.DraggedSpriteAsset == null)
                {
                    MoveAsset(EditorSelection.DraggedAssetPath, normalized);
                    EditorSelection.ClearAssetDrag();
                }

                var entityPayload = ImGui.AcceptDragDropPayload("HIERARCHY_ENTITIES");
                if (entityPayload.Handle != null)
                {
                    foreach (var ent in EditorSelection.SelectedEntities)
                        _app.SaveEntityAsBlueprint(ent, normalized);
                    EditorSelection.DraggedEntity = null;
                }
            }

            ImGui.EndDragDropTarget();
        }

        if (ImGui.BeginPopupContextItem("FolderTreeContext"))
        {
            NavigateToDirectory(normalized, true);
            _creationShaderPath = null;
            if (ImGui.MenuItem(L10n.Tr("menu_show_in_explorer")))
                Process.Start("explorer.exe", normalized.Replace("/", "\\"));
            if (ImGui.MenuItem(L10n.Tr("btn_reload")))
                ReloadProjectBrowser();
            ImGui.Separator();
            if (!isRoot)
            {
                if (ImGui.MenuItem(L10n.Tr("btn_rename")))
                    OpenRenamePopup(normalized);
                if (ImGui.MenuItem(L10n.Tr("btn_delete")))
                    DeleteAsset(normalized);
                ImGui.Separator();
            }
            DrawCreateMenu(normalized);
            ImGui.EndPopup();
        }

        if (opened)
        {
            foreach (string directory in GetTreeDirectories(path))
                DrawDirectoryNode(directory, false);
            ImGui.TreePop();
        }

        ImGui.PopID();
    }

    private void DrawCreateMenu(string target)
    {
        if (!ImGui.BeginMenu(L10n.Tr("menu_create")))
            return;

        if (ImGui.MenuItem(L10n.Tr("CreationType_World")))
            OpenCreatePopup(target, CreationType.World);
        if (ImGui.MenuItem(L10n.Tr("CreationType_Script")))
            OpenCreatePopup(target, CreationType.Script);
        if (ImGui.MenuItem(L10n.Tr("CreationType_Folder"), "Ctrl+N"))
            OpenCreatePopup(target, CreationType.Folder);
        ImGui.Separator();
        if (ImGui.MenuItem(L10n.Tr("CreationType_Shader")))
            OpenCreatePopup(target, CreationType.Shader);
        if (ImGui.MenuItem(L10n.Tr("CreationType_Style")))
            OpenCreatePopup(target, CreationType.Style);
        if (ImGui.MenuItem(L10n.Tr("CreationType_UiScreen")))
            OpenCreatePopup(target, CreationType.UiScreen);
        if (ImGui.MenuItem(L10n.Tr("CreationType_UiStyle")))
            OpenCreatePopup(target, CreationType.UiStyle);
        ImGui.Separator();
        if (ImGui.MenuItem(L10n.Tr("CreationType_Tile")))
            OpenCreatePopup(target, CreationType.Tile);
        if (ImGui.MenuItem(L10n.Tr("CreationType_AnimatedTile")))
            OpenCreatePopup(target, CreationType.AnimatedTile);
        if (ImGui.MenuItem(L10n.Tr("CreationType_RuleTile")))
            OpenCreatePopup(target, CreationType.RuleTile);
        ImGui.EndMenu();
    }

    private void HandleShortcuts()
    {
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
            return;

        var io = ImGui.GetIO();
        if (io.WantCaptureKeyboard)
            return;

        bool ctrl = io.KeyCtrl;
        if (ctrl && ImGui.IsKeyPressed(ImGuiKey.N))
            OpenCreatePopup(ResolveContextDirectory(), CreationType.Folder);

        if (EditorSelection.SelectedSpriteAsset.HasValue)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Delete))
                DeleteSelectedSpriteAsset();
            if (ImGui.IsKeyPressed(ImGuiKey.F2))
                OpenRenamePopup(EditorSelection.SelectedSpriteAsset.Value);
            if (ctrl && ImGui.IsKeyPressed(ImGuiKey.D))
                DuplicateSelectedSpriteAsset();
            return;
        }

        string? selectedPath = EditorSelection.SelectedAssetPath ?? _selectedFolderPath;
        if (selectedPath == null)
            return;

        if (ImGui.IsKeyPressed(ImGuiKey.Delete))
            DeleteAsset(selectedPath);
        if (ImGui.IsKeyPressed(ImGuiKey.F2))
            OpenRenamePopup(selectedPath);
        if (ctrl && ImGui.IsKeyPressed(ImGuiKey.D))
            DuplicateAsset(selectedPath);
    }

    private void NavigateToDirectory(string path, bool selectFolder)
    {
        string normalized = NormalizePath(path);
        if (!Directory.Exists(normalized))
            return;

        _currentDirectory = normalized;
        _contextDirectory = normalized;
        if (selectFolder)
            SelectFolder(normalized);
    }

    private void NavigateUp()
    {
        if (_app.AssetsPath == null || _currentDirectory == null)
            return;

        string assets = NormalizePath(_app.AssetsPath);
        if (string.Equals(_currentDirectory, assets, StringComparison.OrdinalIgnoreCase))
            return;

        string? parent = Directory.GetParent(_currentDirectory)?.FullName;
        if (!string.IsNullOrWhiteSpace(parent) && IsWithinAssets(parent))
            NavigateToDirectory(parent, true);
    }

    private void SelectFolder(string path)
    {
        _selectedFolderPath = NormalizePath(path);
        _contextDirectory = _selectedFolderPath;
        EditorSelection.ClearAssetSelection();
        EditorSelection.SelectedEntity = null;
    }

    private void SelectAsset(string path)
    {
        _selectedFolderPath = null;
        _contextDirectory = NormalizePath(Path.GetDirectoryName(path)!);
        EditorSelection.SelectAsset(NormalizePath(path));
        EditorSelection.SelectedEntity = null;
    }

    private void SelectSprite(string assetPath, string spriteId)
    {
        _selectedFolderPath = null;
        _contextDirectory = NormalizePath(Path.GetDirectoryName(assetPath)!);
        EditorSelection.SelectSpriteAsset(_app.CreateSpriteReference(assetPath, spriteId));
        EditorSelection.SelectedEntity = null;
    }

    private void DeleteAsset(string path)
    {
        string normalized = NormalizePath(path);
        try
        {
            if (File.Exists(normalized))
            {
                File.Delete(normalized);
                DeleteMetaFile(normalized);
            }
            else if (Directory.Exists(normalized))
            {
                Directory.Delete(normalized, true);
            }
            else
            {
                return;
            }

            AssetPathUtility.InvalidateCache(_app.ProjectPath);
            InvalidateBrowserCache();
            ClearSelectionForDeletedPath(normalized);
        }
        catch (Exception e)
        {
            Verity.Core.Debug.LogError($"[Asset] Delete Failed: {e.Message}");
        }
    }

    private void DeleteSelectedSpriteAsset()
    {
        if (!TryGetSelectedSpriteTarget(out string assetPath, out SpriteImportSettings settings, out int sliceIndex))
            return;

        if (settings.Slices.Count <= 1)
            return;

        settings.Slices.RemoveAt(sliceIndex);
        AssetPathUtility.SaveSpriteImportSettings(assetPath, settings);
        AssetPathUtility.InvalidateCache(_app.ProjectPath);
        InvalidateBrowserCache();
        SelectAsset(assetPath);
    }

    private void DuplicateAsset(string path)
    {
        string normalized = NormalizePath(path);
        try
        {
            string dir = NormalizePath(Path.GetDirectoryName(normalized)!);
            string name = Path.GetFileNameWithoutExtension(normalized);
            string ext = Path.GetExtension(normalized);
            string next = NormalizePath(Path.Combine(dir, name + " (Copy)" + ext));
            if (File.Exists(normalized))
            {
                File.Copy(normalized, next, true);
                AssetPathUtility.EnsureMetaAndGetGuid(next);
            }
            else if (Directory.Exists(normalized))
            {
                CopyDirectory(normalized, next);
                EnsureMetaForDirectory(next);
            }
            else
            {
                return;
            }

            AssetPathUtility.InvalidateCache(_app.ProjectPath);
            InvalidateBrowserCache();
        }
        catch (Exception e)
        {
            Verity.Core.Debug.LogError($"[Asset] Duplicate Failed: {e.Message}");
        }
    }

    private void DuplicateSelectedSpriteAsset()
    {
        if (!TryGetSelectedSpriteTarget(out string assetPath, out SpriteImportSettings settings, out int sliceIndex))
            return;

        var duplicated = settings.Slices[sliceIndex].Clone();
        duplicated.Id = Guid.NewGuid().ToString("N");
        duplicated.Name = MakeUniqueSliceName(settings, $"{duplicated.Name} Copy");
        settings.Slices.Insert(sliceIndex + 1, duplicated);
        AssetPathUtility.SaveSpriteImportSettings(assetPath, settings);
        AssetPathUtility.InvalidateCache(_app.ProjectPath);
        InvalidateBrowserCache();
        SelectSprite(assetPath, duplicated.Id);
    }

    private void MoveAsset(string source, string targetDir)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(targetDir))
            return;

        string normalizedSource = NormalizePath(source);
        string normalizedTargetDir = NormalizePath(targetDir);
        if (string.Equals(normalizedSource, normalizedTargetDir, StringComparison.OrdinalIgnoreCase))
            return;

        string? sourceDir = Path.GetDirectoryName(normalizedSource);
        if (sourceDir != null && string.Equals(NormalizePath(sourceDir), normalizedTargetDir, StringComparison.OrdinalIgnoreCase))
            return;

        string destination = NormalizePath(Path.Combine(normalizedTargetDir, Path.GetFileName(normalizedSource)));
        MovePath(normalizedSource, destination);
    }

    private void MovePath(string source, string destination)
    {
        try
        {
            if (File.Exists(source))
            {
                File.Move(source, destination);
                MoveMetaFile(source, destination);
            }
            else if (Directory.Exists(source))
            {
                Directory.Move(source, destination);
            }
            else
            {
                return;
            }

            UpdateProjectAssetReferences(source, destination);
            UpdatePathBasedSelectionState(source, destination);
            AssetPathUtility.InvalidateCache(_app.ProjectPath);
            InvalidateBrowserCache();
        }
        catch (Exception e)
        {
            Verity.Core.Debug.LogError($"[Asset] Move Failed: {e.Message}");
        }
    }

    public void OpenCreatePopup(string dir, CreationType type)
    {
        _activeMode = ModalMode.Create;
        _creationType = type;
        _targetPath = NormalizePath(dir);
        _targetSpriteAssetPath = null;
        _targetSpriteId = null;
        _inputBuffer = type switch
        {
            CreationType.Script => "NewScript",
            CreationType.World => "NewWorld",
            CreationType.Shader => "NewShader",
            CreationType.Style => "NewStyle",
            CreationType.UiScreen => "NewUIScreen",
            CreationType.UiStyle => "NewUIStyle",
            CreationType.Tile => "NewTile",
            CreationType.AnimatedTile => "NewAnimatedTile",
            CreationType.RuleTile => "NewRuleTile",
            _ => "NewFolder"
        };
        _shouldOpenPopup = true;
    }

    private void OpenRenamePopup(string path)
    {
        _activeMode = ModalMode.RenameAsset;
        _targetPath = NormalizePath(path);
        _targetSpriteAssetPath = null;
        _targetSpriteId = null;
        _inputBuffer = File.Exists(_targetPath) ? Path.GetFileNameWithoutExtension(_targetPath) : Path.GetFileName(_targetPath);
        _shouldOpenPopup = true;
    }

    private void OpenRenamePopup(Sprite sprite)
    {
        string assetPath = ResolveAbsoluteAssetPath(sprite.Path);
        var settings = _app.TryGetSpriteImportSettings(assetPath, false);
        var slice = settings?.Slices.FirstOrDefault(item => string.Equals(item.Id, sprite.SpriteId, StringComparison.OrdinalIgnoreCase));
        if (slice == null)
            return;

        _activeMode = ModalMode.RenameSprite;
        _targetPath = null;
        _targetSpriteAssetPath = assetPath;
        _targetSpriteId = sprite.SpriteId;
        _inputBuffer = slice.Name;
        _shouldOpenPopup = true;
    }

    private unsafe void DrawInputModal()
    {
        var viewport = ImGui.GetMainViewport();
        var center = new Vector2(viewport.Pos.X + viewport.Size.X * 0.5f, viewport.Pos.Y + viewport.Size.Y * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        if (!ImGui.BeginPopupModal("AssetInputModal", null, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        string title = _activeMode == ModalMode.Create
            ? L10n.Tr("msg_create_asset", L10n.Tr($"CreationType_{_creationType}"))
            : L10n.Tr("msg_rename_asset");
        ImGui.Text(title);
        ImGui.Separator();
        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();
        ImGui.InputText(L10n.Tr("label_name"), ref _inputBuffer, 64);
        var buttonSize = new Vector2(120, 0);
        if (ImGui.Button(L10n.Tr("btn_ok"), buttonSize) || ImGui.IsKeyPressed(ImGuiKey.Enter))
        {
            if (_activeMode == ModalMode.Create)
                FinalizeCreate();
            else if (_activeMode == ModalMode.RenameAsset)
                FinalizeRename();
            else if (_activeMode == ModalMode.RenameSprite)
                FinalizeRenameSprite();

            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("btn_cancel"), buttonSize) || ImGui.IsKeyPressed(ImGuiKey.Escape))
            ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void FinalizeCreate()
    {
        if (_targetPath == null || string.IsNullOrWhiteSpace(_inputBuffer))
            return;

        try
        {
            string fullPath = NormalizePath(Path.Combine(_targetPath, _inputBuffer));
            switch (_creationType)
            {
                case CreationType.Script:
                    File.WriteAllText(fullPath + ".cs", $"// using Verity.Core;\n// using Verity.Graphics;\n// using Verity.Input;\n// using System.Numerics;\nusing Verity.Core.ECS;\n\npublic class {_inputBuffer} : Script\n{{\n    void Start()\n    {{\n    }}\n\n    void Update()\n    {{\n    }}\n}}");
                    break;
                case CreationType.World:
                    var world = new World(_inputBuffer);
                    var cameraEntity = world.CreateEntity("Main Camera");
                    cameraEntity.AddComponent<Camera>();
                    string worldPath = fullPath + ".verity";
                    File.WriteAllText(worldPath, SceneSerializer.Serialize(world));
                    LoadWorldByPath(worldPath);
                    break;
                case CreationType.Folder:
                    Directory.CreateDirectory(fullPath);
                    break;
                case CreationType.Shader:
                    File.WriteAllText(fullPath + ".shader", "// VERTEX\n#version 330 core\nlayout(location = 0) in vec2 aPosition;\nlayout(location = 1) in vec2 aTexCoord;\nuniform mat4 uProjection;\nuniform mat4 uView;\nuniform mat4 uModel;\nout vec2 vTexCoord;\nvoid main() {\n    vTexCoord = aTexCoord;\n    gl_Position = uProjection * uView * uModel * vec4(aPosition, 0.0, 1.0);\n}\n\n// FRAGMENT\n#version 330 core\nin vec2 vTexCoord;\nuniform sampler2D uTexture;\nuniform vec4 uColor;\nout vec4 FragColor;\nvoid main() {\n    FragColor = texture(uTexture, vTexCoord) * uColor;\n}");
                    break;
                case CreationType.Style:
                    var style = new StyleData();
                    if (!string.IsNullOrEmpty(_creationShaderPath))
                        style.ShaderPath = _creationShaderPath;
                    File.WriteAllText(fullPath + ".style", JsonSerializer.Serialize(style, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Converters =
                        {
                            new Vector2Converter(),
                            new Vector3Converter(),
                            new Vector4Converter(),
                            new SpriteConverter(),
                            new StyleAssetConverter(),
                            new ShaderAssetConverter(),
                            new Verity.Core.Serialization.ColorConverter()
                        }
                    }));
                    break;
                case CreationType.UiScreen:
                    UiSerializer.Save(fullPath + ".ui", UiSerializer.CreateDefaultScreen(_inputBuffer));
                    break;
                case CreationType.UiStyle:
                    UiSerializer.SaveStyle(fullPath + ".uistyle", new UiStyleAsset { Name = _inputBuffer });
                    break;
                case CreationType.Tile:
                    File.WriteAllText(fullPath + ".tile", JsonSerializer.Serialize<TileBase>(new Tile { Name = _inputBuffer }, _options));
                    break;
                case CreationType.AnimatedTile:
                    File.WriteAllText(fullPath + ".animtile", JsonSerializer.Serialize<TileBase>(new AnimatedTile { Name = _inputBuffer }, _options));
                    break;
                case CreationType.RuleTile:
                    File.WriteAllText(fullPath + ".ruletile", JsonSerializer.Serialize<TileBase>(new RuleTile { Name = _inputBuffer }, _options));
                    break;
            }

            foreach (string createdFile in Directory.Exists(fullPath) ? Array.Empty<string>() : Directory.GetFiles(_targetPath, _inputBuffer + ".*", SearchOption.TopDirectoryOnly))
                AssetPathUtility.EnsureMetaAndGetGuid(createdFile);

            AssetPathUtility.InvalidateCache(_app.ProjectPath);
            InvalidateBrowserCache();
        }
        catch (Exception e)
        {
            Verity.Core.Debug.LogError(e.Message);
        }
    }

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Converters =
        {
            new Vector2Converter(),
            new Vector3Converter(),
            new Vector4Converter(),
            new SpriteConverter(),
            new StyleAssetConverter(),
            new ShaderAssetConverter(),
            new Verity.Core.Serialization.ColorConverter(),
            new TileBaseConverter(),
            new TilemapTilesConverter()
        }
    };

    private void FinalizeRename()
    {
        if (_targetPath == null || string.IsNullOrWhiteSpace(_inputBuffer))
            return;

        try
        {
            string source = NormalizePath(_targetPath);
            string directory = NormalizePath(Path.GetDirectoryName(source)!);
            string next = NormalizePath(Path.Combine(directory, _inputBuffer + Path.GetExtension(source)));

            if (string.Equals(source, next, StringComparison.OrdinalIgnoreCase))
                return;

            MovePath(source, next);
        }
        catch (Exception e)
        {
            Verity.Core.Debug.LogError(e.Message);
        }
    }

    private void FinalizeRenameSprite()
    {
        if (string.IsNullOrWhiteSpace(_targetSpriteAssetPath) ||
            string.IsNullOrWhiteSpace(_targetSpriteId) ||
            string.IsNullOrWhiteSpace(_inputBuffer))
        {
            return;
        }

        var settings = _app.TryGetSpriteImportSettings(_targetSpriteAssetPath, false);
        var slice = settings?.Slices.FirstOrDefault(item => string.Equals(item.Id, _targetSpriteId, StringComparison.OrdinalIgnoreCase));
        if (settings == null || slice == null)
            return;

        slice.Name = _inputBuffer.Trim();
        AssetPathUtility.SaveSpriteImportSettings(_targetSpriteAssetPath, settings);
        AssetPathUtility.InvalidateCache(_app.ProjectPath);
        InvalidateBrowserCache();
        SelectSprite(_targetSpriteAssetPath, slice.Id);
    }

    private void OnAssetDoubleClicked(string path)
    {
        string normalized = NormalizePath(path);
        if (normalized.EndsWith(".verity", StringComparison.OrdinalIgnoreCase))
        {
            LoadWorldByPath(normalized);
            return;
        }

        if (normalized.EndsWith(".ui", StringComparison.OrdinalIgnoreCase))
        {
            SelectAsset(normalized);
            var uiEditor = _app.GetWindow<UIEditorWindow>();
            if (uiEditor != null)
                uiEditor.IsOpen = true;
            return;
        }

        if (normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".style", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = normalized,
                UseShellExecute = true
            });
        }
    }

    public void LoadWorldByPath(string path)
    {
        string normalized = NormalizePath(path);
        if (!File.Exists(normalized))
            return;

        if (_app.IsPlaying)
            _app.ExitPlayMode();

        var world = WorldManager.CreateOrReplaceWorld(Path.GetFileNameWithoutExtension(normalized));
        SceneSerializer.Deserialize(world, File.ReadAllText(normalized), _app.ScriptCompiler?.CompiledAssembly);
        _app.BindWorldAssets(world);
        WorldManager.SetActiveWorld(world);
        _app.ResetDirty();
    }

    public void CreateWorldInProject() => OpenCreatePopup(_app.AssetsPath!, CreationType.World);

    public void SaveActiveWorldAsAsset()
    {
        if (_app.IsPlaying)
        {
            _app.ShowOverlayMessage(L10n.Tr("msg_cannot_save_world_play_mode"), 3.0f);
            return;
        }

        if (WorldManager.ActiveWorld == null || _app.AssetsPath == null)
            return;

        string path = Path.Combine(_app.AssetsPath, $"{WorldManager.ActiveWorld.Name}.verity");
        File.WriteAllText(path, SceneSerializer.Serialize(WorldManager.ActiveWorld));
        AssetPathUtility.EnsureMetaAndGetGuid(path);
        _app.ResetDirty();
        _app.ShowOverlayMessage(L10n.Tr("msg_world_saved", WorldManager.ActiveWorld.Name));
    }

    public void PublishSingleFile()
    {
        if (_app.IsBuilding || _app.ProjectPath == null)
            return;

        Task.Run(() =>
        {
            _app.IsBuilding = true;
            try
            {
                _app.BuildStatus = "Preparing publish directory...";
                string publishDir = Path.Combine(_app.ProjectPath, "Build");
                if (Directory.Exists(publishDir))
                {
                    try { Directory.Delete(publishDir, true); } catch { }
                }

                Directory.CreateDirectory(publishDir);
                string? projectRoot = ResolveProjectRoot();
                if (projectRoot == null)
                {
                    Verity.Core.Debug.LogError("[Publish] Could not find solution root.");
                    return;
                }

                string gameProjDir = Path.Combine(projectRoot, "Verity.Game");
                _app.BuildStatus = "Syncing Assets to Game Engine...";
                string gameAssets = Path.Combine(gameProjDir, "Assets");
                if (Directory.Exists(gameAssets))
                    Directory.Delete(gameAssets, true);
                CopyDirectory(_app.AssetsPath!, gameAssets);

                _app.BuildStatus = "Syncing Build Settings...";
                string settingsSrc = Path.Combine(_app.ProjectPath, "BuildSettings.json");
                if (File.Exists(settingsSrc))
                    File.Copy(settingsSrc, Path.Combine(gameProjDir, "BuildSettings.json"), true);

                _app.BuildStatus = "Compiling Script Library...";
                string gameDll = Path.Combine(gameProjDir, "UserScripts.dll");
                _app.ScriptCompiler?.CompileToFile(gameDll);

                _app.BuildStatus = "Running .NET Publish (May take a minute)...";
                var psi = new ProcessStartInfo("dotnet", $"publish \"{Path.Combine(gameProjDir, "Verity.Game.csproj")}\" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o \"{publishDir}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                var process = Process.Start(psi);
                while (!process!.StandardOutput.EndOfStream)
                {
                    string? line = process.StandardOutput.ReadLine();
                    if (line != null)
                        _app.BuildStatus = line.Length > 40 ? line[..40] + "..." : line;
                }

                process.WaitForExit();
                if (process.ExitCode == 0)
                {
                    _app.BuildStatus = "Done!";
                    Process.Start("explorer.exe", publishDir);
                }
                else
                {
                    Verity.Core.Debug.LogError("[Publish] Publish failed. See console.");
                }
            }
            catch (Exception e)
            {
                Verity.Core.Debug.LogError($"[Publish] Error: {e.Message}");
            }
            finally
            {
                _app.IsBuilding = false;
            }
        });
    }

    private string? ResolveProjectRoot()
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

    private static void DeleteMetaFile(string assetPath)
    {
        string metaPath = AssetPathUtility.GetMetaPath(assetPath);
        if (File.Exists(metaPath))
            File.Delete(metaPath);
    }

    private static void MoveMetaFile(string sourcePath, string destPath)
    {
        string sourceMeta = AssetPathUtility.GetMetaPath(sourcePath);
        string destMeta = AssetPathUtility.GetMetaPath(destPath);
        if (!File.Exists(sourceMeta))
            return;

        if (File.Exists(destMeta))
            File.Delete(destMeta);

        File.Move(sourceMeta, destMeta);
    }

    private static void EnsureMetaForDirectory(string directory)
    {
        foreach (string file in Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories))
        {
            if (!AssetPathUtility.IsMetaFile(file))
                AssetPathUtility.EnsureMetaAndGetGuid(file);
        }
    }

    private void UpdateProjectAssetReferences(string oldPath, string newPath)
    {
        if (_app.AssetsPath == null)
            return;

        string oldNormalized = AssetPathUtility.Normalize(oldPath);
        string newNormalized = AssetPathUtility.Normalize(newPath);
        string[] extensions = [".verity", ".blueprint", ".style", ".tile", ".animtile", ".ruletile", ".controller", ".json", ".ui", ".uistyle", ".uiprefab"];

        foreach (string file in Directory.GetFiles(_app.AssetsPath, "*.*", SearchOption.AllDirectories))
        {
            if (AssetPathUtility.IsMetaFile(file) || !extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                continue;

            try
            {
                string json = File.ReadAllText(file);
                JsonNode? root = JsonNode.Parse(json);
                if (root == null || !RewriteAssetPaths(root, oldNormalized, newNormalized))
                    continue;

                File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
            }
        }
    }

    private static bool RewriteAssetPaths(JsonNode node, string oldPath, string newPath)
    {
        bool changed = false;

        if (node is JsonObject obj)
        {
            foreach (var kvp in obj.ToList())
            {
                if (kvp.Value == null)
                    continue;

                if (kvp.Value is JsonValue value && value.TryGetValue<string>(out var stringValue))
                {
                    string? rewritten = RewriteAssetPathValue(stringValue, oldPath, newPath);
                    if (rewritten != null)
                    {
                        obj[kvp.Key] = rewritten;
                        changed = true;
                    }
                }
                else
                {
                    changed |= RewriteAssetPaths(kvp.Value, oldPath, newPath);
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] == null)
                    continue;

                if (array[i] is JsonValue value && value.TryGetValue<string>(out var stringValue))
                {
                    string? rewritten = RewriteAssetPathValue(stringValue, oldPath, newPath);
                    if (rewritten != null)
                    {
                        array[i] = rewritten;
                        changed = true;
                    }
                }
                else if (array[i] != null)
                {
                    changed |= RewriteAssetPaths(array[i]!, oldPath, newPath);
                }
            }
        }

        return changed;
    }

    private static string? RewriteAssetPathValue(string value, string oldPath, string newPath)
    {
        string normalized = value.Replace("\\", "/");
        if (string.Equals(normalized, oldPath, StringComparison.OrdinalIgnoreCase))
            return newPath;

        if (normalized.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase))
            return newPath + normalized[oldPath.Length..];

        return null;
    }

    private bool TryGetSelectedSpriteTarget(out string assetPath, out SpriteImportSettings settings, out int sliceIndex)
    {
        assetPath = string.Empty;
        settings = null!;
        sliceIndex = -1;

        if (!EditorSelection.SelectedSpriteAsset.HasValue)
            return false;

        Sprite sprite = EditorSelection.SelectedSpriteAsset.Value;
        if (string.IsNullOrWhiteSpace(sprite.Path) || string.IsNullOrWhiteSpace(sprite.SpriteId))
            return false;

        assetPath = ResolveAbsoluteAssetPath(sprite.Path);
        settings = _app.TryGetSpriteImportSettings(assetPath) ?? null!;
        if (settings == null)
            return false;

        sliceIndex = settings.Slices.FindIndex(slice => string.Equals(slice.Id, sprite.SpriteId, StringComparison.OrdinalIgnoreCase));
        return sliceIndex >= 0;
    }

    private string MakeUniqueSliceName(SpriteImportSettings settings, string desiredName)
    {
        string candidate = desiredName.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
            candidate = "Sprite";

        string unique = candidate;
        int suffix = 1;
        while (settings.Slices.Any(slice => string.Equals(slice.Name, unique, StringComparison.OrdinalIgnoreCase)))
            unique = $"{candidate} {suffix++}";
        return unique;
    }

    private void ClearSelectionForDeletedPath(string deletedPath)
    {
        string normalized = NormalizePath(deletedPath);
        if (!string.IsNullOrWhiteSpace(_selectedFolderPath) && PathMatchesOrContains(_selectedFolderPath, normalized))
            _selectedFolderPath = null;

        if (!string.IsNullOrWhiteSpace(_currentDirectory) && PathMatchesOrContains(_currentDirectory, normalized))
            _currentDirectory = NormalizePath(_app.AssetsPath!);

        if (!string.IsNullOrWhiteSpace(_contextDirectory) && PathMatchesOrContains(_contextDirectory, normalized))
            _contextDirectory = _currentDirectory;

        if (!string.IsNullOrWhiteSpace(EditorSelection.SelectedAssetPath) && PathMatchesOrContains(EditorSelection.SelectedAssetPath, normalized))
            EditorSelection.ClearAssetSelection();

        if (EditorSelection.SelectedSpriteAsset.HasValue && PathMatchesOrContains(EditorSelection.SelectedSpriteAsset.Value.Path, normalized))
            EditorSelection.ClearAssetSelection();
    }

    private void UpdatePathBasedSelectionState(string oldPath, string newPath)
    {
        _currentDirectory = RewriteMovedPath(_currentDirectory, oldPath, newPath);
        _contextDirectory = RewriteMovedPath(_contextDirectory, oldPath, newPath);
        _selectedFolderPath = RewriteMovedPath(_selectedFolderPath, oldPath, newPath);

        if (!string.IsNullOrWhiteSpace(EditorSelection.SelectedAssetPath))
            EditorSelection.SelectedAssetPath = RewriteMovedPath(EditorSelection.SelectedAssetPath, oldPath, newPath);

        if (EditorSelection.SelectedSpriteAsset.HasValue)
        {
            Sprite sprite = EditorSelection.SelectedSpriteAsset.Value;
            string? updatedPath = RewriteMovedPath(sprite.Path, oldPath, newPath);
            if (!string.IsNullOrWhiteSpace(updatedPath))
            {
                sprite.Path = updatedPath;
                EditorSelection.SelectedSpriteAsset = sprite;
            }
        }

        if (!string.IsNullOrWhiteSpace(EditorSelection.DraggedAssetPath))
            EditorSelection.DraggedAssetPath = RewriteMovedPath(EditorSelection.DraggedAssetPath, oldPath, newPath);

        if (EditorSelection.DraggedSpriteAsset.HasValue)
        {
            Sprite sprite = EditorSelection.DraggedSpriteAsset.Value;
            string? updatedPath = RewriteMovedPath(sprite.Path, oldPath, newPath);
            if (!string.IsNullOrWhiteSpace(updatedPath))
            {
                sprite.Path = updatedPath;
                EditorSelection.DraggedSpriteAsset = sprite;
            }
        }
    }

    private static bool PathMatchesOrContains(string currentPath, string rootPath)
    {
        string current = AssetPathUtility.Normalize(currentPath);
        string root = AssetPathUtility.Normalize(rootPath);
        return string.Equals(current, root, StringComparison.OrdinalIgnoreCase) ||
               current.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? RewriteMovedPath(string? currentPath, string oldPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
            return currentPath;

        string current = AssetPathUtility.Normalize(currentPath);
        string oldNormalized = AssetPathUtility.Normalize(oldPath);
        string newNormalized = AssetPathUtility.Normalize(newPath);
        if (string.Equals(current, oldNormalized, StringComparison.OrdinalIgnoreCase))
            return newNormalized;

        if (current.StartsWith(oldNormalized + "/", StringComparison.OrdinalIgnoreCase))
            return newNormalized + current[oldNormalized.Length..];

        return currentPath;
    }

    private string ResolveContextDirectory() => _contextDirectory ?? _currentDirectory ?? _app.AssetsPath ?? AppContext.BaseDirectory;

    private bool IsWithinAssets(string path)
    {
        if (_app.AssetsPath == null)
            return false;
        return NormalizePath(path).StartsWith(NormalizePath(_app.AssetsPath), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path).Replace("\\", "/");

    private string ResolveAbsoluteAssetPath(string path)
    {
        if (Path.IsPathRooted(path))
            return NormalizePath(path);

        if (_app.ProjectPath == null)
            return NormalizePath(path);

        return NormalizePath(Path.Combine(_app.ProjectPath, path));
    }

    private string ToProjectDisplayPath(string path)
    {
        if (_app.AssetsPath == null)
            return path;

        string assetsPath = NormalizePath(_app.AssetsPath);
        string normalized = NormalizePath(path);
        if (string.Equals(normalized, assetsPath, StringComparison.OrdinalIgnoreCase))
            return "Assets";
        return "Assets/" + Path.GetRelativePath(assetsPath, normalized).Replace("\\", "/");
    }

    private static bool IsImageAsset(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg";
    }

    private IEnumerable<string> GetBrowserDirectories(string directory)
    {
        string normalized = NormalizePath(directory);
        if (!string.Equals(_cachedBrowserDirectory, normalized, StringComparison.OrdinalIgnoreCase))
        {
            _cachedBrowserDirectory = normalized;
            _cachedBrowserDirectories = Directory.GetDirectories(normalized).OrderBy(Path.GetFileName).ToArray();
            _cachedBrowserFiles = Directory.GetFiles(normalized)
                .Where(file => !AssetPathUtility.IsMetaFile(file))
                .OrderBy(Path.GetFileName)
                .ToArray();
        }

        return _cachedBrowserDirectories;
    }

    private IEnumerable<string> GetBrowserFiles(string directory)
    {
        GetBrowserDirectories(directory);
        return _cachedBrowserFiles;
    }

    private IEnumerable<string> GetTreeDirectories(string directory)
    {
        string normalized = NormalizePath(directory);
        if (_cachedTreeDirectories.TryGetValue(normalized, out var cached))
            return cached;

        string[] directories = Directory.GetDirectories(normalized).OrderBy(Path.GetFileName).ToArray();
        _cachedTreeDirectories[normalized] = directories;
        return directories;
    }

    private void InvalidateBrowserCache()
    {
        _cachedBrowserDirectory = null;
        _cachedBrowserDirectories = Array.Empty<string>();
        _cachedBrowserFiles = Array.Empty<string>();
        _cachedTreeDirectories.Clear();
    }

    private void ReloadProjectBrowser()
    {
        AssetPathUtility.InvalidateCache(_app.ProjectPath);
        InvalidateBrowserCache();
        EnsureBrowserState();
    }
}
