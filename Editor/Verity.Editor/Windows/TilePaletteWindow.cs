using System.Numerics;
using System.Text.Json;
using System.Linq;
using Hexa.NET.ImGui;
using Irodori.Backend.OpenGL;
using Irodori.Texture;
using Verity.Core;
using Verity.Core.World;
using Verity.Core.ECS;
using Verity.Core.Serialization;

namespace Verity.Editor.Windows;

public unsafe class TilePaletteWindow : EditorWindow
{
    private readonly EditorApp _app;
    private List<string> _tileAssetPaths = new();
    private Dictionary<string, TileBase> _loadedTileCache = new();
    private DateTime _lastRefresh = DateTime.MinValue;

    private static readonly JsonSerializerOptions _tileOptions = new() { 
        WriteIndented = true,
        Converters = { new Vector2Converter(), new Vector3Converter(), new Vector4Converter(), new SpriteConverter(), new StyleAssetConverter(), new ShaderAssetConverter(), new Verity.Core.Serialization.ColorConverter(), new TileBaseConverter(), new TilemapTilesConverter() }
    };

    public TilePaletteWindow(EditorApp app) : base(L10n.Tr("window_tile_palette"))
    {
        _app = app;
    }

    public override void RefreshTitle() { Title = L10n.Tr("window_tile_palette"); }

    public override unsafe void OnGui()
    {
        RefreshAssetListIfNeeded();
        SyncSelectionFromCurrentTile();

        DrawToolToolbar();
        ImGui.Separator();

        if (ImGui.Button("+ " + L10n.Tr("menu_create"))) ImGui.OpenPopup("CreateTilePopup");
        if (ImGui.BeginPopup("CreateTilePopup"))
        {
            if (ImGui.MenuItem(L10n.Tr("CreationType_Tile"))) RequestCreateTile(ProjectWindow.CreationType.Tile);
            if (ImGui.MenuItem(L10n.Tr("CreationType_AnimatedTile"))) RequestCreateTile(ProjectWindow.CreationType.AnimatedTile);
            if (ImGui.MenuItem(L10n.Tr("CreationType_RuleTile"))) RequestCreateTile(ProjectWindow.CreationType.RuleTile);
            ImGui.EndPopup();
        }

        ImGui.Text(L10n.Tr("label_tile_assets"));
        if (ImGui.BeginChild("TileList", new System.Numerics.Vector2(0, 300), ImGuiChildFlags.Borders))
        {
            float cellSize = 88f;
            int columns = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / (cellSize + 10f)));

            if (ImGui.BeginTable("TileGrid", columns, ImGuiTableFlags.None))
            {
                foreach (var path in _tileAssetPaths)
                {
                    ImGui.TableNextColumn();
                    string fileName = Path.GetFileNameWithoutExtension(path);
                    bool isSelected = EditorSelection.SelectedAssetPath == path;

                    ImGui.PushID(path);
                    
                    // Background for selection
                    var drawList = ImGui.GetWindowDrawList();
                    var pos = ImGui.GetCursorScreenPos();
                    if (isSelected) drawList.AddRectFilled(pos, pos + new System.Numerics.Vector2(cellSize, cellSize + 20), ImGui.GetColorU32(ImGuiCol.HeaderActive), 4f);
                    else if (ImGui.IsMouseHoveringRect(pos, pos + new System.Numerics.Vector2(cellSize, cellSize + 20))) drawList.AddRectFilled(pos, pos + new System.Numerics.Vector2(cellSize, cellSize + 20), ImGui.GetColorU32(ImGuiCol.HeaderHovered), 4f);

                    // Draw Preview
                    var tile = GetLoadedTile(path);
                    if (tile != null)
                    {
                        var preview = TryGetPreviewData(GetPreviewSprite(tile));
                        
                        if (preview.Texture is OpenGlTexture glTex)
                        {
                            ImGui.SetCursorScreenPos(pos + new System.Numerics.Vector2(5, 5));
                            ImGui.Image(new ImTextureRef(null, new ImTextureID((nint)glTex.Id)), new Vector2(cellSize - 10, cellSize - 10), preview.UvMin, preview.UvMax);
                        }
                        else
                        {
                            // Fallback to colored box
                            drawList.AddRectFilled(pos + new System.Numerics.Vector2(5, 5), pos + new System.Numerics.Vector2(cellSize - 5, cellSize - 5), ImGui.GetColorU32((Vector4)tile.Color));
                        }

                        drawList.AddRect(
                            pos + new System.Numerics.Vector2(5, 5),
                            pos + new System.Numerics.Vector2(cellSize - 5, cellSize - 5),
                            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, isSelected ? 0.85f : 0.2f)),
                            4f,
                            0,
                            isSelected ? 2f : 1f);
                    }

