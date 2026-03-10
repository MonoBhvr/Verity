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
3. **FixedUpdate**: 물리 Tick마다 호출. 고정 시간 간격(`Time.FixedDeltaTime`).
4. **Update**: 로직 Tick마다 호출. 매 프레임의 핵심 게임 로직.
5. **LateUpdate**: 모든 `Update`가 끝난 후 호출. 카메라 추적 등에 사용.

---

## 3. Filter System (`Verity.Input.Filter`)

Verity Engine은 복잡한 조건 비교 로직을 데이터화하여 관리할 수 있는 **Filter** 시스템을 제공합니다. 이는 코드 하드코딩을 줄이고 에디터에서 게임 규칙을 동적으로 제어하기 위한 핵심 기능입니다.

### 3.1. 개념 (Concept)
필터는 **"여러 개의 Enum 값을 하나의 논리적 그룹으로 묶는 것"**입니다. 이를 통해 "이 아이템이 장착 가능한가?", "이 진영이 적인가?"와 같은 질문에 대해 데이터 기반으로 응답할 수 있습니다. 모든 필터 데이터는 `Assets/Filters.json`에 저장됩니다.

### 3.2. Filter 종류
*   **Filter (단일 타입)**: 하나의 Enum 타입 내에서 복수의 값을 비교합니다. (예: `KeyCode` 중 조작키들)
*   **Mixed-Filter (혼합 타입)**: 서로 다른 여러 Enum 타입의 값들을 한꺼번에 담을 수 있습니다. (예: `KeyCode` + `MouseButton` + `GamepadButton`)

**Properties:**
| Name | Type | Description |
| :--- | :--- | :--- |
| `Name` | `string` | 필터의 고유 식별 이름입니다. |
| `Mode` | `FilterMode` | `Whitelist`(포함 시 True) 또는 `Blacklist`(제외 시 True)를 결정합니다. |
| `Check(value)` | `Method` | 특정 Enum 값이 필터 조건에 맞는지 검사합니다. |

**Generic Example:**
```csharp
// 에디터에서 "RedTeamOnly" 필터(Whitelist)를 만들고 Faction.Red, Faction.Blue를 추가했을 때
public Filter accessFilter; 

void OnEnterPortal(Faction visitor) {
    if (accessFilter.Check(visitor)) {
        OpenPortal(); // Red, Blue 진영만 통과
    }
}
```

---

## 4. Input System (`Verity.Input.Input`)

입력 시스템은 키보드와 마우스 입력을 처리하며, 위에서 설명한 **Filter System**을 활용하여 **'Action Binding'**을 구현합니다.

### 4.1. Unified Input
Verity에서는 마우스 버튼이 `KeyCode`로 통합되어 있습니다. 따라서 키보드 키와 마우스 버튼을 구분 없이 하나의 API로 처리할 수 있습니다.
*   `KeyCode.MouseLeft`, `KeyCode.MouseRight`, `KeyCode.MouseMiddle` 등

### 4.2. Action Mapping (Filter 활용)
특정 동작(예: "Jump")에 대해 여러 키를 바인딩하고 싶을 때 필터를 사용합니다. 에디터에서 "Jump"라는 이름의 필터를 만들고 여러 키를 등록하면, 코드에서는 이름 하나로 모든 입력을 감지합니다.

**Methods:**
| Name | Description |
| :--- | :--- |
| `GetKey(name / key)` | 입력이 유지되는 동안 `true`. |
| `GetKeyDown(name / key)` | 입력을 시작한 순간 한 번 `true`. |
| `GetKeyUp(name / key)` | 입력을 뗀 순간 한 번 `true`. |

**Input Example:**
```csharp
// 1. 직접적인 키 체크
if (Input.GetKeyDown(KeyCode.Space)) Jump();

// 2. 필터 이름을 이용한 액션 체크 (권장)
// "Attack" 필터에 [LeftCtrl, MouseLeft]가 있다면 둘 중 무엇을 눌러도 작동
if (Input.GetKeyDown("Attack")) DoAttack();

// 3. 인스펙터에 노출된 필터 변수 사용
public Filter interactFilter;
void Update() {
    if (Input.GetKeyDown(interactFilter)) Interact();
}
```

---

## 5. Core Data Types

### 5.1. Time (`Verity.Core.Engine.Time`)
시간 및 실행 횟수 관련 정적 클래스입니다.

| Property | Description |
| :--- | :--- |
| `DeltaTime` | 지난 Tick부터 현재 Tick까지 걸린 시간(초)입니다. |
| `FixedDeltaTime` | 고정 업데이트 간격입니다. (`FixedUpdate` 실행 주기) |
| `FrameCount` | 화면에 렌더링된 총 프레임 수입니다. |
| `LogicTickCount` | `Update`가 실행된 총 횟수입니다. |

---

## 6. Graphics & Camera

### 6.1. Camera (`Verity.Graphics.Camera`)
월드를 비추는 카메라입니다. 모든 월드에는 최소 하나 이상의 활성화된 카메라가 필요합니다.

**Properties & Methods:**
| Name | Type | Description |
| :--- | :--- | :--- |
| `Main` | `Camera?` | (Static) 현재 활성화된 월드의 메인 카메라를 반환합니다. |
| `Zoom` | `float` | 카메라 확대/축소 배율입니다. |
| `FixedAspectRatio` | `bool` | 고정 화면 비율(레터박스) 사용 여부입니다. |
| `AspectWidth / Height`| `float` | 고정할 화면 비율의 가로/세로 값입니다. |

---

## 7. Editor Features

### 7.1. Filter Editor Window
**Window > Filter Editor** 메뉴를 통해 프로젝트에서 사용할 모든 필터를 관리할 수 있습니다. 여기서 생성한 필터는 즉시 인스펙터와 `Input` API에서 사용할 수 있습니다.

### 7.2. Shortcuts
*   `F`: 선택된 엔티티로 카메라 포커스.
*   `F2`: 선택된 엔티티/에셋 이름 변경.
*   `Ctrl + N`: 빈 엔티티 생성 또는 새 폴더 생성.
*   `W / E / R`: 이동 / 스케일 / 회전 도구 전환.

---

## 8. Branding & Customization

### 8.1. Editor & Build Logo
*   **에디터 로고**: `EditorResources/EditorLogo.png` 파일이 런처와 창 아이콘에 적용됩니다.
*   **빌드본 로고**: `BuildSettings.json`의 `LogoPath`에 지정된 파일이 사용됩니다.
