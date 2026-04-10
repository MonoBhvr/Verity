using System.Numerics;
using System.Reflection;
using Hexa.NET.ImGui;
using Verity.Core;
using Verity.Core.Animation;
using Verity.Core.ECS;
using Verity.Core.World;

namespace Verity.Editor.Windows;

public class AnimationWindow : EditorWindow
{
    private readonly EditorApp _app;
    private string _lastSavedControllerJson = string.Empty;

    private enum AnimatorParameterKind
    {
        None,
        Float,
        Int,
        Bool,
        Trigger
    }
    
    // State
    private Entity? _boundEntity;
    private Animator? _currentAnimator;
    private AnimatorState? _currentState;
    private AnimationClip? _currentClip;
    private float _currentTime = 0.0f;
    private bool _isPlaying = false;
    public bool IsRecording { get; private set; } = false;
    
    private float _pixelsPerSecond = 100.0f;
    private float _leftPanelWidth = 250.0f;
    
    // Selection
    private Keyframe? _selectedKeyframe;
    private AnimationTrack? _selectedTrack;

    public AnimationWindow(EditorApp app) : base(L10n.Tr("window_animation")) 
    { 
        _app = app; 
    }

    public override void RefreshTitle() { Title = L10n.Tr("window_animation"); }

    public override void OnGui()
    {
        var entity = EditorSelection.SelectedEntity;
        if (entity == null)
        {
            ImGui.Text(L10n.Tr("msg_select_entity_animation"));
            return;
        }

        if (_boundEntity != entity)
            BindToEntity(entity);

        _currentAnimator = entity.GetComponent<Animator>();
        if (_currentAnimator == null)
        {
            if (ImGui.Button(L10n.Tr("btn_create_animator")))
            {
                entity.AddComponent<Animator>();
            }
            return;
        }

        if (_currentAnimator.Controller == null)
        {
            ImGui.Text(L10n.Tr("msg_no_controller"));
            if (ImGui.Button(L10n.Tr("btn_create_controller")))
            {
                CreateController(entity);
            }
            return;
        }

        SyncSelection();
        if (_currentClip == null) return;

        DrawToolbar();

        ImGui.Separator();
        
        DrawTimeline();
        
        // Update Preview
        if (_isPlaying)
        {
            _currentTime += ImGui.GetIO().DeltaTime;
            if (_currentClip.Duration > 0f && _currentTime > _currentClip.Duration)
            {
                if (_currentClip.Loop) _currentTime %= _currentClip.Duration;
                else { _currentTime = _currentClip.Duration; _isPlaying = false; }
            }
            SampleCurrentClip();
        }

        AutoSaveControllerAssetIfChanged();
    }