                    // Click area
                    ImGui.SetCursorScreenPos(pos);
                    if (ImGui.InvisibleButton("##tilebtn", new Vector2(MathF.Max(1f, cellSize), MathF.Max(1f, cellSize + 20))))
                    {
                        SelectTileAsset(path, tile);
                    }
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(fileName);
                    }
                    
                    // Name label
                    string displayName = fileName;
                    if (displayName.Length > 10) displayName = displayName.Substring(0, 8) + "..";
                    var textSize = ImGui.CalcTextSize(displayName);
                    ImGui.SetCursorScreenPos(pos + new System.Numerics.Vector2((cellSize - textSize.X) * 0.5f, cellSize + 2));
                    ImGui.Text(displayName);

                    ImGui.PopID();
                }
                ImGui.EndTable();
            }
        }
        ImGui.EndChild();

        ImGui.Separator();
        if (EditorSelection.SelectedTile != null)
        {
            DrawSelectedTilePreview(EditorSelection.SelectedTile);
            DrawTileProperties(EditorSelection.SelectedTile);
        }
    }

    private void DrawToolToolbar()
    {
        var tools = Enum.GetValues<TilemapEditor.Tool>();
        foreach (var tool in tools)
        {
            bool active = EditorSelection.SelectedTool == tool;
            if (active) ImGui.PushStyleColor(ImGuiCol.Button, new System.Numerics.Vector4(0.2f, 0.4f, 0.6f, 1.0f));
            
            if (ImGui.Button(L10n.Tr($"tile_tool_{tool}")))
                SetSelectedTool(tool);
            
            if (active) ImGui.PopStyleColor();
            ImGui.SameLine();
        }
        ImGui.NewLine();

        float controlWidth = MathF.Min(220f, MathF.Max(140f, ImGui.GetContentRegionAvail().X));
        ImGui.SetNextItemWidth(controlWidth);
        int brushSize = EditorSelection.TileBrushSize;
        if (ImGui.DragInt(L10n.Tr("tile_brush_size"), ref brushSize, 0.1f, 1, 32))
            EditorSelection.TileBrushSize = Math.Max(1, brushSize);
        if (ImGui.IsItemActivated())
            _app.BeginUndoAction();
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            EditorSelection.TileBrushSize = Math.Max(1, EditorSelection.TileBrushSize);
            _app.EndUndoAction();
        }

        var shape = EditorSelection.TileBrushShape;
        ImGui.SetNextItemWidth(controlWidth);
        if (ImGui.BeginCombo(L10n.Tr("tile_brush_shape"), L10n.Tr($"tile_brush_shape_{shape}")))
        {
            foreach (var brushShape in Enum.GetValues<TilemapEditor.BrushShape>())
            {
                bool selected = brushShape == shape;
                if (ImGui.Selectable(L10n.Tr($"tile_brush_shape_{brushShape}"), selected))
                    SetBrushShape(brushShape);
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
    }

    public void RestoreUndoState(string? selectedAssetPath)
    {
        if (string.IsNullOrWhiteSpace(selectedAssetPath))
        {
            EditorSelection.SelectedAssetPath = null;
            EditorSelection.SelectedTile = null;
            return;
        }

        EditorSelection.SelectedAssetPath = selectedAssetPath;
        EditorSelection.SelectedTile = GetLoadedTile(selectedAssetPath);
    }

    private void SetSelectedTool(TilemapEditor.Tool tool)
    {
        if (EditorSelection.SelectedTool == tool)
            return;

        _app.RecordUndo();
        EditorSelection.SelectedTool = tool;
    }

    private void SetBrushShape(TilemapEditor.BrushShape brushShape)
    {
        if (EditorSelection.TileBrushShape == brushShape)
            return;

        _app.RecordUndo();
        EditorSelection.TileBrushShape = brushShape;
    }

    private void RequestCreateTile(ProjectWindow.CreationType type)
    {
        var pw = _app.GetWindow<ProjectWindow>();
        if (pw != null && _app.AssetsPath != null)
        {
            pw.OpenCreatePopup(_app.AssetsPath, type);
        }
    }

    private void LoadTile(string path)
    {
        try {
            EditorSelection.SelectedTile = TileAssetCache.Load(path);
        } catch (Exception e) { Verity.Core.Debug.LogError($"Failed to load tile: {e.Message}"); }
    }

    private void SaveCurrentTile()
    {
        if (EditorSelection.SelectedTile == null || EditorSelection.SelectedAssetPath == null) return;
        try {
            string json = JsonSerializer.Serialize<TileBase>(EditorSelection.SelectedTile, _tileOptions);
            File.WriteAllText(EditorSelection.SelectedAssetPath, json);
            EditorSelection.SelectedTile.AssetPath = AssetPathUtility.Normalize(EditorSelection.SelectedAssetPath);
            EditorSelection.SelectedTile.AssetGuid = AssetPathUtility.TryGetGuid(EditorSelection.SelectedAssetPath);
            TileAssetCache.Invalidate(EditorSelection.SelectedAssetPath, _app.ProjectPath);
            TileAssetCache.Load(EditorSelection.SelectedAssetPath, assetRootPath: _app.ProjectPath);
        } catch (Exception e) { Verity.Core.Debug.LogError($"Failed to save tile: {e.Message}"); }
    }

    private void DrawTileProperties(TileBase tile)
    {
        ImGui.TextColored(new System.Numerics.Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"{L10n.Tr("label_tile")}: {tile.Name}");
        
        string name = tile.Name;
        if (ImGui.InputText(L10n.Tr("label_name"), ref name, 64)) { tile.Name = name; SaveCurrentTile(); }

        bool collidable = tile.IsCollidable;
        if (ImGui.Checkbox(L10n.Tr("tile_collidable"), ref collidable)) { tile.IsCollidable = collidable; SaveCurrentTile(); }

        var color = (System.Numerics.Vector4)tile.Color;
        if (ImGui.ColorEdit4(L10n.Tr("field_Color"), ref color)) { tile.Color = (Verity.Core.Color)color; SaveCurrentTile(); }

        if (tile is Tile normalTile)
        {
            DrawSpriteField(L10n.Tr("field_Sprite"), normalTile.Sprite ?? default, s => { normalTile.Sprite = (Sprite)s!; SaveCurrentTile(); });
        }
        else if (tile is AnimatedTile animTile)
        {
            float speed = animTile.AnimationSpeed;
            if (ImGui.DragFloat(L10n.Tr("tile_animation_speed"), ref speed, 0.1f)) { animTile.AnimationSpeed = speed; SaveCurrentTile(); }
            
            ImGui.Text(L10n.Tr("tile_frames"));
            for (int i = 0; i < animTile.Sprites.Count; i++)
            {
                int idx = i;
                DrawSpriteField(string.Format(L10n.Tr("tile_frame_n"), i), animTile.Sprites[i], s => { animTile.Sprites[idx] = (Sprite)s!; SaveCurrentTile(); });
            }
            if (ImGui.Button(L10n.Tr("btn_add_frame"))) { animTile.Sprites.Add(default); SaveCurrentTile(); }
        }
        else if (tile is RuleTile ruleTile)
        {
            DrawSpriteField(L10n.Tr("tile_default_sprite"), ruleTile.DefaultSprite ?? default, s => { ruleTile.DefaultSprite = (Sprite)s!; SaveCurrentTile(); });

            ImGui.Separator();
            ImGui.Text(L10n.Tr("tile_rules"));
            
            for (int i = 0; i < ruleTile.Rules.Count; i++)
            {
                var rule = ruleTile.Rules[i];
                ImGui.PushID(i);
                if (ImGui.CollapsingHeader(string.Format(L10n.Tr("tile_rule_n"), i), ImGuiTreeNodeFlags.DefaultOpen))
                {
                    DrawSpriteField(L10n.Tr("tile_rule_output"), rule.Sprite ?? default, s => { rule.Sprite = (Sprite)s!; SaveCurrentTile(); });
                    
                    ImGui.Text(L10n.Tr("tile_neighbors"));
                    // Draw 3x3 grid omitting center
                    if (ImGui.BeginTable("NeighborGrid", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
                    {
                        for (int y = 0; y < 3; y++)
                        {
                            ImGui.TableNextRow();
                            for (int x = 0; x < 3; x++)
                            {
                                ImGui.TableSetColumnIndex(x);
                                if (x == 1 && y == 1)
                                {
                                    // Center is self
                                    ImGui.Text(L10n.Tr("tile_self"));
                                    continue;
                                }

                                // Map 3x3 (excl center) to index 0-7
                                // 0 1 2
                                // 3 X 4
                                // 5 6 7
                                int index = y * 3 + x;
                                if (index > 4) index--; 

                                var current = rule.Neighbors[index];
                                string label = current switch
                                {
                                    RuleTile.Neighbor.Any => "?",
                                    RuleTile.Neighbor.Required => "O",
                                    RuleTile.Neighbor.NotRequired => "X",   
                                    _ => "?"
                                };

                                if (ImGui.Button($"{label}##{index}", new System.Numerics.Vector2(30, 30)))
                                {
                                    // Cycle state
                                    rule.Neighbors[index] = current switch
                                    {
                                        RuleTile.Neighbor.Any => RuleTile.Neighbor.Required,
                                        RuleTile.Neighbor.Required => RuleTile.Neighbor.NotRequired,
                                        _ => RuleTile.Neighbor.Any
                                    };
                                    SaveCurrentTile();
                                }
                                if (ImGui.IsItemHovered())
                                {
                                    ImGui.SetTooltip(current.ToString());
                                }
                            }
                        }
                        ImGui.EndTable();
                    }

                    if (ImGui.Button(L10n.Tr("btn_remove_rule")))
                    {
                        ruleTile.Rules.RemoveAt(i);
                        SaveCurrentTile();
                        i--; 
                    }
                }
                ImGui.PopID();
            }

            if (ImGui.Button(L10n.Tr("btn_add_rule")))
            {
                ruleTile.Rules.Add(new RuleTile.Rule());
                SaveCurrentTile();
            }
        }
    }

    private unsafe void DrawSelectedTilePreview(TileBase tile)
    {
        ImGui.Text(L10n.Tr("label_preview"));
        var drawList = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var size = new System.Numerics.Vector2(96, 96);
        drawList.AddRectFilled(start, start + size, ImGui.GetColorU32(new Vector4(0.1f, 0.1f, 0.1f, 1f)), 6f);
        drawList.AddRect(start, start + size, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.2f)), 6f);

        var preview = TryGetPreviewData(GetPreviewSprite(tile));
        if (preview.Texture is OpenGlTexture glTex)
        {
            ImGui.SetCursorScreenPos(start + new System.Numerics.Vector2(8, 8));
            ImGui.Image(new ImTextureRef(null, new ImTextureID((nint)glTex.Id)), new Vector2(80, 80), preview.UvMin, preview.UvMax);
        }
        else
        {
            drawList.AddRectFilled(start + new System.Numerics.Vector2(8, 8), start + new System.Numerics.Vector2(88, 88), ImGui.GetColorU32((Vector4)tile.Color), 4f);
        }

        ImGui.SetCursorScreenPos(start + new System.Numerics.Vector2(0, 100));
        ImGui.Dummy(new Vector2(96, 4));
    }

    private void DrawSpriteField(string label, Sprite current, Action<object?> onUpdate)
    {
        ImGui.PushID(label);
        ImGui.Text(label); ImGui.SameLine(100);
        string btnLabel = GetSpriteButtonLabel(current);
        if (ImGui.Button($"{btnLabel}##box", new System.Numerics.Vector2(-1, 0))) { ImGui.OpenPopup("SpritePicker"); }
        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("ASSET_PATH");
            if (payload.Handle != null && EditorSelection.DraggedAssetPath != null)
            {
                var ext = Path.GetExtension(EditorSelection.DraggedAssetPath).ToLowerInvariant();
                if (ext is ".png" or ".jpg" or ".jpeg")
                    onUpdate(EditorSelection.DraggedSpriteAsset ?? _app.CreateSpriteReference(EditorSelection.DraggedAssetPath));
            }
            ImGui.EndDragDropTarget();
        }
        
        if (ImGui.BeginPopup("SpritePicker"))
        {
            if (ImGui.MenuItem(L10n.Tr("msg_none"))) onUpdate(default(Sprite));
            if (_app.AssetsPath != null && _app.ProjectPath != null)
            {
                foreach (var f in Directory.GetFiles(_app.AssetsPath, "*.*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(f).ToLower();
                    if (ext is ".png" or ".jpg" or ".jpeg")
                    {
                        var rel = Path.GetRelativePath(_app.ProjectPath, f).Replace("\\", "/");
                        DrawSpritePickerEntry(f, rel, onUpdate);
                    }
                }
            }
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    private void RefreshAssetListIfNeeded()
    {
        if ((DateTime.Now - _lastRefresh).TotalSeconds < 2.0) return;
        
        if (_app.AssetsPath == null) return;

        _tileAssetPaths.Clear();
        _loadedTileCache.Clear();
        string[] extensions = { ".tile", ".animtile", ".ruletile" };
        
        foreach (var ext in extensions)
        {
            _tileAssetPaths.AddRange(Directory.GetFiles(_app.AssetsPath, "*" + ext, SearchOption.AllDirectories));
        }

        _lastRefresh = DateTime.Now;
    }

    private TileBase? GetLoadedTile(string path)
    {
        if (_loadedTileCache.TryGetValue(path, out var tile)) return tile;
        try
        {
            var loaded = TileAssetCache.Load(path, assetRootPath: _app.ProjectPath);
            if (loaded != null) _loadedTileCache[path] = loaded;
            return loaded;
        }
        catch { return null; }
    }

    public void InvalidateTileAsset(string path)
    {
        _loadedTileCache.Remove(path);
        TileAssetCache.Invalidate(path, _app.ProjectPath);
        _lastRefresh = DateTime.MinValue;

        if (string.Equals(EditorSelection.SelectedAssetPath, path, StringComparison.OrdinalIgnoreCase))
        {
            EditorSelection.SelectedTile = GetLoadedTile(path);
        }
    }

    public bool TrySelectTileAsset(TileBase? tile)
    {
        if (tile == null)
            return false;

        foreach (var path in _tileAssetPaths)
        {
            var loaded = GetLoadedTile(path);
            if (ReferenceEquals(loaded, tile))
            {
                SelectTileAsset(path, loaded);
                return true;
            }
        }

        string targetJson = SerializeTileForComparison(tile);
        foreach (var path in _tileAssetPaths)
        {
            var loaded = GetLoadedTile(path);
            if (loaded != null && SerializeTileForComparison(loaded) == targetJson)
            {
                SelectTileAsset(path, loaded);
                return true;
            }
        }

        EditorSelection.SelectedTile = tile;
        return false;
    }

    private static Sprite? GetPreviewSprite(TileBase tile)
    {
        return tile switch
        {
            Tile simpleTile => simpleTile.Sprite,
            AnimatedTile animatedTile => animatedTile.Sprites.FirstOrDefault(),
            RuleTile ruleTile => ruleTile.DefaultSprite ?? ruleTile.Rules.Select(r => r.Sprite).FirstOrDefault(sprite => sprite.HasValue),
            _ => null
        };
    }

    private (TextureObjectUploaded? Texture, Vector2 UvMin, Vector2 UvMax) TryGetPreviewData(Sprite? sprite)
    {
        if (!sprite.HasValue || string.IsNullOrWhiteSpace(sprite.Value.Path))
        {
            return (null, new Vector2(0, 1), new Vector2(1, 0));
        }

        string fullPath = Path.IsPathRooted(sprite.Value.Path)
            ? sprite.Value.Path
            : Path.Combine(_app.ProjectPath ?? "", sprite.Value.Path);

        if (!File.Exists(fullPath))
        {
            return (null, new Vector2(0, 1), new Vector2(1, 0));
        }

        try
        {
            var texture = _app.LoadSpriteTexture(sprite.Value);
            if (texture == null)
                return (null, new Vector2(0, 1), new Vector2(1, 0));

            var slice = _app.ResolveSpriteSlice(sprite.Value);
            Vector2 uvMin = new(slice.X / (float)Math.Max(1, texture.Width), 1f - (slice.Y / (float)Math.Max(1, texture.Height)));
            Vector2 uvMax = new((slice.X + slice.Width) / (float)Math.Max(1, texture.Width), 1f - ((slice.Y + slice.Height) / (float)Math.Max(1, texture.Height)));
            return (texture, uvMin, uvMax);
        }
        catch
        {
            return (null, new Vector2(0, 1), new Vector2(1, 0));
        }
    }

    private string GetSpriteButtonLabel(Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(sprite.Path))
            return L10n.Tr("msg_none");

        string fileName = Path.GetFileName(sprite.Path);
        if (string.IsNullOrWhiteSpace(sprite.SpriteId))
            return fileName;

        string fullPath = Path.IsPathRooted(sprite.Path) ? sprite.Path : Path.Combine(_app.ProjectPath ?? "", sprite.Path);
        var settings = _app.TryGetSpriteImportSettings(fullPath, false);
        string sliceName = settings?.Slices.FirstOrDefault(slice => string.Equals(slice.Id, sprite.SpriteId, StringComparison.OrdinalIgnoreCase))?.Name ?? sprite.SpriteId;
        return $"{fileName} / {sliceName}";
    }

    private void DrawSpritePickerEntry(string fullPath, string relativePath, Action<object?> onUpdate)
    {
        var settings = _app.TryGetSpriteImportSettings(fullPath);
        if (settings is { SpriteMode: SpriteImportMode.Multiple } && settings.Slices.Count > 0)
        {
            if (ImGui.BeginMenu(relativePath))
            {
                if (ImGui.MenuItem(L10n.Tr("menu_full_texture")))
                    onUpdate(_app.CreateSpriteReference(fullPath));

                foreach (var slice in settings.Slices)
                {
                    if (ImGui.MenuItem(slice.Name))
                        onUpdate(_app.CreateSpriteReference(fullPath, slice.Id));
                }

                ImGui.EndMenu();
            }
            return;
        }

        if (ImGui.MenuItem(relativePath))
            onUpdate(_app.CreateSpriteReference(fullPath));
    }

    public void DrawSelectedTileInspector()
    {
        if (EditorSelection.SelectedTile == null)
            return;

        DrawSelectedTilePreview(EditorSelection.SelectedTile);
        DrawTileProperties(EditorSelection.SelectedTile);
    }

    private void SyncSelectionFromCurrentTile()
    {
        if (EditorSelection.SelectedTile == null)
            return;

        if (!string.IsNullOrWhiteSpace(EditorSelection.SelectedAssetPath) &&
            _tileAssetPaths.Any(path => string.Equals(path, EditorSelection.SelectedAssetPath, StringComparison.OrdinalIgnoreCase)))
            return;

        TrySelectTileAsset(EditorSelection.SelectedTile);
    }

    private void SelectTileAsset(string path, TileBase? tile)
    {
        EditorSelection.SelectedAssetPath = path;
        EditorSelection.SelectedTile = tile ?? GetLoadedTile(path);
    }

    private string SerializeTileForComparison(TileBase tile)
    {
        return JsonSerializer.Serialize<TileBase>(tile, _tileOptions);
    }
}
