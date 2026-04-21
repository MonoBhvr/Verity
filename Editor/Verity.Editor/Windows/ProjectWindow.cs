using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Text;
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
    private enum PublishBuildMode
    {
        Debug,
        Release
    }

    private readonly EditorApp _app;
    private string? _currentDirectory;
    private string? _contextDirectory;
    private string? _selectedFolderPath;

    private string _inputBuffer = "";
    private string? _targetPath;
    private string? _targetSpriteAssetPath;
    private string? _targetSpriteId;
    private string? _creationShaderPath;
    private string? _targetSdfFontSourcePath;
    private string _pathBarBuffer = "";
    private ModalMode _activeMode = ModalMode.None;
    private CreationType _creationType = CreationType.Folder;
    private bool _shouldOpenPopup = false;
    private bool _shouldOpenSdfFontPopup = false;
    private string _sdfFontOutputName = "";
    private string _sdfCharacterSet = "";
    private float _sdfPointSize = 48f;
    private int _sdfAtlasWidth = 1024;
    private int _sdfAtlasHeight = 1024;
    private int _sdfPadding = 12;
    private int _sdfSpread = 8;
    private int _sdfSupersample = 4;

    private float _thumbnailSize = 52f;
    private float _leftPanelWidth = 240f;
    private string? _cachedBrowserDirectory;
    private string[] _cachedBrowserDirectories = Array.Empty<string>();
    private string[] _cachedBrowserFiles = Array.Empty<string>();
    private string? _cachedBrowserItemsDirectory;
    private List<BrowserItem> _cachedBrowserItems = [];
    private readonly Dictionary<string, string[]> _cachedTreeDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CachedTexturePreview> _cachedTexturePreviews = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CachedSpritePreview> _cachedSpritePreviews = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CachedBlueprintPreviewSprite> _cachedBlueprintPreviewSprites = new(StringComparer.OrdinalIgnoreCase);
    private string? _pendingRevealAssetPath;
    private string? _pendingRevealSpriteId;
    private readonly HashSet<string> _selectedBrowserItemKeys = new(StringComparer.OrdinalIgnoreCase);
    private string? _browserSelectionAnchorKey;
    private List<BrowserItem> _visibleBrowserItems = [];

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
        LuaScript,
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

    private readonly record struct CachedTexturePreview(DateTime AssetWriteTimeUtc, BrowserPreview Preview);
    private readonly record struct CachedSpritePreview(DateTime AssetWriteTimeUtc, DateTime MetaWriteTimeUtc, BrowserPreview Preview);
    private readonly record struct CachedBlueprintPreviewSprite(DateTime AssetWriteTimeUtc, Sprite Sprite, bool HasPreview);

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

        if (_shouldOpenSdfFontPopup)
        {
            ImGui.OpenPopup("GenerateSdfFontModal");
            _shouldOpenSdfFontPopup = false;
        }

        DrawInputModal();
        DrawSdfFontGenerationModal();

        ImGui.TextDisabled($"{L10n.Tr("window_project")}: {_app.CurrentProjectName}");
        ImGui.Separator();

        ImGui.Columns(2, "ProjectBrowserColumns", true);
        _leftPanelWidth = MathF.Max(160f, _leftPanelWidth);
        ImGui.SetColumnWidth(0, _leftPanelWidth);

        DrawFolderTreePanel();
        _leftPanelWidth = MathF.Max(160f, ImGui.GetColumnWidth(0));
        ImGui.NextColumn();
        DrawBrowserToolbar();
        DrawBrowserPanel();
        DrawZoomFooter();

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

        Vector2 remaining = ImGui.GetContentRegionAvail();
        float fillHeight = MathF.Max(remaining.Y, 80f);
        float fillWidth = MathF.Max(remaining.X, 1f);
        ImGui.InvisibleButton("##ProjectFolderTreeBackground", new Vector2(MathF.Max(1f, fillWidth), MathF.Max(1f, fillHeight)));
        if (ImGui.BeginDragDropTarget())
        {
            HandleBrowserDropTarget(GetFolderTreeDropDirectory());
            ImGui.EndDragDropTarget();
        }

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

        var items = GetBrowserItems();
        _visibleBrowserItems = items;
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
        ImGui.InvisibleButton("##ProjectBrowserBackground", new Vector2(MathF.Max(1f, fillWidth), MathF.Max(1f, fillHeight)));

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            ClearBrowserSelection();
            _contextDirectory = _currentDirectory;
        }

        if (ImGui.BeginDragDropTarget())
        {
            HandleBrowserDropTarget(_currentDirectory!);

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
            if (ImGui.MenuItem(L10n.Tr("btn_reload")))
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

    private List<BrowserItem> GetBrowserItems()
    {
        string normalized = NormalizePath(_currentDirectory!);
        if (!string.Equals(_cachedBrowserItemsDirectory, normalized, StringComparison.OrdinalIgnoreCase))
        {
            _cachedBrowserItemsDirectory = normalized;
            _cachedBrowserItems = BuildBrowserItems();
        }

        return _cachedBrowserItems;
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
        var item = new BrowserItem(BrowserItemKind.Folder, normalized, name, ToProjectDisplayPath(normalized));
        bool selected = IsBrowserItemSelected(item);
        var row = BeginBrowserRow($"folder_{normalized}", selected, 0f);

        if (row.Clicked)
            HandleBrowserItemClick(item);
        if (row.DoubleClicked)
        {
            EnsureBrowserItemSelected(item);
            NavigateToDirectory(normalized, true);
        }

        if (ImGui.BeginDragDropSource())
        {
            EditorSelection.BeginAssetDrag(normalized);
            ImGui.SetDragDropPayload("ASSET_PATH", null, 0);
            ImGui.Text(L10n.Tr("msg_move_folder", name));
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            HandleBrowserDropTarget(normalized);

            ImGui.EndDragDropTarget();
        }

        if (ImGui.BeginPopupContextItem("FolderBrowserContext"))
        {
            EnsureBrowserItemSelected(item);
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
        MaybeScrollToBrowserItem(item);
        EndBrowserRow();
    }

    private void DrawFileBrowserItem(string path)
    {
        string normalized = NormalizePath(path);
        string fileName = Path.GetFileName(path);
        string subtitle = Path.GetExtension(path).ToUpperInvariant();

        SpriteImportSettings? spriteImport = null;
        if (IsImageAsset(path))
        {
            spriteImport = _app.TryGetSpriteImportSettings(normalized, false);
            if (spriteImport is { SpriteMode: SpriteImportMode.Multiple, Slices.Count: > 0 })
                subtitle = L10n.Tr("label_slice_count", spriteImport.Slices.Count);
        }

        var item = new BrowserItem(BrowserItemKind.File, normalized, fileName, subtitle);
        bool selected = IsBrowserItemSelected(item);
        var row = BeginBrowserRow($"file_{normalized}", selected, 0f);

        if (row.Clicked)
            HandleBrowserItemClick(item);
        if (row.DoubleClicked)
        {
            EnsureBrowserItemSelected(item);
            OnAssetDoubleClicked(normalized);
        }

        if (ImGui.BeginDragDropSource())
        {
            EditorSelection.BeginAssetDrag(normalized);
            ImGui.SetDragDropPayload("ASSET_PATH", null, 0);
            ImGui.Text(L10n.Tr("msg_move_file", fileName));
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginPopupContextItem("FileBrowserContext"))
        {
            EnsureBrowserItemSelected(item);
            DrawFileAssetContextMenu(normalized);
            ImGui.EndPopup();
        }

        if (ImGui.BeginDragDropTarget())
        {
            HandleBrowserDropTarget(NormalizePath(Path.GetDirectoryName(path)!));
            ImGui.EndDragDropTarget();
        }

        DrawFilePreview(normalized, row.PreviewPosition, row.PreviewSize);
        DrawBrowserRowText(row, fileName, subtitle);
        MaybeScrollToBrowserItem(item);
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
        string subtitle = $"{slice.Width} x {slice.Height}";
        var item = new BrowserItem(BrowserItemKind.Sprite, normalized, slice.Name, subtitle, slice.Id);
        bool selected = IsBrowserItemSelected(item);
        var row = BeginBrowserRow($"sprite_{normalized}_{slice.Id}", selected, 24f);

        if (row.Clicked)
            HandleBrowserItemClick(item);

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
            EnsureBrowserItemSelected(item);
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
        MaybeScrollToBrowserItem(item);
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
        Vector2 size = new(MathF.Max(1f, MathF.Max(40f, width)), MathF.Max(1f, height));
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

        if (ImGui.BeginDragDropTarget())
        {
            HandleBrowserDropTarget(GetDropDirectoryForItem(item));
            ImGui.EndDragDropTarget();
        }

        DrawBrowserItemContextMenu(item);
        MaybeScrollToBrowserItem(item);
        ImGui.PopID();
    }

    private void HandleBrowserDropTarget(string targetDirectory)
    {
        unsafe
        {
            var assetPayload = ImGui.AcceptDragDropPayload("ASSET_PATH");
            if (assetPayload.Handle != null && EditorSelection.DraggedAssetPath != null && EditorSelection.DraggedSpriteAsset == null)
            {
                MoveAsset(EditorSelection.DraggedAssetPath, targetDirectory);
                EditorSelection.ClearAssetDrag();
            }

            var entityPayload = ImGui.AcceptDragDropPayload("HIERARCHY_ENTITIES");
            if (entityPayload.Handle != null)
            {
                var draggedEntities = EditorSelection.SelectedEntities.Count > 0
                    ? EditorSelection.SelectedEntities.ToArray()
                    : EditorSelection.DraggedEntity != null
                        ? [EditorSelection.DraggedEntity]
                        : [];

                foreach (var ent in draggedEntities)
                {
                    if (ent != null)
                        _app.SaveEntityAsBlueprint(ent, targetDirectory);
                }

                if (draggedEntities.Length > 0)
                    ReloadProjectBrowser();

                EditorSelection.DraggedEntity = null;
            }
        }
    }

    private string GetDropDirectoryForItem(BrowserItem item)
    {
        if (item.Kind == BrowserItemKind.Folder)
            return item.AssetPath;

        string? parent = Path.GetDirectoryName(item.AssetPath);
        return string.IsNullOrWhiteSpace(parent) ? _currentDirectory! : NormalizePath(parent);
    }

    private string GetFolderTreeDropDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_selectedFolderPath))
            return _selectedFolderPath;
        if (!string.IsNullOrWhiteSpace(_currentDirectory))
            return _currentDirectory;
        return NormalizePath(_app.AssetsPath!);
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
        if (_selectedBrowserItemKeys.Count > 0)
            return _selectedBrowserItemKeys.Contains(GetBrowserItemKey(item));

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
        SelectOnlyBrowserItem(item);
    }

    private void HandleBrowserItemClick(BrowserItem item)
    {
        var io = ImGui.GetIO();
        string key = GetBrowserItemKey(item);

        if (io.KeyShift)
        {
            SelectBrowserItemRange(item, additive: io.KeyCtrl);
            return;
        }

        if (io.KeyCtrl)
        {
            if (!_selectedBrowserItemKeys.Remove(key))
            {
                _selectedBrowserItemKeys.Add(key);
                ApplyPrimaryBrowserItem(item);
            }
            else
            {
                ApplyPrimaryBrowserSelection();
            }

            _browserSelectionAnchorKey = key;
            return;
        }

        SelectOnlyBrowserItem(item);
    }

    private void EnsureBrowserItemSelected(BrowserItem item)
    {
        if (IsBrowserItemSelected(item))
            return;

        SelectOnlyBrowserItem(item);
    }

    private void SelectOnlyBrowserItem(BrowserItem item)
    {
        _selectedBrowserItemKeys.Clear();
        _selectedBrowserItemKeys.Add(GetBrowserItemKey(item));
        _browserSelectionAnchorKey = GetBrowserItemKey(item);
        ApplyPrimaryBrowserItem(item);
    }

    private void SelectBrowserItemRange(BrowserItem item, bool additive)
    {
        if (_visibleBrowserItems.Count == 0)
        {
            SelectOnlyBrowserItem(item);
            return;
        }

        string anchorKey = _browserSelectionAnchorKey ?? GetBrowserItemKey(item);
        int start = _visibleBrowserItems.FindIndex(value => string.Equals(GetBrowserItemKey(value), anchorKey, StringComparison.OrdinalIgnoreCase));
        int end = _visibleBrowserItems.FindIndex(value => string.Equals(GetBrowserItemKey(value), GetBrowserItemKey(item), StringComparison.OrdinalIgnoreCase));
        if (start < 0 || end < 0)
        {
            SelectOnlyBrowserItem(item);
            return;
        }

        if (!additive)
            _selectedBrowserItemKeys.Clear();

        for (int i = Math.Min(start, end); i <= Math.Max(start, end); i++)
            _selectedBrowserItemKeys.Add(GetBrowserItemKey(_visibleBrowserItems[i]));

        ApplyPrimaryBrowserItem(item);
    }

    private void ApplyPrimaryBrowserSelection()
    {
        BrowserItem? primary = null;
        foreach (var item in _visibleBrowserItems)
        {
            if (_selectedBrowserItemKeys.Contains(GetBrowserItemKey(item)))
                primary = item;
        }

        if (primary.HasValue)
        {
            ApplyPrimaryBrowserItem(primary.Value);
            return;
        }

        ClearBrowserSelection();
    }

    private void ApplyPrimaryBrowserItem(BrowserItem item)
    {
        switch (item.Kind)
        {
            case BrowserItemKind.Folder:
                _selectedFolderPath = item.AssetPath;
                _contextDirectory = item.AssetPath;
                EditorSelection.ClearAssetSelection();
                break;
            case BrowserItemKind.File:
                _selectedFolderPath = null;
                _contextDirectory = NormalizePath(Path.GetDirectoryName(item.AssetPath)!);
                EditorSelection.SelectAsset(item.AssetPath);
                break;
            case BrowserItemKind.Sprite:
                _selectedFolderPath = null;
                _contextDirectory = NormalizePath(Path.GetDirectoryName(item.AssetPath)!);
                EditorSelection.SelectSpriteAsset(_app.CreateSpriteReference(item.AssetPath, item.SpriteId));
                break;
        }

        EditorSelection.SelectedEntity = null;
    }

    private void ClearBrowserSelection()
    {
        _selectedBrowserItemKeys.Clear();
        _browserSelectionAnchorKey = null;
        _selectedFolderPath = null;
        EditorSelection.ClearAssetSelection();
        EditorSelection.SelectedEntity = null;
    }

    private static string GetBrowserItemKey(BrowserItem item)
    {
        return item.Kind == BrowserItemKind.Sprite
            ? $"{item.Kind}:{item.AssetPath}:{item.SpriteId}"
            : $"{item.Kind}:{item.AssetPath}";
    }

    public void RevealAsset(string path)
    {
        RevealBrowserItem(NormalizePath(path), null);
    }

    public void RevealSprite(string assetPath, string spriteId)
    {
        RevealBrowserItem(NormalizePath(assetPath), spriteId);
    }

    private void RevealBrowserItem(string assetPath, string? spriteId)
    {
        string normalized = NormalizePath(assetPath);
        string? directory = Path.GetDirectoryName(normalized);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            _currentDirectory = NormalizePath(directory);
            _contextDirectory = _currentDirectory;
            _pathBarBuffer = ToProjectDisplayPath(_currentDirectory);
        }

        _selectedFolderPath = null;
        _pendingRevealAssetPath = normalized;
        _pendingRevealSpriteId = string.IsNullOrWhiteSpace(spriteId) ? null : spriteId;

        if (string.IsNullOrWhiteSpace(spriteId))
            SelectAsset(normalized);
        else
            SelectSprite(normalized, spriteId);
    }

    private void MaybeScrollToBrowserItem(BrowserItem item)
    {
        if (!ShouldScrollToBrowserItem(item))
            return;

        ImGui.SetScrollHereY(0.35f);
    }

    private bool ShouldScrollToBrowserItem(BrowserItem item)
    {
        if (string.IsNullOrWhiteSpace(_pendingRevealAssetPath))
            return false;

        if (!string.Equals(item.AssetPath, _pendingRevealAssetPath, StringComparison.OrdinalIgnoreCase))
            return false;

        if (item.Kind == BrowserItemKind.Sprite)
        {
            if (!string.Equals(item.SpriteId, _pendingRevealSpriteId, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        else if (!string.IsNullOrWhiteSpace(_pendingRevealSpriteId))
        {
            return false;
        }

        _pendingRevealAssetPath = null;
        _pendingRevealSpriteId = null;
        return true;
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
                DrawFileAssetContextMenu(item.AssetPath);
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
        ImGui.InvisibleButton("##row", new Vector2(MathF.Max(1f, rowWidth), MathF.Max(1f, rowHeight)));
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
        if (path.EndsWith(".verity", StringComparison.OrdinalIgnoreCase) &&
            TryGetFullTexturePreview(_app.EditorLogoPath, out var worldPreview))
        {
            DrawTexturePreview(worldPreview, position, size);
            return;
        }

        if (IsTileAsset(path) && TryGetTilePreviewSprite(path, out var tileSprite))
        {
            DrawSpritePreview(tileSprite, position, size);
            return;
        }

        if (IsImageAsset(path) && TryGetFullTexturePreview(path, out var preview))
        {
            DrawTexturePreview(preview, position, size);
            return;
        }

        if (path.EndsWith(".blueprint", StringComparison.OrdinalIgnoreCase) &&
            TryGetCachedBlueprintPreviewSprite(path, out Sprite blueprintSprite) &&
            TryGetSpritePreview(blueprintSprite, out var blueprintPreview))
        {
            DrawTexturePreview(blueprintPreview, position, size);
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
        string normalized = NormalizePath(assetPath);
        DateTime assetWriteTimeUtc = GetFileWriteTimeUtc(normalized);
        if (_cachedTexturePreviews.TryGetValue(normalized, out var cached) &&
            cached.AssetWriteTimeUtc == assetWriteTimeUtc)
        {
            preview = cached.Preview;
            return true;
        }

        try
        {
            SpriteImportSettings? settings = _app.TryGetSpriteImportSettings(normalized, false);
            var texture = _app.TextureManager.Load(normalized, settings?.Filter ?? SpriteTextureFilter.Point);

            preview = new BrowserPreview(texture, new Vector2(0, 1), new Vector2(1, 0), texture.Width, texture.Height);
            _cachedTexturePreviews[normalized] = new CachedTexturePreview(assetWriteTimeUtc, preview);
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

            string normalized = NormalizePath(fullPath);
            string cacheKey = string.IsNullOrWhiteSpace(sprite.SpriteId)
                ? normalized
                : $"{normalized}::{sprite.SpriteId}";
            DateTime assetWriteTimeUtc = GetFileWriteTimeUtc(normalized);
            DateTime metaWriteTimeUtc = GetFileWriteTimeUtc(AssetPathUtility.GetMetaPath(normalized));
            if (_cachedSpritePreviews.TryGetValue(cacheKey, out var cached) &&
                cached.AssetWriteTimeUtc == assetWriteTimeUtc &&
                cached.MetaWriteTimeUtc == metaWriteTimeUtc)
            {
                preview = cached.Preview;
                return true;
            }

            SpriteImportSettings? settings = _app.TryGetSpriteImportSettings(normalized, false);
            var texture = _app.TextureManager.Load(normalized, settings?.Filter ?? SpriteTextureFilter.Point);
            var resolvedSprite = new Sprite(AssetPathUtility.Normalize(normalized), sprite.Guid, sprite.SpriteId);
            var slice = AssetPathUtility.ResolveSpriteSlice(normalized, resolvedSprite, texture.Width, texture.Height);
            Vector2 uvMin = new(slice.X / (float)Math.Max(1, texture.Width), 1f - (slice.Y / (float)Math.Max(1, texture.Height)));
            Vector2 uvMax = new((slice.X + slice.Width) / (float)Math.Max(1, texture.Width), 1f - ((slice.Y + slice.Height) / (float)Math.Max(1, texture.Height)));
            preview = new BrowserPreview(texture, uvMin, uvMax, slice.Width, slice.Height);
            _cachedSpritePreviews[cacheKey] = new CachedSpritePreview(assetWriteTimeUtc, metaWriteTimeUtc, preview);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryGetCachedBlueprintPreviewSprite(string path, out Sprite sprite)
    {
        string normalized = NormalizePath(path);
        DateTime assetWriteTimeUtc = GetFileWriteTimeUtc(normalized);
        if (_cachedBlueprintPreviewSprites.TryGetValue(normalized, out var cached) &&
            cached.AssetWriteTimeUtc == assetWriteTimeUtc)
        {
            sprite = cached.Sprite;
            return cached.HasPreview;
        }

        bool hasPreview = _app.TryGetBlueprintPreviewSprite(normalized, out sprite);
        _cachedBlueprintPreviewSprites[normalized] = new CachedBlueprintPreviewSprite(assetWriteTimeUtc, sprite, hasPreview);
        return hasPreview;
    }

    private static DateTime GetFileWriteTimeUtc(string path)
    {
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
    }

    private static bool IsTileAsset(string path)
    {
        return path.EndsWith(".tile", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".animtile", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".ruletile", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetTilePreviewSprite(string path, out Sprite sprite)
    {
        sprite = default;
        TileBase? tile = TileAssetCache.Load(path, assetRootPath: _app.ProjectPath);
        Sprite? previewSprite = tile switch
        {
            Tile simpleTile => simpleTile.Sprite,
            AnimatedTile animatedTile => animatedTile.Sprites.FirstOrDefault(),
            RuleTile ruleTile => ruleTile.DefaultSprite ?? ruleTile.Rules.Select(rule => rule.Sprite).FirstOrDefault(value => value.HasValue),
            _ => null
        };

        if (!previewSprite.HasValue || string.IsNullOrWhiteSpace(previewSprite.Value.Path))
            return false;

        sprite = previewSprite.Value;
        return true;
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
            HandleBrowserDropTarget(normalized);

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
        if (ImGui.MenuItem("Lua Script"))
            OpenCreatePopup(target, CreationType.LuaScript);
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

    private void DrawFileAssetContextMenu(string assetPath)
    {
        string normalized = NormalizePath(assetPath);
        string parentDir = NormalizePath(Path.GetDirectoryName(normalized)!);
        _contextDirectory = parentDir;
        _creationShaderPath = normalized.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) && _app.ProjectPath != null
            ? Path.GetRelativePath(_app.ProjectPath, normalized).Replace("\\", "/")
            : null;

        if (ImGui.MenuItem(L10n.Tr("menu_show_in_explorer")))
            Process.Start("explorer.exe", $"/select,\"{normalized.Replace("/", "\\")}\"");
        if (ImGui.MenuItem(L10n.Tr("btn_reload")))
            ReloadProjectBrowser();
        if (ImGui.MenuItem(L10n.Tr("btn_rename")))
            OpenRenamePopup(normalized);
        if (ImGui.MenuItem(L10n.Tr("btn_delete")))
            DeleteAsset(normalized);

        if (IsFontSourceAsset(normalized))
        {
            ImGui.Separator();
            if (ImGui.MenuItem(L10n.Tr("menu_generate_sdf_font")))
                OpenGenerateSdfFontPopup(normalized);
        }

        ImGui.Separator();
        DrawCreateMenu(parentDir);
    }

    private void OpenGenerateSdfFontPopup(string assetPath)
    {
        string normalized = NormalizePath(assetPath);
        _targetSdfFontSourcePath = normalized;
        _sdfFontOutputName = Path.GetFileNameWithoutExtension(normalized);
        _sdfPointSize = 48f;
        _sdfAtlasWidth = 1024;
        _sdfAtlasHeight = 1024;
        _sdfSpread = 8;
        _sdfPadding = 12;
        _sdfSupersample = 4;
        _sdfCharacterSet = BuildEditorLocalizedCharacterSet();
        _shouldOpenSdfFontPopup = true;
    }

    private unsafe void DrawSdfFontGenerationModal()
    {
        var viewport = ImGui.GetMainViewport();
        var center = new Vector2(viewport.Pos.X + viewport.Size.X * 0.5f, viewport.Pos.Y + viewport.Size.Y * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(620f, 0f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal("GenerateSdfFontModal", null, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.Text(L10n.Tr("msg_generate_sdf_font"));
        ImGui.Separator();

        string sourceDisplay = string.IsNullOrWhiteSpace(_targetSdfFontSourcePath) ? L10n.Tr("msg_none") : ToProjectDisplayPath(_targetSdfFontSourcePath);
        ImGui.Text($"{L10n.Tr("label_source_font")}: {sourceDisplay}");
        ImGui.InputText(L10n.Tr("label_output_name"), ref _sdfFontOutputName, 128);
        ImGui.InputFloat(L10n.Tr("label_point_size"), ref _sdfPointSize, 1f, 8f, "%.0f");
        ImGui.InputInt(L10n.Tr("label_atlas_width"), ref _sdfAtlasWidth);
        ImGui.InputInt(L10n.Tr("label_atlas_height"), ref _sdfAtlasHeight);
        ImGui.InputInt(L10n.Tr("label_padding"), ref _sdfPadding);
        ImGui.InputInt(L10n.Tr("label_spread"), ref _sdfSpread);
        ImGui.InputInt(L10n.Tr("label_supersample"), ref _sdfSupersample);

        ImGui.Text(L10n.Tr("label_charset_presets"));
        if (ImGui.Button(L10n.Tr("btn_charset_basic_latin")))
            _sdfCharacterSet = SdfFontGenerationOptions.DefaultCharacterSet;
        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("btn_charset_editor_ko")))
            _sdfCharacterSet = BuildEditorLocalizedCharacterSet();
        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("btn_charset_full_hangul")))
            _sdfCharacterSet = BuildFullHangulCharacterSet();

        string glyphCountText = _sdfCharacterSet.EnumerateRunes().Count().ToString();
        ImGui.Text($"{L10n.Tr("label_glyph_count")}: {glyphCountText}");
        ImGui.InputTextMultiline(L10n.Tr("label_characters"), ref _sdfCharacterSet, 65536, new Vector2(560f, 180f));

        var buttonSize = new Vector2(140f, 0f);
        bool closePopup = false;
        if (ImGui.Button(L10n.Tr("btn_generate"), buttonSize))
            closePopup = TryGenerateSdfFontAsset();

        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("btn_cancel"), buttonSize) || ImGui.IsKeyPressed(ImGuiKey.Escape))
            closePopup = true;

        if (closePopup)
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
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
        SelectOnlyBrowserItem(new BrowserItem(
            BrowserItemKind.Folder,
            NormalizePath(path),
            Path.GetFileName(path),
            ToProjectDisplayPath(path)));
    }

    private void SelectAsset(string path)
    {
        string normalized = NormalizePath(path);
        SelectOnlyBrowserItem(new BrowserItem(
            BrowserItemKind.File,
            normalized,
            Path.GetFileName(normalized),
            Path.GetExtension(normalized).ToUpperInvariant()));
    }

    private void SelectSprite(string assetPath, string spriteId)
    {
        string normalized = NormalizePath(assetPath);
        string title = spriteId;
        var settings = _app.TryGetSpriteImportSettings(normalized, false);
        var slice = settings?.Slices.FirstOrDefault(item => string.Equals(item.Id, spriteId, StringComparison.OrdinalIgnoreCase));
        if (slice != null)
            title = slice.Name;

        SelectOnlyBrowserItem(new BrowserItem(
            BrowserItemKind.Sprite,
            normalized,
            title,
            string.Empty,
            spriteId));
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
            string next = NormalizePath(Path.Combine(dir, name + L10n.Tr("label_copy_suffix") + ext));
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
        duplicated.Name = MakeUniqueSliceName(settings, $"{duplicated.Name} {L10n.Tr("label_copy_word")}");
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
            CreationType.Script => L10n.Tr("creation_default_script"),
            CreationType.LuaScript => "NewLuaScript",
            CreationType.World => L10n.Tr("creation_default_world"),
            CreationType.Shader => L10n.Tr("creation_default_shader"),
            CreationType.Style => L10n.Tr("creation_default_style"),
            CreationType.UiScreen => L10n.Tr("creation_default_ui_screen"),
            CreationType.UiStyle => L10n.Tr("creation_default_ui_style"),
            CreationType.Tile => L10n.Tr("creation_default_tile"),
            CreationType.AnimatedTile => L10n.Tr("creation_default_animated_tile"),
            CreationType.RuleTile => L10n.Tr("creation_default_rule_tile"),
            _ => L10n.Tr("creation_default_folder")
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
                case CreationType.LuaScript:
                    File.WriteAllText(fullPath + ".lua", "function Awake()\nend\n\nfunction Start()\nend\n\nfunction Update(deltaTime)\nend\n");
                    break;
                case CreationType.World:
                    var world = new World(_inputBuffer);
                    var cameraEntity = world.CreateEntity(L10n.Tr("creation_default_main_camera"));
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

    private bool TryGenerateSdfFontAsset()
    {
        if (!OperatingSystem.IsWindows())
        {
            Verity.Core.Debug.LogError("[Font] SDF font generation is only supported on Windows.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_targetSdfFontSourcePath) || !File.Exists(_targetSdfFontSourcePath))
        {
            Verity.Core.Debug.LogError("[Font] Missing source font file.");
            return false;
        }

        string outputName = (_sdfFontOutputName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(outputName))
        {
            Verity.Core.Debug.LogError("[Font] Output name is required.");
            return false;
        }

        if (outputName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            Verity.Core.Debug.LogError("[Font] Output name contains invalid characters.");
            return false;
        }

        try
        {
            string sourcePath = NormalizePath(_targetSdfFontSourcePath);
            string directory = NormalizePath(Path.GetDirectoryName(sourcePath)!);
            string outputPath = NormalizePath(Path.Combine(directory, outputName + SdfFontAsset.PrimaryExtension));

            var options = new SdfFontGenerationOptions
            {
                PointSize = _sdfPointSize,
                AtlasWidth = _sdfAtlasWidth,
                AtlasHeight = _sdfAtlasHeight,
                Padding = _sdfPadding,
                Spread = _sdfSpread,
                Supersample = _sdfSupersample,
                Characters = _sdfCharacterSet,
                OverwriteExistingFiles = true
            };

            var asset = SdfFontAssetGenerator.Generate(sourcePath, outputPath, options);
            AssetPathUtility.EnsureMetaAndGetGuid(outputPath);

            foreach (var atlasPage in asset.AtlasPages)
            {
                string atlasPath = Path.IsPathRooted(atlasPage.Path)
                    ? NormalizePath(atlasPage.Path)
                    : NormalizePath(Path.Combine(directory, atlasPage.Path));
                AssetPathUtility.EnsureMetaAndGetGuid(atlasPath);
            }

            AssetPathUtility.InvalidateCache(_app.ProjectPath);
            InvalidateBrowserCache();
            SelectAsset(outputPath);
            Verity.Core.Debug.Log($"[Font] Generated SDF font asset: {ToProjectDisplayPath(outputPath)}");
            return true;
        }
        catch (Exception e)
        {
            Verity.Core.Debug.LogError($"[Font] SDF generation failed: {e.Message}");
            return false;
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

        if (normalized.EndsWith(".blueprint", StringComparison.OrdinalIgnoreCase))
        {
            _app.OpenBlueprintAsset(normalized);
            return;
        }

        if (normalized.EndsWith(".ui", StringComparison.OrdinalIgnoreCase))
        {
            SelectAsset(normalized);
            _app.OpenWindow<UIEditorWindow>();
            return;
        }

        if (normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ||
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
        _app.SetActiveAssetContext(normalized, EditorAssetKind.World);
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

        if (_app.IsEditingBlueprint)
        {
            _app.SaveActiveBlueprint();
            return;
        }

        string? path = _app.ActiveAssetPath;
        if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".verity", StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(_app.AssetsPath, $"{WorldManager.ActiveWorld.Name}.verity");

        File.WriteAllText(path, SceneSerializer.Serialize(WorldManager.ActiveWorld));
        AssetPathUtility.EnsureMetaAndGetGuid(path);
        _app.SetActiveAssetContext(path, EditorAssetKind.World);
        _app.ResetDirty();
        _app.ShowOverlayMessage(L10n.Tr("msg_world_saved", WorldManager.ActiveWorld.Name));
    }

    public void PublishSingleFile()
    {
        PublishBuild(PublishBuildMode.Release);
    }

    public void PublishDebugBuild()
    {
        PublishBuild(PublishBuildMode.Debug);
    }

    public void PublishReleaseBuild()
    {
        PublishBuild(PublishBuildMode.Release);
    }

    private void PublishBuild(PublishBuildMode mode)
    {
        if (_app.IsBuilding || _app.ProjectPath == null)
            return;

        Task.Run(() =>
        {
            _app.IsBuilding = true;
            try
            {
                _app.BuildStatus = L10n.Tr("msg_publish_preparing_dir");
                string publishDir = Path.Combine(_app.ProjectPath, "Build", mode.ToString());
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
                _app.BuildStatus = L10n.Tr("msg_publish_syncing_assets");
                string gameAssets = Path.Combine(gameProjDir, "Assets");
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
                string gameDll = Path.Combine(gameProjDir, "UserScripts.dll");
                _app.ScriptCompiler?.CompileToFile(gameDll);

                _app.BuildStatus = L10n.Tr("msg_publish_running_dotnet");
                string publishArgs = mode == PublishBuildMode.Debug
                    ? $"publish \"{Path.Combine(gameProjDir, "Verity.Game.csproj")}\" -c Debug -r win-x64 --self-contained true -p:PublishSingleFile=false -p:RuntimeShowConsole=true -p:RuntimeDiagnostics=true -p:DebugSymbols=true -p:DebugType=portable -o \"{publishDir}\""
                    : $"publish \"{Path.Combine(gameProjDir, "Verity.Game.csproj")}\" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:RuntimeShowConsole=false -p:RuntimeDiagnostics=false -p:DebugSymbols=false -p:DebugType=None -o \"{publishDir}\"";

                var psi = new ProcessStartInfo("dotnet", publishArgs)
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
                    _app.BuildStatus = L10n.Tr("msg_done");
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
            candidate = L10n.Tr("CreationType_Sprite");

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

        _selectedBrowserItemKeys.RemoveWhere(key => key.Contains(normalized, StringComparison.OrdinalIgnoreCase));

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
        RewriteSelectedBrowserItems(oldPath, newPath);

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

    private void RewriteSelectedBrowserItems(string oldPath, string newPath)
    {
        if (_selectedBrowserItemKeys.Count == 0)
            return;

        var updated = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in _selectedBrowserItemKeys)
        {
            string rewritten = key.Replace(AssetPathUtility.Normalize(oldPath), AssetPathUtility.Normalize(newPath), StringComparison.OrdinalIgnoreCase);
            updated.Add(rewritten);
        }

        _selectedBrowserItemKeys.Clear();
        foreach (string key in updated)
            _selectedBrowserItemKeys.Add(key);

        if (!string.IsNullOrWhiteSpace(_browserSelectionAnchorKey))
            _browserSelectionAnchorKey = _browserSelectionAnchorKey.Replace(AssetPathUtility.Normalize(oldPath), AssetPathUtility.Normalize(newPath), StringComparison.OrdinalIgnoreCase);
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

    private static bool IsFontSourceAsset(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".ttf" or ".otf";
    }

    private string BuildEditorLocalizedCharacterSet()
    {
        var builder = new StringBuilder(SdfFontGenerationOptions.DefaultCharacterSet);
        foreach (string lang in L10n.AvailableLanguages)
        {
            foreach (string path in L10n.EnumerateCandidatePaths(lang))
            {
                AppendLocaleCharacters(builder, path);
            }
        }
        return string.Concat(EnumerateDistinctRunes(builder.ToString()));
    }

    private static string BuildFullHangulCharacterSet()
    {
        var builder = new StringBuilder(SdfFontGenerationOptions.DefaultCharacterSet.Length + 12000);
        builder.Append(SdfFontGenerationOptions.DefaultCharacterSet);
        for (int codepoint = 0xAC00; codepoint <= 0xD7A3; codepoint++)
            builder.Append(char.ConvertFromUtf32(codepoint));
        return string.Concat(EnumerateDistinctRunes(builder.ToString()));
    }

    private static void AppendLocaleCharacters(StringBuilder builder, string path)
    {
        if (!File.Exists(path))
            return;

        try
        {
            string json = File.ReadAllText(path);
            var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (strings == null)
                return;

            foreach ((string _, string value) in strings)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    builder.Append(value);
            }
        }
        catch
        {
        }
    }

    private static IEnumerable<string> EnumerateDistinctRunes(string text)
    {
        var seen = new HashSet<int>();
        foreach (var rune in text.EnumerateRunes())
        {
            if (seen.Add(rune.Value))
                yield return rune.ToString();
        }
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
        _cachedBrowserItemsDirectory = null;
        _cachedBrowserItems = [];
        _cachedTreeDirectories.Clear();
        _cachedTexturePreviews.Clear();
        _cachedSpritePreviews.Clear();
        _cachedBlueprintPreviewSprites.Clear();
    }

    private void ReloadProjectBrowser()
    {
        AssetPathUtility.InvalidateCache(_app.ProjectPath);
        InvalidateBrowserCache();
        EnsureBrowserState();
    }
}
