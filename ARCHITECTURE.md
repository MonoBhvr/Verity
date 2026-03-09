# Verity Engine Architecture


# 주의 현재 이 문서는 최신 사항을 반영하고 있지 않습니다.

## Overview

Verity는 .NET 9.0 (C#) 기반 2D 게임 엔진으로, 통합 에디터를 포함합니다.
렌더링은 irodori 그래픽스 라이브러리(OpenGL 백엔드)를 사용하며, 에디터 UI는 ImGui(Hexa.NET.ImGui)로 구현됩니다.
단일 SDL2 윈도우에서 게임 씬을 FBO에 렌더링한 뒤 ImGui::Image()로 Scene View에 표시하는 구조입니다.

---

## Solution Structure

```
Verity.sln
├── Engine/
│   ├── Verity.Core/        # 순수 C# 엔진 코어 (의존성 없음)
│   ├── Verity.Graphics/    # 렌더링 (irodori, SDL2, StbImageSharp)
│   └── Verity.Input/       # 입력 시스템 (SDL2)
├── Editor/
│   ├── Verity.Editor/      # 에디터 프레임워크 (Hexa.NET.ImGui)
│   └── Verity.Editor.App/  # 에디터 진입점
└── Verity.Game/            # 게임 프로젝트 (사용자 코드)
```

### Dependency Graph

```
Verity.Core  ← (no deps)
     ↑
Verity.Graphics  ← irodori, irodori.Backend.OpenGL, ppy.SDL2-CS, StbImageSharp
     ↑
Verity.Input  ← ppy.SDL2-CS
     ↑
Verity.Editor  ← Hexa.NET.ImGui, Hexa.NET.ImGui.Backends.SDL2/OpenGL3
     ↑
Verity.Editor.App  (entry point)
```

`InternalsVisibleTo`: Verity.Core → Verity.Editor

---

## Verity.Core — 엔진 코어

### ECS (Entity Component System)

경량 컴포넌트 기반 아키텍처. Unity 스타일의 GameObject-Component 모델.

| 클래스 | 역할 |
|--------|------|
| `Component` | 모든 컴포넌트의 추상 베이스. `Owner`, `Enabled`, `OnEnable/OnDisable/OnDestroy` |
| `Transform` | Position(Vector2), Rotation(float, degrees), Scale(Vector2). 부모-자식 계층. `GetWorldMatrix()`, `WorldPosition` |
| `Script` | 게임 로직 컴포넌트. `Awake → Start → Update/FixedUpdate/LateUpdate → OnDestroy` 라이프사이클 |
| `GameObject` | 컴포넌트 컨테이너. `AddComponent<T>()`, `GetComponent<T>()`, `GetComponents<T>()`, `RemoveComponent<T>()`. Transform은 항상 존재 (제거 불가) |
| `SerializeFieldAttribute` | 프로퍼티/필드를 Inspector에 노출하는 어트리뷰트 |
| `HideInInspectorAttribute` | public 프로퍼티/필드를 Inspector에서 숨기는 어트리뷰트 |

### Scene

| 클래스 | 역할 |
|--------|------|
| `Scene` | GameObject 컨테이너. `CreateGameObject()`, `DestroyGameObject()`, `ProcessPendingDestroys()` |
| `SceneManager` | static. Scene 생성/활성화/언로드. `ActiveScene` |

### Engine

| 클래스 | 역할 |
|--------|------|
| `Time` | static 시간 제공자. `DeltaTime`, `FixedDeltaTime`, `TotalTime`, `TimeScale`, `FrameCount` |
| `GameLoop` | 프레임 루프. `TickLogic(deltaTime)` = Script 라이프사이클 실행, `TickRender()` = 렌더 콜백. FixedUpdate는 고정 타임스텝 누적기 사용 |

### VerityCore

- `Version`: "0.0.1"
- `ResetRuntime()`: SceneManager + Time 리셋

---

## Verity.Graphics — 렌더링

### 핵심 클래스

| 클래스 | 역할 |
|--------|------|
| `GraphicsDevice` | SDL2 윈도우 + irodori Gfx<OpenGlBackend, VeritySdl2Window> 래퍼. `Clear()`, 셰이더/텍스처/FBO/VBO 생성 팩토리 |
| `VeritySdl2Window` | irodori `Window` 추상 클래스 구현. SDL2 윈도우/GL 컨텍스트 관리. `OnSdlEvent` 이벤트 |
| `VeritySdl2Windowing` | irodori `IWindowing` 구현 |
| `Shader2D` | GLSL 330 core 셰이더 (uProjection, uView, uModel, uTexture, uColor). 유닛 쿼드 VBO 포함 |
| `Camera2D` | **Component**. `OrthographicSize`(world-unit half-height), `BackgroundColor`, `Zoom`. Owner가 있으면 Transform에서 위치/회전, 없으면 자체 Position/Rotation 사용 (에디터 독립 카메라 모드) |
| `SpriteRenderer` | Component. Texture, Color, SortingLayerName, OrderInLayer, Pivot, FlipX/FlipY |
| `RenderPipeline` | FBO 관리 + 씬 렌더링. SortingLayer → OrderInLayer → CustomSortAxis 3단계 정렬. `EnsureFbo()`, `RenderScene(scene, camera, fbo?)` |
| `TextureManager` | StbImageSharp 기반 텍스처 로드/캐시. `Load(path)`, `CreateFromRgba()`, `CreateWhitePixel()` |
| `SortingLayer` | static. 레이어 이름 기반 정렬 순서 관리. "Default" 레이어 기본 |

### 렌더링 파이프라인

```
RenderScene(scene, camera, fbo):
  1. Clear FBO with camera.BackgroundColor
  2. Collect all SpriteRenderer from scene (재귀 트리 순회)
  3. Sort: SortingLayer → OrderInLayer → CustomSortAxis (Y/X, asc/desc)
  4. For each SpriteRenderer:
     - Build model matrix (Transform world + Pivot + Flip)
     - Set projection/view/model/texture/color uniforms
     - Draw unit quad
```

### Projection Model

```
Projection = OrthographicOffCenter(-halfW, halfW, -halfH, halfH, -1, 1)
  where halfH = OrthographicSize * Zoom
        halfW = halfH * (viewportWidth / viewportHeight)

View = Translation(-pos) * RotationZ(-rot)
```

---

## Verity.Input — 입력

| 클래스 | 역할 |
|--------|------|
| `Input` | static. 프레임 기반 키보드/마우스 폴링. `GetKey()`, `GetKeyDown()`, `GetKeyUp()`, `GetMouseButton()`, `MousePosition`, `MouseDelta`, `ScrollDelta` |
| `KeyCode` | SDL 키코드 매핑 enum |
| `MouseButton` | Left, Right, Middle enum |

`BeginFrame()` → `ProcessEvent(SDL_Event)` → `EndFrame()` 순서로 매 프레임 호출.

---

## Verity.Editor — 에디터

### 핵심 클래스

| 클래스 | 역할 |
|--------|------|
| `EditorApp` | 에디터 메인 루프. GraphicsDevice, ImGuiController, Shader2D, TextureManager, RenderPipeline, Camera2D(에디터 카메라) 소유. Play/Edit 모드 전환 |
| `ImGuiController` | ImGui 초기화/프레임/셧다운. SDL2 + OpenGL3 백엔드 |
| `EditorWindow` | 에디터 패널 추상 베이스. `Title`, `IsOpen`, `OnGui()` |
| `EditorSelection` | static. `SelectedGameObject` |
| `SceneSnapshot` | Play 모드 진입 시 씬 스냅샷 캡처, 종료 시 복원. SpriteRenderer, Camera2D, Script, Generic 각각의 Snapshot 클래스 |

### 에디터 윈도우

| 윈도우 | 역할 |
|--------|------|
| `SceneViewWindow` | FBO → ImGui::Image 렌더링. 카메라 팬(미들 드래그)/줌(스크롤). 클릭 선택(AABB), 드래그 이동, 그리드 스냅 |
| `HierarchyWindow` | 씬 계층 트리뷰. 우클릭 → "Create Empty". 클릭 선택 |
| `InspectorWindow` | 선택된 GameObject의 컴포넌트 편집. **리플렉션 기반 자동 드로잉**: Transform은 커스텀 드로어, 나머지는 `[SerializeField]`/public get+set 프로퍼티 자동 감지 후 타입별 ImGui 컨트롤 생성 |
| `ConsoleWindow` | 로그 메시지 표시. `ConsoleWindow.Log()`, Clear 버튼 |

### Inspector 직렬화 규칙

- `[SerializeField]` → Inspector 노출
- `[HideInInspector]` → Inspector 숨김
- `[SerializeField]` 없는 public get+set 프로퍼티 → 지원 타입이면 자동 노출
- private 필드 → `[SerializeField]` 있을 때만 노출
- 지원 타입: `float`, `int`, `bool`, `string`, `Vector2`, `Vector3`, `Vector4`(ColorEdit4), `Enum`(Combo)
- 제외: `Owner`, `Enabled`, `HasStarted`, static, indexer

### DockSpace 레이아웃

```
┌──────────────┬──────────────────────┬───────────┐
│  Hierarchy   │                      │ Inspector │
│   (20%)      │      Scene View      │   (25%)   │
│              │                      │           │
├──────────────┴──────────────────────┴───────────┤
│                    Console (25%)                 │
└─────────────────────────────────────────────────┘
```

### Play/Edit 모드

1. **▶ Play**: `SceneSnapshot.Capture()` → `Time.Reset()` → `GameLoop` 생성 → `IsPlaying = true`
2. 매 프레임: `GameLoop.TickLogic(deltaTime)` 호출 (Script 라이프사이클 실행)
3. **■ Stop**: `SceneSnapshot.Restore()` → 씬 원복 → `IsPlaying = false`

### 에디터 진입점 (Program.cs)

```csharp
using var app = new EditorApp();
app.AddWindow(new SceneViewWindow(app));
app.AddWindow(new HierarchyWindow(app));
app.AddWindow(new InspectorWindow());
app.AddWindow(new ConsoleWindow());
app.Run();
```

---

## Key Design Decisions

1. **단일 SDL2 윈도우**: 게임 뷰와 에디터 UI가 같은 GL 컨텍스트 공유. FBO로 씬을 오프스크린 렌더링 후 ImGui에 표시.
2. **Camera2D dual-mode**: Component이지만 Owner 없이도 동작 (에디터 카메라용). Owner가 있으면 Transform에서 위치/회전을 읽음.
3. **Orthographic projection**: 월드 단위(OrthographicSize = half-height in world units). Zoom은 OrthographicSize에 곱해져 projection에 반영.
4. **리플렉션 기반 Inspector**: 하드코딩 없이 모든 Component 자동 지원. 새로운 컴포넌트 추가 시 `[SerializeField]` 어트리뷰트만 붙이면 Inspector 자동 노출.
5. **Sprite 정렬**: SortingLayer(이름 기반) → OrderInLayer(int) → CustomSortAxis(Y/X, asc/desc) 3단계.
6. **SceneSnapshot**: Play 모드 진입 시 값 타입 필드를 전부 캡처. GPU 리소스(Texture)는 참조만 유지.
