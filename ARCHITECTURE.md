# Verity Engine Architecture & Scripting API Reference

Verity Engine은 C# 기반의 Entity-Component-System (ECS) 아키텍처를 따르는 2D 게임 엔진입니다. 유니티 엔진과 유사한 워크플로우를 제공하며, 직관적인 스크립팅 API를 제공합니다.

---

## 1. Core Architecture (ECS)

### 1.1. Entity (`Verity.Core.ECS.Entity`)
월드(World)에 존재하는 모든 객체의 기본 단위입니다. 엔티티 자체는 데이터나 동작을 가지지 않으며, 컴포넌트들을 담는 컨테이너 역할을 합니다.

**Properties:**
| Name | Type | Description |
| :--- | :--- | :--- |
| `Id` | `Guid` | 엔티티의 고유 식별자입니다. Undo/Redo 및 직렬화 시 엔티티를 정확히 추적하는 데 사용됩니다. |
| `Name` | `string` | 엔티티의 이름입니다. 에디터의 Hierarchy 창에 표시됩니다. |
| `Active` | `bool` | 엔티티의 활성화 상태입니다. `false`일 경우 해당 엔티티와 모든 컴포넌트의 업데이트가 중지됩니다. |
| `Transform` | `Transform` | 엔티티의 위치, 회전, 크기를 담당하는 필수 컴포넌트입니다. |

**Code Example:**
```csharp
// 엔티티 생성 및 컴포넌트 추가 예시
Entity player = WorldManager.ActiveWorld.CreateEntity("Player");
player.Transform.Position = new Vector2(0, 0);

// 컴포넌트 추가
var renderer = player.AddComponent<SpriteRenderer>();
renderer.Sprite = "Assets/Textures/Player.png";

// 컴포넌트 가져오기
var foundRenderer = player.GetComponent<SpriteRenderer>();
```

---

### 1.2. Component (`Verity.Core.ECS.Component`)
모든 기능적 요소(Script, Renderer, Transform 등)의 최상위 부모 클래스입니다.

**Properties:**
| Name | Type | Description |
| :--- | :--- | :--- |
| `Owner` | `Entity` | 이 컴포넌트가 부착된 엔티티를 가리킵니다. (Unity의 `gameObject`와 유사) |
| `Transform` | `Transform` | `Owner.Transform`에 대한 단축 접근 프로퍼티입니다. |
| `Enabled` | `bool` | 컴포넌트의 활성화 상태입니다. |

**Lifecycle Methods:**
| Name | Description |
| :--- | :--- |
| `OnEnable()` / `OnDisable()` | 컴포넌트가 활성화되거나 비활성화될 때 호출됩니다. |
| `OnDestroy()` | 컴포넌트가 제거되거나 엔티티가 파괴될 때 호출됩니다. |

---

### 1.3. Transform (`Verity.Core.ECS.Transform`)
엔티티의 공간적 정보와 계층 구조(Hierarchy)를 관리합니다.

**Properties:**
| Name | Type | Description |
| :--- | :--- | :--- |
| `Position` / `Rotation` / `Scale` | `Vector2` / `float` / `Vector2` | 부모를 기준으로 한 로컬 변환 값입니다. |
| `WorldPosition` / `WorldRotation` | `Vector2` / `float` | 월드 공간에서의 절대 변환 값입니다. (Read-only) |
| `Parent` | `Transform?` | 부모 트랜스폼입니다. |
| `Children` | `IReadOnlyList<Transform>` | 자식 트랜스폼 리스트입니다. |

---

## 2. Scripting API (`Verity.Core.ECS.Script`)

사용자가 게임 로직을 작성할 때 상속받는 기본 클래스입니다. `Component`를 상속받습니다.

### Lifecycle Flow & Examples
Verity의 로직 실행 단위는 **Tick**입니다.

1. **Awake**: 스크립트 생성 시 호출.
2. **Start**: 첫 번째 `Update` Tick 실행 전 호출.
3. **FixedUpdate**: 물리 Tick(`PhysicsTickCount`) 마다 호출. 고정 시간 간격(`Time.FixedDeltaTime`).
4. **Update**: 로직 Tick(`LogicTickCount`) 마다 호출. 매 프레임의 핵심 게임 로직.
5. **LateUpdate**: 모든 `Update`가 끝난 후 호출. 카메라 추적 등에 사용.

