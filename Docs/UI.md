# Verity UI 문서

이 문서는 retained UI 시스템의 구조와 스크립팅 API를 설명합니다.

범위는 다음과 같습니다.

- UI 공통 enum과 데이터 타입
- `UiNode` 계층
- `Canvas`
- binding / layout / runtime system

---

## 1. UI 시스템 개요

Verity UI는 retained tree 기반입니다. 즉, 즉시 그릴 명령만 나열하는 구조가 아니라, 상태를 가진 UI 노드 트리를 유지하고 그 결과를 레이아웃/입력/렌더 단계가 읽습니다.

### 존재 이유

- 스크립트와 에디터가 같은 UI 구조를 공유할 수 있어야 해서
- 이벤트, binding, layout을 데이터 구조 중심으로 처리하기 위해

---

## 2. UI 공통 enum

### `UiStateFlags`

- `None`
- `Hover`
- `Pressed`
- `Disabled`
- `Selected`
- `Focused`
- `Expanded`
- `Checked`

### `UiRenderMode`

- `ScreenSpaceOverlay`
- `ScreenSpaceCamera`
- `WorldSpace`

### `UiNodeKind`

- `Container`
- `Panel`
- `Label`
- `RichText`
- `Image`
- `Button`
- `IconButton`
- `Toggle`
- `ToggleGroup`
- `Dropdown`
- `InputField`
- `TextArea`
- `Slider`
- `ProgressBar`
- `Scrollbar`
- `ScrollView`
- `ListView`
- `GridView`
- `Window`
- `Modal`
- `Tabs`
- `Tooltip`
- `Spacer`

### `UiLayoutMode`

- `None`
- `HorizontalStack`
- `VerticalStack`
- `Grid`
- `Wrap`
- `ScrollContent`

### `UiNavigationMode`

- `Automatic`
- `Explicit`

### `UiBindingMode`

- `OneWay`
- `TwoWay`

### `UiEventType`

- `PointerEnter`
- `PointerExit`
- `PointerDown`
- `PointerUp`
- `Click`
- `DoubleClick`
- `DragBegin`
- `Drag`
- `DragEnd`
- `Scroll`
- `ValueChanged`
- `Submit`
- `Cancel`
- `FocusChanged`

---

## 3. 공통 데이터 타입

## 3.1 `UiRect`

- 프로퍼티
  - `Vector2 Position`
  - `Vector2 Size`
  - `float Right`
  - `float Bottom`
- 메서드
  - `bool Contains(Vector2 point)`

## 3.2 `UiEvent`

- 프로퍼티
  - `UiEventType Type`
  - `UiNode? Node`
  - `Vector2 Position`
  - `Vector2 Delta`
  - `float ScrollDelta`
  - `object? Value`

## 3.3 `UiTransform`

- 프로퍼티
  - `Vector2 AnchorMin`
  - `Vector2 AnchorMax`
  - `Vector2 Pivot`
  - `Vector2 Position`
  - `Vector2 Size`
  - `Vector4 Margin`
  - `Vector2 MinSize`
  - `Vector2 MaxSize`
  - `float Rotation`
  - `float Scale`
  - `int ZOrder`

## 3.4 `UiVisualStyle`

- 프로퍼티
  - `Color BackgroundColor`
  - `Color ForegroundColor`
  - `Color BorderColor`
  - `Color HoverColor`
  - `Color PressedColor`
  - `Color DisabledColor`
  - `float BorderThickness`
  - `float CornerRadius`
  - `float FontSize`
  - `Vector4 Padding`
  - `string FontPath`
  - `string FontFamily`
  - `string BackgroundToken`
  - `string ForegroundToken`

## 3.5 `UiLayoutGroup`

- 프로퍼티
  - `UiLayoutMode Mode`
  - `int Columns`
  - `Vector2 Spacing`
  - `Vector4 Padding`
  - `bool FitChildren`

## 3.6 `UiNavigation`

- 프로퍼티
  - `UiNavigationMode Mode`
  - `string Up`
  - `string Down`
  - `string Left`
  - `string Right`

## 3.7 `UiAnimationState`

- 프로퍼티
  - `string Name`
  - `float Duration`

## 3.8 `UiBinding`

- 프로퍼티
  - `string Path`
  - `string TargetProperty`
  - `UiBindingMode Mode`

## 3.9 `UiEventAction`

- 프로퍼티
  - `UiEventType Trigger`
  - `string Target`
  - `string Method`

---

## 4. `UiNode`와 파생 타입

`UiNode`는 모든 UI 요소의 베이스 타입입니다.

### 존재 이유

- 모든 UI 요소가 공통 상태, 스타일, 자식 관계, 이벤트 표면을 공유해야 하기 때문입니다.

### `UiNode` 프로퍼티

- `string Id`
- `string Name`
- `string Tag`
- `bool Active`
- `UiNodeKind Kind`
- `UiTransform Transform`
- `UiVisualStyle Visual`
- `UiNavigation Navigation`
- `UiAnimationState Animation`
- `List<UiBinding> Bindings`
- `List<UiEventAction> Events`
- `List<UiNode> Children`
- `bool Interactable`
- `bool Visible`
- `UiNode? Parent`
- `UiRect LayoutRect`
- `UiStateFlags RuntimeState`

### `UiNode` 이벤트

- `event Action<UiEvent>? OnEvent`
- `event Action<UiEvent>? OnClick`
- `event Action<UiEvent>? OnValueChanged`
- `event Action<UiEvent>? OnSubmit`

### `UiNode` 메서드

- `void AddChild(UiNode child)`
- `void RemoveChild(UiNode child)`
- `IEnumerable<UiNode> DescendantsAndSelf()`
- `T? Query<T>(string nameOrId) where T : UiNode`
- `UiNode? Query(string nameOrId)`
- `void RebindTree()`