    private void DrawToolbar()
    {
        // Record Button
        if (IsRecording) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1, 0, 0, 1));
        if (ImGui.Button(L10n.Tr("anim_record_short"), new Vector2(40, 25))) IsRecording = !IsRecording;
        if (IsRecording) ImGui.PopStyleColor();

        ImGui.SameLine();
        
        // Play Button
        if (ImGui.Button(_isPlaying ? "||" : ">", new Vector2(30, 25))) _isPlaying = !_isPlaying;
        
        ImGui.SameLine();
        
        // Time Display
        float displayFrameRate = _currentClip?.FrameRate ?? 60f;
        ImGui.Text(L10n.Tr("anim_time_display", MathF.Floor(_currentTime * displayFrameRate), _currentTime.ToString("F2")));

        if (_currentAnimator?.Controller == null || _currentClip == null)
            return;

        ImGui.SameLine();
        ImGui.SetNextItemWidth(170);
        if (ImGui.BeginCombo("##AnimState", _currentState?.Name ?? L10n.Tr("msg_none")))
        {
            foreach (var state in _currentAnimator.Controller.States)
            {
                if (ImGui.Selectable(state.Name, state == _currentState))
                {
                    SelectState(state);
                }
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("anim_btn_add_state"), new Vector2(70, 25)))
            CreateState();

        ImGui.SameLine();
        ImGui.SetNextItemWidth(140);
        string defaultStateName = _currentAnimator.Controller.DefaultState?.Name ?? L10n.Tr("msg_none");
        if (ImGui.BeginCombo(L10n.Tr("anim_default_state"), defaultStateName))
        {
            foreach (var state in _currentAnimator.Controller.States)
            {
                bool selected = _currentAnimator.Controller.DefaultStateName == state.Name;
                if (ImGui.Selectable(state.Name, selected))
                {
                    _currentAnimator.Controller.DefaultStateName = state.Name;
                    _app.MarkAsDirty();
                }
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        float frameRate = _currentClip.FrameRate;
        if (ImGui.DragFloat(L10n.Tr("anim_fps"), ref frameRate, 1f, 1f, 240f))
        {
            _currentClip.FrameRate = Math.Clamp(frameRate, 1f, 240f);
            _app.MarkAsDirty();
        }

        ImGui.SameLine();
        bool loop = _currentClip.Loop;
        if (ImGui.Checkbox(L10n.Tr("anim_loop"), ref loop))
        {
            _currentClip.Loop = loop;
            _app.MarkAsDirty();
        }

        ImGui.SameLine();
        string controllerPath = string.IsNullOrWhiteSpace(_currentAnimator.ControllerPath) ? L10n.Tr("anim_unsaved") : _currentAnimator.ControllerPath;
        ImGui.TextDisabled(controllerPath);

        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("btn_save"), new Vector2(55, 25)))
            SaveControllerAsset(force: true);
    }

    private unsafe void DrawTimeline()
    {
        float availH = ImGui.GetContentRegionAvail().Y;
        
        ImGui.Columns(2, "AnimColumns", true);
        ImGui.SetColumnWidth(0, _leftPanelWidth);
        
        // Left Panel: Tracks
        ImGui.BeginChild("TrackList", new Vector2(0, availH - 20));
        if (ImGui.Button(L10n.Tr("btn_add_property"))) ImGui.OpenPopup("AddPropPopup");
        
        if (ImGui.BeginPopup("AddPropPopup"))
        {
            DrawAddPropertyPopup();
            ImGui.EndPopup();
        }

        foreach (var track in _currentClip!.Tracks)
        {
            ImGui.Selectable(track.Path, _selectedTrack == track);
            if (ImGui.IsItemClicked()) _selectedTrack = track;
        }
        ImGui.EndChild();
        
        ImGui.NextColumn();
        
        // Right Panel: Dope Sheet / Timeline
        ImGui.BeginChild("TimelineArea", new Vector2(0, availH - 20), (ImGuiChildFlags)0, ImGuiWindowFlags.HorizontalScrollbar);
        
        var drawList = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var scrollX = ImGui.GetScrollX();
        float contentOriginX = p.X - scrollX;
        
        // Draw Ruler
        float duration = Math.Max(_currentClip.Duration, 1.0f);
        float width = duration * _pixelsPerSecond + 160;
        float rowHeight = 24f;
        int secondCount = Math.Max(1, (int)MathF.Ceiling(duration));

        for (int second = 0; second <= secondCount; second++)
        {
            float x = contentOriginX + second * _pixelsPerSecond;
            drawList.AddLine(new Vector2(x, p.Y), new Vector2(x, p.Y + availH), 0x22FFFFFF, 1.0f);
            drawList.AddText(new Vector2(x + 4, p.Y + 2), 0xFFBBBBBB, L10n.Tr("anim_second_mark", second));
        }
        
        // Draw Time Scrubber
        float scrubberX = contentOriginX + _currentTime * _pixelsPerSecond;
        drawList.AddLine(new Vector2(scrubberX, p.Y), new Vector2(scrubberX, p.Y + availH), 0xFFFF0000, 2.0f);
        
        // Handle input for scrubbing
        if (ImGui.IsWindowHovered() && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            float mouseX = ImGui.GetMousePos().X;
            float t = (mouseX - contentOriginX) / _pixelsPerSecond;
            _currentTime = Math.Max(0, t);
            SampleCurrentClip();
        }

        // Draw Keys
        float y = p.Y + 26;
        foreach (var track in _currentClip.Tracks)
        {
            drawList.AddLine(new Vector2(contentOriginX, y + rowHeight), new Vector2(contentOriginX + width, y + rowHeight), 0x22111111, 1.0f);
            foreach (var kf in track.Keyframes)
            {
                float x = contentOriginX + kf.Time * _pixelsPerSecond;
                Vector2 center = new Vector2(x, y + 10); // Center of track row
                
                // Diamond shape
                drawList.AddQuadFilled(
                    new Vector2(center.X, center.Y - 4),
                    new Vector2(center.X + 4, center.Y),
                    new Vector2(center.X, center.Y + 4),
                    new Vector2(center.X - 4, center.Y),
                    _selectedKeyframe == kf ? 0xFF00FF00 : 0xFFAAAAAA
                );
                
                // Click keyframe
                // Very basic hit test
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    Vector2 mouse = ImGui.GetMousePos();
                    if (Math.Abs(mouse.X - center.X) < 5 && Math.Abs(mouse.Y - center.Y) < 10)
                    {
                        _selectedKeyframe = kf;
                        _selectedTrack = track;
                    }
                }
            }
            y += rowHeight;
        }
        
        // Drag & Drop Handling
        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("ASSET_PATH");
            if (payload.Handle != null && EditorSelection.DraggedAssetPath != null)
            {
                string path = EditorSelection.DraggedAssetPath;
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext is ".png" or ".jpg" or ".jpeg")
                {
                    // Calculate drop time
                    float mouseX = ImGui.GetMousePos().X;
                    float t = Math.Max(0, (mouseX - contentOriginX) / _pixelsPerSecond);
                    
                    AddSpriteKeyframe(EditorSelection.DraggedSpriteAsset ?? CreateSpriteReference(path), t);
                }
            }
            ImGui.EndDragDropTarget();
        }
        
        ImGui.Dummy(new Vector2(width, availH));
        ImGui.EndChild();
        
        ImGui.Columns(1);
        
        DrawSelectedKeyframeInspector();
        DrawStateInspector();
    }

    private void DrawAddPropertyPopup()
    {
        var entity = EditorSelection.SelectedEntity;
        if (entity == null) return;

        foreach (var comp in entity.GetAllComponents())
        {
            if (comp is Animator) continue;
            if (!ImGui.BeginMenu(comp.GetType().Name)) continue;

            var type = comp.GetType();
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.CanWrite &&
                    prop.CanRead &&
                    prop.GetIndexParameters().Length == 0 &&
                    AnimationTypeUtility.IsAnimatable(prop.PropertyType) &&
                    ImGui.MenuItem(prop.Name))
                {
                    AddTrack(comp.GetType().Name, prop.Name, prop.PropertyType);
                }
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (AnimationTypeUtility.IsAnimatable(field.FieldType) && ImGui.MenuItem(field.Name))
                {
                    AddTrack(comp.GetType().Name, field.Name, field.FieldType);
                }
            }

            ImGui.EndMenu();
        }
    }

    private void AddTrack(string compName, string propName, Type type)
    {
        if (_currentClip == null || !AnimationTypeUtility.IsAnimatable(type)) return;

        string path = $"{compName}.{propName}";
        if (_currentClip.Tracks.Any(t => t.Path == path)) return;
        
        _currentClip.Tracks.Add(new AnimationTrack
        {
            Path = path,
            TypeName = AnimationTypeUtility.GetTypeName(type)
        });

        _app.MarkAsDirty();
    }

    private void CreateController(Entity entity)
    {
        _currentAnimator = entity.GetComponent<Animator>();
        if (_currentAnimator == null) return;

        var controller = new AnimatorController();
        string idleName = L10n.Tr("anim_default_state_name");
        var state = new AnimatorState { Name = idleName, Clip = new AnimationClip { Name = idleName } };
        controller.AddState(state);

        _currentAnimator!.Controller = controller;
        SelectState(state);
        _app.MarkAsDirty();
        SaveControllerAsset(force: true);
    }

    private void CreateState()
    {
        if (_currentAnimator?.Controller == null) return;

        string stateName = L10n.Tr("anim_state_n", _currentAnimator.Controller.States.Count + 1);
        var clip = new AnimationClip { Name = stateName };
        var state = new AnimatorState { Name = stateName, Clip = clip };
        _currentAnimator!.Controller!.AddState(state);
        SelectState(state);
        _app.MarkAsDirty();
    }

    public void RecordKeyframe(Entity entity, Type componentType, string propertyName, object value)
    {
        if (_currentClip == null || entity != _currentAnimator?.Owner) return;

        string path = $"{componentType.Name}.{propertyName}";
        var track = _currentClip.Tracks.FirstOrDefault(t => t.Path == path);
        
        if (track == null)
        {
            AddTrack(componentType.Name, propertyName, value.GetType());
            track = _currentClip.Tracks.FirstOrDefault(t => t.Path == path);
        }

        if (track == null) return;

        // Find keyframe at current time (approx)
        var kf = track.Keyframes.FirstOrDefault(k => Math.Abs(k.Time - _currentTime) < 0.01f);
        object storedValue = PrepareValueForRecording(value);
        if (kf != null)
        {
            kf.Value = storedValue;
        }
        else
        {
            track.Keyframes.Add(new Keyframe(_currentTime, storedValue));
            track.SortKeyframes();
        }
        _currentClip.RecalculateDuration();
        _app.MarkAsDirty();
    }

    private void AddSpriteKeyframe(Sprite sprite, float time)
    {
        if (_currentAnimator?.Owner.GetComponent<Verity.Graphics.SpriteRenderer>() == null)
        {
            _app.ShowOverlayMessage(L10n.Tr("msg_no_sprite_renderer"));
            return;
        }

        const string trackPath = "SpriteRenderer.Sprite";
        var track = _currentClip!.Tracks.FirstOrDefault(tr => tr.Path == trackPath);
        if (track == null)
        {
            AddTrack("SpriteRenderer", "Sprite", typeof(Sprite));
            track = _currentClip.Tracks.FirstOrDefault(tr => tr.Path == trackPath);
        }

        if (track == null) return;

        track.Keyframes.RemoveAll(k => Math.Abs(k.Time - time) < 0.01f);
        track.Keyframes.Add(new Keyframe(time, sprite));
        track.SortKeyframes();
        _currentClip.RecalculateDuration();
        _app.MarkAsDirty();
        SampleCurrentClip();
    }

    private void DrawSelectedKeyframeInspector()
    {
        if (_selectedKeyframe == null || _selectedTrack == null || _currentClip == null)
            return;

        ImGui.Separator();
        ImGui.Text(L10n.Tr("anim_keyframe_track", _selectedTrack.Path));

        float time = _selectedKeyframe.Time;
        if (ImGui.DragFloat(L10n.Tr("anim_time"), ref time, 0.01f))
        {
            _selectedKeyframe.Time = Math.Max(0f, time);
            _selectedTrack.SortKeyframes();
            _currentClip.RecalculateDuration();
            _app.MarkAsDirty();
            SampleCurrentClip();
        }

        Type? valueType = AnimationTypeUtility.ResolveType(_selectedTrack.TypeName) ?? _selectedKeyframe.Value?.GetType();
        if (valueType != null && DrawKeyframeValueEditor(L10n.Tr("anim_value"), valueType, _selectedKeyframe.Value, out object? updatedValue))
        {
            _selectedKeyframe.Value = PrepareValueForRecording(updatedValue!);
            _app.MarkAsDirty();
            SampleCurrentClip();
        }

        if (ImGui.Button(L10n.Tr("anim_delete_keyframe"), new Vector2(-1, 0)))
        {
            _selectedTrack.Keyframes.Remove(_selectedKeyframe);
            _currentClip.RecalculateDuration();
            _selectedKeyframe = null;
            _app.MarkAsDirty();
            SampleCurrentClip();
        }
    }

    private void DrawStateInspector()
    {
        if (_currentAnimator?.Controller == null || _currentState == null)
            return;

        ImGui.Separator();
        ImGui.Text(L10n.Tr("anim_state"));

        string stateName = _currentState.Name;
        if (ImGui.InputText(L10n.Tr("anim_state_name"), ref stateName, 128))
            RenameState(_currentState, stateName);

        bool canDeleteState = _currentAnimator.Controller.States.Count > 1;
        if (!canDeleteState)
            ImGui.BeginDisabled();
        if (ImGui.Button(L10n.Tr("anim_delete_state"), new Vector2(-1, 0)))
            DeleteState(_currentState);
        if (!canDeleteState)
            ImGui.EndDisabled();

        DrawParametersInspector();
        DrawTransitionsInspector();
    }

    private void DrawParametersInspector()
    {
        if (_currentAnimator?.Controller == null)
            return;

        ImGui.Separator();
        ImGui.Text(L10n.Tr("anim_parameters"));

        float buttonWidth = (ImGui.GetContentRegionAvail().X - 12f) * 0.25f;
        if (ImGui.Button(L10n.Tr("anim_add_float"), new Vector2(buttonWidth, 0)))
            AddParameter(_currentAnimator.Controller.FloatParameters, L10n.Tr("anim_param_float_prefix"), 0f);
        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("anim_add_int"), new Vector2(buttonWidth, 0)))
            AddParameter(_currentAnimator.Controller.IntParameters, L10n.Tr("anim_param_int_prefix"), 0);
        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("anim_add_bool"), new Vector2(buttonWidth, 0)))
            AddParameter(_currentAnimator.Controller.BoolParameters, L10n.Tr("anim_param_bool_prefix"), false);
        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("anim_add_trigger"), new Vector2(buttonWidth, 0)))
            AddParameter(_currentAnimator.Controller.TriggerParameters, L10n.Tr("anim_param_trigger_prefix"), false);

        DrawFloatParameters();
        DrawIntParameters();
        DrawBoolParameters(L10n.Tr("anim_param_bool"), _currentAnimator.Controller.BoolParameters);
        DrawBoolParameters(L10n.Tr("anim_param_trigger"), _currentAnimator.Controller.TriggerParameters);
    }

    private void DrawTransitionsInspector()
    {
        if (_currentAnimator?.Controller == null || _currentState == null)
            return;

        ImGui.Separator();
        ImGui.Text(L10n.Tr("anim_transitions"));

        if (ImGui.Button(L10n.Tr("anim_add_transition"), new Vector2(-1, 0)))
        {
            string targetState = _currentAnimator.Controller.States.FirstOrDefault(state => state != _currentState)?.Name ?? _currentState.Name;
            _currentState.Transitions.Add(new AnimatorTransition { ToState = targetState });
            _app.MarkAsDirty();
        }

        if (_currentState.Transitions.Count == 0)
        {
            ImGui.TextDisabled(L10n.Tr("anim_no_transitions"));
            return;
        }

        for (int i = 0; i < _currentState.Transitions.Count; i++)
        {
            var transition = _currentState.Transitions[i];
            ImGui.PushID(i);
            ImGui.Separator();
            ImGui.Text(L10n.Tr("anim_transition_n", i + 1));

            if (ImGui.BeginCombo(L10n.Tr("anim_to_state"), string.IsNullOrWhiteSpace(transition.ToState) ? L10n.Tr("msg_none") : transition.ToState))
            {
                foreach (var state in _currentAnimator.Controller.States)
                {
                    if (ImGui.Selectable(state.Name, state.Name == transition.ToState))
                    {
                        transition.ToState = state.Name;
                        _app.MarkAsDirty();
                    }
                }
                ImGui.EndCombo();
            }

            bool hasExitTime = transition.HasExitTime;
            if (ImGui.Checkbox(L10n.Tr("anim_has_exit_time"), ref hasExitTime))
            {
                transition.HasExitTime = hasExitTime;
                _app.MarkAsDirty();
            }

            if (transition.HasExitTime)
            {
                float exitTime = transition.ExitTime;
                if (ImGui.DragFloat(L10n.Tr("anim_exit_time"), ref exitTime, 0.01f, 0f, 1f))
                {
                    transition.ExitTime = Math.Clamp(exitTime, 0f, 1f);
                    _app.MarkAsDirty();
                }
            }

            if (ImGui.Button(L10n.Tr("anim_add_condition"), new Vector2(-1, 0)))
            {
                string firstParameter = GetAllParameterNames().FirstOrDefault() ?? "";
                transition.Conditions.Add(new AnimatorCondition { Parameter = firstParameter, Mode = GetDefaultConditionMode(firstParameter) });
                _app.MarkAsDirty();
            }

            for (int conditionIndex = 0; conditionIndex < transition.Conditions.Count; conditionIndex++)
            {
                var condition = transition.Conditions[conditionIndex];
                DrawConditionEditor(transition, condition, conditionIndex);
            }

            if (ImGui.Button(L10n.Tr("anim_remove_transition"), new Vector2(-1, 0)))
            {
                _currentState.Transitions.RemoveAt(i);
                _app.MarkAsDirty();
                ImGui.PopID();
                break;
            }

            ImGui.PopID();
        }
    }

    private void DrawConditionEditor(AnimatorTransition transition, AnimatorCondition condition, int conditionIndex)
    {
        ImGui.PushID(conditionIndex);

        if (ImGui.BeginCombo(L10n.Tr("anim_parameter"), string.IsNullOrWhiteSpace(condition.Parameter) ? L10n.Tr("anim_select_parameter") : condition.Parameter))
        {
            foreach (var parameterName in GetAllParameterNames())
            {
                if (ImGui.Selectable(parameterName, parameterName == condition.Parameter))
                {
                    condition.Parameter = parameterName;
                    condition.Mode = GetDefaultConditionMode(parameterName);
                    if (GetParameterKind(parameterName) is AnimatorParameterKind.Bool or AnimatorParameterKind.Trigger)
                        condition.Threshold = 1f;
                    _app.MarkAsDirty();
                }
            }
            ImGui.EndCombo();
        }

        string modePreview = GetConditionModeLabel(condition.Mode);
        if (ImGui.BeginCombo(L10n.Tr("anim_mode"), modePreview))
        {
            foreach (var mode in GetAvailableModes(condition.Parameter))
            {
                if (ImGui.Selectable(GetConditionModeLabel(mode), mode == condition.Mode))
                {
                    condition.Mode = mode;
                    _app.MarkAsDirty();
                }
            }
            ImGui.EndCombo();
        }

        DrawConditionThresholdEditor(condition);

        if (ImGui.Button(L10n.Tr("anim_remove_condition"), new Vector2(-1, 0)))
        {
            transition.Conditions.RemoveAt(conditionIndex);
            _app.MarkAsDirty();
            ImGui.PopID();
            return;
        }

        ImGui.PopID();
    }

    private void DrawConditionThresholdEditor(AnimatorCondition condition)
    {
        switch (GetParameterKind(condition.Parameter))
        {
            case AnimatorParameterKind.Float:
            {
                float threshold = condition.Threshold;
                if (ImGui.DragFloat(L10n.Tr("anim_threshold"), ref threshold, 0.1f))
                {
                    condition.Threshold = threshold;
                    _app.MarkAsDirty();
                }
                break;
            }
            case AnimatorParameterKind.Int:
            {
                int threshold = (int)condition.Threshold;
                if (ImGui.DragInt(L10n.Tr("anim_threshold"), ref threshold))
                {
                    condition.Threshold = threshold;
                    _app.MarkAsDirty();
                }
                break;
            }
            case AnimatorParameterKind.Bool:
            case AnimatorParameterKind.Trigger:
            {
                bool expected = condition.Threshold != 0f;
                if (ImGui.Checkbox(L10n.Tr("anim_expected_true"), ref expected))
                {
                    condition.Threshold = expected ? 1f : 0f;
                    _app.MarkAsDirty();
                }
                break;
            }
        }
    }

    private void DrawFloatParameters()
    {
        if (_currentAnimator?.Controller == null)
            return;

        foreach (var pair in _currentAnimator.Controller.FloatParameters.ToList())
        {
            string name = pair.Key;
            float value = pair.Value;
            ImGui.PushID($"Float_{name}");
            if (DrawParameterNameEditor("Float", _currentAnimator.Controller.FloatParameters, name, out string currentName))
            {
                name = currentName;
                value = _currentAnimator.Controller.FloatParameters[name];
            }
            if (ImGui.DragFloat(L10n.Tr("anim_value"), ref value, 0.1f))
            {
                _currentAnimator.Controller.FloatParameters[name] = value;
                _currentAnimator.SetFloat(name, value);
                _app.MarkAsDirty();
            }
            if (ImGui.Button(L10n.Tr("btn_remove"), new Vector2(-1, 0)))
            {
                _currentAnimator.Controller.FloatParameters.Remove(name);
                _app.MarkAsDirty();
                ImGui.PopID();
                break;
            }
            ImGui.PopID();
        }
    }

    private void DrawIntParameters()
    {
        if (_currentAnimator?.Controller == null)
            return;

        foreach (var pair in _currentAnimator.Controller.IntParameters.ToList())
        {
            string name = pair.Key;
            int value = pair.Value;
            ImGui.PushID($"Int_{name}");
            if (DrawParameterNameEditor("Int", _currentAnimator.Controller.IntParameters, name, out string currentName))
            {
                name = currentName;
                value = _currentAnimator.Controller.IntParameters[name];
            }
            if (ImGui.DragInt(L10n.Tr("anim_value"), ref value))
            {
                _currentAnimator.Controller.IntParameters[name] = value;
                _currentAnimator.SetInt(name, value);
                _app.MarkAsDirty();
            }
            if (ImGui.Button(L10n.Tr("btn_remove"), new Vector2(-1, 0)))
            {
                _currentAnimator.Controller.IntParameters.Remove(name);
                _app.MarkAsDirty();
                ImGui.PopID();
                break;
            }
            ImGui.PopID();
        }
    }

    private void DrawBoolParameters(string label, Dictionary<string, bool> parameters)
    {
        if (_currentAnimator?.Controller == null)
            return;

        foreach (var pair in parameters.ToList())
        {
            string name = pair.Key;
            bool value = pair.Value;
            ImGui.PushID($"{label}_{name}");
            if (DrawParameterNameEditor(label, parameters, name, out string currentName))
            {
                name = currentName;
                value = parameters[name];
            }
            if (ImGui.Checkbox(L10n.Tr("anim_value"), ref value))
            {
                parameters[name] = value;
                if (ReferenceEquals(parameters, _currentAnimator.Controller.BoolParameters))
                    _currentAnimator.SetBool(name, value);
                else if (value)
                    _currentAnimator.SetTrigger(name);
                else
                    _currentAnimator.ResetTrigger(name);
                _app.MarkAsDirty();
            }
            if (ImGui.Button(L10n.Tr("btn_remove"), new Vector2(-1, 0)))
            {
                parameters.Remove(name);
                _app.MarkAsDirty();
                ImGui.PopID();
                break;
            }
            ImGui.PopID();
        }
    }

    private bool DrawParameterNameEditor<T>(string label, Dictionary<string, T> parameters, string currentName, out string updatedName)
    {
        updatedName = currentName;
        string buffer = currentName;
        if (!ImGui.InputText(L10n.Tr("anim_parameter_name", label), ref buffer, 128))
            return false;

        string trimmed = buffer.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed == currentName || parameters.ContainsKey(trimmed))
            return false;

        RenameParameter(parameters, currentName, trimmed);
        RenameParameterReferences(currentName, trimmed);
        updatedName = trimmed;
        _app.MarkAsDirty();
        return true;
    }

    private unsafe bool DrawKeyframeValueEditor(string label, Type valueType, object? rawValue, out object? updatedValue)
    {
        updatedValue = rawValue;
        valueType = Nullable.GetUnderlyingType(valueType) ?? valueType;

        if (valueType == typeof(float))
        {
            float value = rawValue is float f ? f : 0f;
            if (ImGui.DragFloat(label, ref value, 0.1f))
            {
                updatedValue = value;
                return true;
            }
            return false;
        }

        if (valueType == typeof(int))
        {
            int value = rawValue is int i ? i : 0;
            if (ImGui.DragInt(label, ref value))
            {
                updatedValue = value;
                return true;
            }
            return false;
        }

        if (valueType == typeof(bool))
        {
            bool value = rawValue is bool b && b;
            if (ImGui.Checkbox(label, ref value))
            {
                updatedValue = value;
                return true;
            }
            return false;
        }

        if (valueType == typeof(string))
        {
            string value = rawValue as string ?? string.Empty;
            if (ImGui.InputText(label, ref value, 256))
            {
                updatedValue = value;
                return true;
            }
            return false;
        }

        if (valueType == typeof(Verity.Core.Vector2))
        {
            Verity.Core.Vector2 value = rawValue is Verity.Core.Vector2 coreVec2 ? coreVec2 : Verity.Core.Vector2.Zero;
            if (DrawCoreVector2Editor(label, ref value))
            {
                updatedValue = value;
                return true;
            }
            return false;
        }

        if (valueType == typeof(System.Numerics.Vector2))
        {
            System.Numerics.Vector2 value = rawValue is System.Numerics.Vector2 sysVec2 ? sysVec2 : System.Numerics.Vector2.Zero;
            if (DrawSystemVector2Editor(label, ref value))
            {
                updatedValue = value;
                return true;
            }
            return false;
        }

        if (valueType == typeof(Verity.Core.Vector3))
        {
            Verity.Core.Vector3 value = rawValue is Verity.Core.Vector3 coreVec3 ? coreVec3 : Verity.Core.Vector3.Zero;
            if (DrawCoreVector3Editor(label, ref value))
            {
                updatedValue = value;
                return true;
            }
            return false;
        }

        if (valueType == typeof(System.Numerics.Vector3))
        {
            System.Numerics.Vector3 value = rawValue is System.Numerics.Vector3 sysVec3 ? sysVec3 : System.Numerics.Vector3.Zero;
            if (DrawSystemVector3Editor(label, ref value))
            {
                updatedValue = value;
                return true;
            }
            return false;
        }

        if (valueType == typeof(Vector4))
        {
            Vector4 value = rawValue is Vector4 vec4 ? vec4 : Vector4.Zero;
            if (ImGui.DragFloat4(label, ref value))
            {
                updatedValue = value;
                return true;
            }
            return false;
        }

        if (valueType == typeof(Color))
        {
            Color value = rawValue is Color color ? color : Color.White;
            Vector4 editValue = (Vector4)value;
            if (ImGui.ColorEdit4(label, ref editValue))
            {
                updatedValue = (Color)editValue;
                return true;
            }
            return false;
        }

        if (valueType == typeof(Sprite))
        {
            Sprite sprite = rawValue is Sprite currentSprite ? currentSprite : default;
            string path = sprite.Path ?? string.Empty;
            bool changed = false;
            if (ImGui.InputText(label, ref path, 260))
                changed = true;

            if (ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("ASSET_PATH");
                if (payload.Handle != null && EditorSelection.DraggedAssetPath != null)
                {
                    string ext = Path.GetExtension(EditorSelection.DraggedAssetPath).ToLowerInvariant();
                    if (ext is ".png" or ".jpg" or ".jpeg")
                    {
                        if (EditorSelection.DraggedSpriteAsset.HasValue)
                        {
                            updatedValue = EditorSelection.DraggedSpriteAsset.Value;
                            ImGui.EndDragDropTarget();
                            return true;
                        }

                        path = NormalizeAssetPath(EditorSelection.DraggedAssetPath);
                        changed = true;
                    }
                }
                ImGui.EndDragDropTarget();
            }

            if (changed)
            {
                updatedValue = CreateSpriteReference(path);
                return true;
            }
            return false;
        }

        if (valueType.IsEnum)
        {
            object currentValue = rawValue ?? Activator.CreateInstance(valueType)!;
            string[] names = Enum.GetNames(valueType);
            int selectedIndex = Array.IndexOf(names, currentValue.ToString());
            if (selectedIndex < 0) selectedIndex = 0;
            if (ImGui.Combo(label, ref selectedIndex, names, names.Length))
            {
                updatedValue = Enum.Parse(valueType, names[selectedIndex]);
                return true;
            }
            return false;
        }

        ImGui.TextDisabled($"{label}: {rawValue}");
        return false;
    }

    private static unsafe bool DrawCoreVector2Editor(string label, ref Verity.Core.Vector2 value)
    {
        System.Numerics.Vector2 editValue = value;
        if (!ImGui.DragFloat2(label, (float*)&editValue, 0.1f))
            return false;

        value = editValue;
        return true;
    }

    private static unsafe bool DrawSystemVector2Editor(string label, ref System.Numerics.Vector2 value)
    {
        System.Numerics.Vector2 editValue = value;
        if (!ImGui.DragFloat2(label, (float*)&editValue, 0.1f))
            return false;

        value = editValue;
        return true;
    }

    private static unsafe bool DrawCoreVector3Editor(string label, ref Verity.Core.Vector3 value)
    {
        System.Numerics.Vector3 editValue = value;
        if (!ImGui.DragFloat3(label, (float*)&editValue, 0.1f))
            return false;

        value = editValue;
        return true;
    }

    private static unsafe bool DrawSystemVector3Editor(string label, ref System.Numerics.Vector3 value)
    {
        System.Numerics.Vector3 editValue = value;
        if (!ImGui.DragFloat3(label, (float*)&editValue, 0.1f))
            return false;

        value = editValue;
        return true;
    }

    private void AddParameter<T>(Dictionary<string, T> parameters, string prefix, T defaultValue)
    {
        int index = 1;
        string name;
        do
        {
            name = $"{prefix}{index++}";
        }
        while (GetAllParameterNames().Contains(name));

        parameters[name] = defaultValue;
        _app.MarkAsDirty();
    }

    private IEnumerable<string> GetAllParameterNames()
    {
        if (_currentAnimator?.Controller == null)
            return Enumerable.Empty<string>();

        return _currentAnimator.Controller.FloatParameters.Keys
            .Concat(_currentAnimator.Controller.IntParameters.Keys)
            .Concat(_currentAnimator.Controller.BoolParameters.Keys)
            .Concat(_currentAnimator.Controller.TriggerParameters.Keys)
            .Distinct()
            .OrderBy(name => name)
            .ToArray();
    }

    private AnimatorParameterKind GetParameterKind(string parameterName)
    {
        if (_currentAnimator?.Controller == null || string.IsNullOrWhiteSpace(parameterName))
            return AnimatorParameterKind.None;

        if (_currentAnimator.Controller.FloatParameters.ContainsKey(parameterName))
            return AnimatorParameterKind.Float;
        if (_currentAnimator.Controller.IntParameters.ContainsKey(parameterName))
            return AnimatorParameterKind.Int;
        if (_currentAnimator.Controller.BoolParameters.ContainsKey(parameterName))
            return AnimatorParameterKind.Bool;
        if (_currentAnimator.Controller.TriggerParameters.ContainsKey(parameterName))
            return AnimatorParameterKind.Trigger;

        return AnimatorParameterKind.None;
    }

    private AnimatorConditionMode GetDefaultConditionMode(string parameterName)
    {
        return GetParameterKind(parameterName) switch
        {
            AnimatorParameterKind.Bool => AnimatorConditionMode.If,
            AnimatorParameterKind.Trigger => AnimatorConditionMode.If,
            _ => AnimatorConditionMode.Greater
        };
    }

    private IEnumerable<AnimatorConditionMode> GetAvailableModes(string parameterName)
    {
        return GetParameterKind(parameterName) switch
        {
            AnimatorParameterKind.Bool or AnimatorParameterKind.Trigger => new[]
            {
                AnimatorConditionMode.If,
                AnimatorConditionMode.IfNot,
                AnimatorConditionMode.Equals,
                AnimatorConditionMode.NotEqual
            },
            AnimatorParameterKind.Float or AnimatorParameterKind.Int => new[]
            {
                AnimatorConditionMode.Greater,
                AnimatorConditionMode.Less,
                AnimatorConditionMode.Equals,
                AnimatorConditionMode.NotEqual
            },
            _ => new[] { AnimatorConditionMode.If }
        };
    }

    private static string GetConditionModeLabel(AnimatorConditionMode mode)
    {
        string key = $"enum_{nameof(AnimatorConditionMode)}_{mode}";
        string localized = L10n.Tr(key);
        return localized == key ? mode.ToString() : localized;
    }

    private static void RenameParameter<T>(Dictionary<string, T> parameters, string oldName, string newName)
    {
        if (!parameters.TryGetValue(oldName, out T? value))
            return;

        parameters.Remove(oldName);
        parameters[newName] = value;
    }

    private void RenameParameterReferences(string oldName, string newName)
    {
        if (_currentAnimator?.Controller == null)
            return;

        foreach (var state in _currentAnimator.Controller.States)
        {
            foreach (var transition in state.Transitions)
            {
                foreach (var condition in transition.Conditions)
                {
                    if (condition.Parameter == oldName)
                        condition.Parameter = newName;
                }
            }
        }
    }

    private void RenameState(AnimatorState state, string proposedName)
    {
        if (_currentAnimator?.Controller == null)
            return;

        string trimmed = proposedName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed == state.Name)
            return;

        if (_currentAnimator.Controller.States.Any(existing => existing != state && existing.Name == trimmed))
            return;

        string previousName = state.Name;
        state.Name = trimmed;
        if (state.Clip != null && state.Clip.Name == previousName)
            state.Clip.Name = trimmed;

        if (_currentAnimator.Controller.DefaultStateName == previousName)
            _currentAnimator.Controller.DefaultStateName = trimmed;

        foreach (var existingState in _currentAnimator.Controller.States)
        {
            foreach (var transition in existingState.Transitions)
            {
                if (transition.ToState == previousName)
                    transition.ToState = trimmed;
            }
        }

        _app.MarkAsDirty();
    }

    private void DeleteState(AnimatorState state)
    {
        if (_currentAnimator?.Controller == null || _currentAnimator.Controller.States.Count <= 1)
            return;

        _currentAnimator.Controller.States.Remove(state);
        foreach (var existingState in _currentAnimator.Controller.States)
        {
            existingState.Transitions.RemoveAll(transition => transition.ToState == state.Name);
        }

        if (_currentAnimator.Controller.DefaultStateName == state.Name)
            _currentAnimator.Controller.DefaultStateName = _currentAnimator.Controller.States[0].Name;

        SelectState(_currentAnimator.Controller.DefaultState ?? _currentAnimator.Controller.States.FirstOrDefault());
        _app.MarkAsDirty();
    }

    private void BindToEntity(Entity entity)
    {
        _boundEntity = entity;
        _currentAnimator = entity.GetComponent<Animator>();
        _currentState = null;
        _currentClip = null;
        _currentTime = 0f;
        _isPlaying = false;
        _selectedKeyframe = null;
        _selectedTrack = null;
        _lastSavedControllerJson = string.Empty;
    }

    private void SyncSelection()
    {
        if (_currentAnimator?.Controller == null)
        {
            _currentState = null;
            _currentClip = null;
            return;
        }

        if (_currentState != null && _currentAnimator.Controller.States.Contains(_currentState) && _currentState.Clip != null)
        {
            _currentClip = _currentState.Clip;
            return;
        }

        SelectState(_currentAnimator.Controller.DefaultState ?? _currentAnimator.Controller.States.FirstOrDefault());
    }

    private void SelectState(AnimatorState? state)
    {
        _currentState = state;
        _currentClip = state?.Clip;
        _currentTime = 0f;
        _isPlaying = false;
        _selectedKeyframe = null;
        _selectedTrack = null;
        SampleCurrentClip();
    }

    private void SampleCurrentClip()
    {
        if (_currentAnimator != null && _currentClip != null)
            _currentAnimator.SampleClip(_currentClip, _currentTime);
    }

    private object PrepareValueForRecording(object value)
    {
        if (value is Sprite sprite)
            return new Sprite(NormalizeAssetPath(sprite.Path), sprite.Guid, sprite.SpriteId);

        return AnimationTypeUtility.CloneValue(value) ?? value;
    }

    private string NormalizeAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        if (_app.ProjectPath != null)
        {
            int assetsIndex = path.IndexOf("Assets", StringComparison.OrdinalIgnoreCase);
            if (assetsIndex >= 0)
                return path[assetsIndex..].Replace("\\", "/");

            if (Path.IsPathRooted(path) && path.StartsWith(_app.ProjectPath, StringComparison.OrdinalIgnoreCase))
                return Path.GetRelativePath(_app.ProjectPath, path).Replace("\\", "/");
        }

        return path.Replace("\\", "/");
    }

    private Sprite CreateSpriteReference(string path)
    {
        var sprite = _app.CreateSpriteReference(path);
        string resolvedPath = Path.IsPathRooted(sprite.Path)
            ? sprite.Path
            : (_app.ProjectPath == null ? sprite.Path : Path.Combine(_app.ProjectPath, sprite.Path));
        var settings = _app.TryGetSpriteImportSettings(resolvedPath);
        if (settings is { SpriteMode: SpriteImportMode.Multiple } && settings.Slices.Count > 0)
            return _app.CreateSpriteReference(path, settings.Slices[0].Id);

        return sprite;
    }

    private void AutoSaveControllerAssetIfChanged()
    {
        if (_isPlaying)
            return;

        SaveControllerAsset(force: false);
    }

    private void SaveControllerAsset(bool force)
    {
        if (_currentAnimator?.Controller == null || _app.ProjectPath == null || _app.AssetsPath == null)
            return;

        EnsureControllerAssetPath();
        if (string.IsNullOrWhiteSpace(_currentAnimator.ControllerPath))
            return;

        string json = AnimatorControllerAsset.ToJson(_currentAnimator.Controller);
        if (!force && json == _lastSavedControllerJson)
            return;

        string fullPath = Path.Combine(_app.ProjectPath, _currentAnimator.ControllerPath);
        if (AnimatorControllerAsset.SaveToFile(fullPath, _currentAnimator.Controller))
        {
            _currentAnimator.ControllerGuid = AssetPathUtility.EnsureMetaAndGetGuid(fullPath);
            _lastSavedControllerJson = json;
        }
    }

    private void EnsureControllerAssetPath()
    {
        if (_currentAnimator == null || !string.IsNullOrWhiteSpace(_currentAnimator.ControllerPath) || _app.AssetsPath == null || _app.ProjectPath == null)
            return;

        string animationsDirectory = Path.Combine(_app.AssetsPath, "Animations");
        Directory.CreateDirectory(animationsDirectory);

        string baseName = SanitizeFileName(_boundEntity?.Name ?? _currentState?.Name ?? "AnimatorController");
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "AnimatorController";

        string fullPath = Path.Combine(animationsDirectory, $"{baseName}.controller");
        int suffix = 1;
        while (File.Exists(fullPath))
        {
            fullPath = Path.Combine(animationsDirectory, $"{baseName}_{suffix++}.controller");
        }

        _currentAnimator.ControllerPath = Path.GetRelativePath(_app.ProjectPath, fullPath).Replace("\\", "/");
        _currentAnimator.ControllerGuid = AssetPathUtility.EnsureMetaAndGetGuid(fullPath);
        _app.MarkAsDirty();
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalid.Contains(ch) ? '_' : ch));
    }
}