**Code Example:**
```csharp
public class PlayerController : Script
{
    public float Speed = 5f;

    public void Update()
    {
        // 매 Logic Tick마다 실행
        Vector2 move = Vector2.Zero;
        if (Input.GetKey(KeyCode.W)) move.Y += 1;
        if (Input.GetKey(KeyCode.S)) move.Y -= 1;
        
        Transform.Position += move * Speed * Time.DeltaTime;
    }

    public void FixedUpdate()
    {
        // 매 Physics Tick마다 실행 (일정한 시간 간격)
    }
}
```

---

## 3. Core Data Types

### 3.1. Time (`Verity.Core.Engine.Time`)
시간 및 실행 횟수 관련 정적 클래스입니다.

| Property | Description |
| :--- | :--- |
| `DeltaTime` | 지난 Tick부터 현재 Tick까지 걸린 시간(초)입니다. |
| `FixedDeltaTime` | 고정 업데이트 간격입니다. (`FixedUpdate` 실행 주기) |
| `FrameCount` | 화면에 렌더링된 총 프레임 수입니다. |
| `LogicTickCount` | **`Update`가 실행된 총 횟수**입니다. |
| `PhysicsTickCount` | **`FixedUpdate`가 실행된 총 횟수**입니다. |

---

## 4. Graphics & Camera

### 4.1. Camera (`Verity.Graphics.Camera`)
월드를 비추는 카메라입니다. 모든 월드에는 최소 하나 이상의 활성화된 카메라가 필요합니다.

**Properties & Methods:**
| Name | Type | Description |
| :--- | :--- | :--- |
| `Main` | `Camera?` | (Static) **현재 활성화된 월드의 메인 카메라**를 자동으로 찾아 반환합니다. |
| `Zoom` | `float` | 카메라 확대/축소 배율입니다. |
| `FixedAspectRatio` | `bool` | 고정 화면 비율(레터박스) 사용 여부입니다. `true`일 경우 창 크기에 상관없이 지정된 비율을 유지합니다. |
| `AspectWidth` / `Height` | `float` | 고정할 화면 비율의 가로/세로 값입니다. (예: 16, 9) |
| `LetterboxColor` | `Color` | 화면 비율 유지 시 발생하는 빈 공간(레터박스)의 색상입니다. |
| `SetViewportSize(w, h)` | `Method` | 카메라가 렌더링할 영역의 크기를 설정합니다. |

**Code Example:**
```csharp
// 어디서든 메인 카메라에 접근
var cam = Camera.Main;
if (cam != null)
{
    // 16:9 고정 비율 및 검은색 레터박스 설정
    cam.FixedAspectRatio = true;
    cam.AspectWidth = 16;
    cam.AspectHeight = 9;
    cam.LetterboxColor = Color.Black;

    // 마우스 위치를 월드 좌표로 변환
    Vector2 mouseWorldPos = cam.ScreenToWorld(Input.MousePosition);
}
```

---

## 5. Editor Features

### 5.1. Profiler Window (기본 비활성)
엔진의 성능을 실시간으로 모니터링합니다.
*   **FPS**: 초당 렌더링 프레임 수.
*   **TPS (Actual)**: 초당 실제 **Logic Tick(`Update`)** 발생 횟수.
*   **PTPS (Actual)**: 초당 실제 **Physics Tick(`FixedUpdate`)** 발생 횟수.

### 5.2. Undo/Redo System
트랜잭션 기반으로 상태를 기록합니다. `Guid`를 통해 엔티티를 식별하므로, Undo 후에도 선택 상태나 참조가 정확히 유지됩니다.
*   **Undo**: `Ctrl + Z`
*   **Redo**: `Ctrl + Y` 또는 `Ctrl + Shift + Z`

### 5.3. Shortcuts
*   `F`: Hierarchy나 WorldView에서 선택된 엔티티로 카메라 포커스.
*   `F2`: 선택된 엔티티/에셋 이름 변경.
*   `Ctrl + N`: 빈 엔티티 생성 또는 새 폴더 생성.
*   `W / E / R`: 이동 / 스케일 / 회전 기즈모 도구 전환.

---

## 6. Branding & Customization

### 6.1. Editor & Build Logo
*   **에디터 로고**: `EditorResources/EditorLogo.png` 파일이 런처와 창 아이콘에 적용됩니다.
*   **빌드본 로고**: `BuildSettings.json`의 `LogoPath`에 지정된 파일이 스플래시 스크린과 게임 아이콘으로 사용됩니다.