### 주요 파생 타입

| 타입 | 추가 목적 |
| :--- | :--- |
| `UiContainer` | 자식 배치와 clip 제어 |
| `Panel` | 일반 패널 |
| `TextNode` | 텍스트 공통 베이스 |
| `Label` | 일반 텍스트 |
| `RichText` | 서식 텍스트 |
| `VisualNode` | sprite 시각 요소 |
| `Image` | 이미지 노드 |
| `ClickableNode` | 클릭 가능한 컨테이너 |
| `Button` | 버튼 |
| `IconButton` | 아이콘 버튼 |
| `Toggle` | 토글 |
| `ToggleGroup` | 토글 그룹 |
| `Dropdown` | 드롭다운 |
| `InputField` | 단일행 입력 |
| `TextArea` | 다중행 입력 |
| `Slider` | 슬라이더 |
| `ProgressBar` | 진행률 표시 |
| `Scrollbar` | 스크롤바 |
| `ScrollView` | 스크롤 가능한 컨테이너 |
| `ListView` | 리스트 뷰 |
| `GridView` | 그리드 뷰 |
| `Window` | 윈도우 |
| `Modal` | 모달 |
| `Tabs` | 탭 컨테이너 |
| `Tooltip` | 툴팁 |
| `Spacer` | 레이아웃 간격용 노드 |

파생 타입별 세부 프로퍼티는 코드상 public 멤버 기준으로 존재합니다. 예를 들어:

- `Button.Text`
- `Toggle.IsChecked`, `Toggle.Group`
- `Dropdown.Options`, `Dropdown.SelectedIndex`, `Dropdown.Expanded`
- `InputField.Value`, `InputField.Placeholder`
- `Slider.Min`, `Slider.Max`, `Slider.Value`
- `ScrollView.ScrollOffset`, `ScrollView.Horizontal`, `ScrollView.Vertical`

---

## 5. UI 에셋 타입

### `UiStateStyleOverride`

- `UiStateFlags State`
- `UiVisualStyle Visual`

### `UiStyleAsset`

- `string Name`
- `Dictionary<string, Color> Colors`
- `Dictionary<string, float> Numbers`
- `Dictionary<string, string> Strings`
- `List<UiStateStyleOverride> States`

### `UiPrefabAsset`

- `string Name`
- `UiNode Root`

### `UIScreenAsset`

- `string Id`
- `string Name`
- `UiRenderMode RenderMode`
- `Vector2 ReferenceResolution`
- `float MatchWidthOrHeight`
- `int SortingOrder`
- `UiNode Root`
- `void RebindTree()`

### 존재 이유

- 화면 전체, 재사용 조각, 스타일 데이터를 분리 저장하기 위해

---

## 6. `Canvas`

`Canvas`는 활성 UI 화면 인스턴스입니다.

### 프로퍼티

- `Entity? OwnerEntity`
- `UIScreenAsset Screen`
- `bool Visible`

### 메서드

- `T? Query<T>(string nameOrId) where T : UiNode`
- `UiNode? Query(string nameOrId)`
- `void Bind(string path, object source)`
- `void Update(float viewportWidth, float viewportHeight)`
- `UiNode? HitTest(Vector2 point)`

### 존재 이유

- 같은 `UIScreenAsset`을 여러 번 띄우더라도 각 인스턴스가 독립 상태를 가져야 하기 때문입니다.

---

## 7. 런타임 보조 시스템

## 7.1 `UiNodeFactory`

- `UiNode Create(UiNodeKind kind)`

존재 이유:

- 에디터와 런타임이 kind 기준으로 표준 노드 인스턴스를 만들 수 있어야 하기 때문입니다.

## 7.2 `UiBindingRuntime`

- `object? ResolvePath(object? source, string memberPath)`
- `bool TrySetValue(object target, string propertyName, object? value)`
- `bool TrySetPath(object? source, string memberPath, object? value)`

존재 이유:

- UI 바인딩을 reflection 기반 공통 경로로 처리하기 위해

## 7.3 `UiLayoutEngine`

- `void Layout(UIScreenAsset screen, float viewportWidth, float viewportHeight)`

존재 이유:

- retained tree의 실제 사각형 계산을 한 곳에서 수행하기 위해

## 7.4 `UiSystem`

프로퍼티:

- `string? AssetsRoot`
- `Vector2 PointerPosition`
- `IReadOnlyList<Canvas> ActiveCanvases`

메서드:

- `UIScreenAsset Load(string path)`
- `string ResolveAssetPath(string path, string? guid = null)`
- `UIScreenAsset LoadAsset(string path, string? guid = null)`
- `Canvas ShowScreen(UIScreenAsset screen, Entity? ownerEntity = null)`
- `void HideScreen(string id)`
- `void HideCanvas(Canvas canvas)`
- `Canvas? FindCanvas(string screenNameOrId)`
- `T? Query<T>(string nameOrId) where T : UiNode`
- `UiNode? Query(string nameOrId)`
- `void Bind(string path, object source)`
- `void Unbind(string path)`
- `bool TryResolveBindingSource(string key, out object source)`
- `void Update(float viewportWidth, float viewportHeight)`
- `void Clear()`

### 존재 이유

- 활성 캔버스 목록, 전역 binding source, 화면 표시/숨김을 중앙에서 관리하기 위해

---

## 8. 현재 주의점

- UI binding과 action 호출은 reflection 기반이므로 빈번한 대규모 갱신에서 비용이 있습니다.
- retained UI이므로 상태 변경과 레이아웃 변경이 누적되면 트리 순회 비용이 커질 수 있습니다.

