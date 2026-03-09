# Verity Engine Architecture & Scripting API Reference

Verity Engine은 C# 기반의 Entity-Component-System (ECS) 아키텍처를 따르는 2D 게임 엔진입니다. 유니티 엔진과 유사한 워크플로우를 제공하며, 직관적인 스크립팅 API를 제공합니다.

---

## 1. Core Architecture (ECS)

### 1.1. Entity (`Verity.Core.ECS.Entity`)
월드(World)에 존재하는 모든 객체의 기본 단위입니다. 엔티티 자체는 데이터나 동작을 가지지 않으며, 컴포넌트들을 담는 컨테이너 역할을 합니다.

**Properties:**
| Name | Type | Description |
| :--- | :--- | :--- |
| `Name` | `string` | 엔티티의 이름입니다. 에디터의 Hierarchy 창에 표시됩니다. |
| `Active` | `bool` | 엔티티의 활성화 상태입니다. `false`일 경우 해당 엔티티와 모든 컴포넌트의 업데이트가 중지됩니다. |
| `Transform` | `Transform` | 엔티티의 위치, 회전, 크기를 담당하는 필수 컴포넌트입니다. 모든 엔티티는 생성 시 `Transform`을 가집니다. |

**Methods:**
| Name | Description |
| :--- | :--- |
| `AddComponent<T>()` | 새로운 컴포넌트 `T`를 추가하고 반환합니다. |
| `GetComponent<T>()` | 해당 타입의 컴포넌트가 있다면 반환하고, 없으면 `null`을 반환합니다. |
| `RemoveComponent(Component)` | 특정 컴포넌트 인스턴스를 제거합니다. (`Transform`은 제거 불가) |
| `GetAllComponents()` | 엔티티에 부착된 모든 컴포넌트 리스트를 반환합니다. |

---

### 1.2. Component (`Verity.Core.ECS.Component`)
모든 기능적 요소(Script, Renderer, Transform 등)의 최상위 부모 클래스입니다.

**Properties:**
| Name | Type | Description |
| :--- | :--- | :--- |
| `Owner` | `Entity` | 이 컴포넌트가 부착된 엔티티를 가리킵니다. (Unity의 `gameObject`와 유사) |
| `Transform` | `Transform` | `Owner.Transform`에 대한 단축 접근 프로퍼티입니다. |
| `Enabled` | `bool` | 컴포넌트의 활성화 상태입니다. |

**Lifecycle Methods (Override these):**
| Name | Description |
| :--- | :--- |
| `OnDestroy()` | 컴포넌트가 제거되거나 엔티티가 파괴될 때 호출됩니다. 리소스 정리 등에 사용합니다. |

---

### 1.3. Transform (`Verity.Core.ECS.Transform`)
엔티티의 공간적 정보와 계층 구조(Hierarchy)를 관리합니다.

**Properties:**
| Name | Type | Description |
| :--- | :--- | :--- |
| `Position` | `Vector2` | 부모를 기준으로 한 로컬 위치입니다. |
| `Rotation` | `float` | 부모를 기준으로 한 로컬 회전값(도, Degree)입니다. |
| `Scale` | `Vector2` | 부모를 기준으로 한 로컬 크기입니다. 기본값은 `(1, 1)`입니다. |
| `WorldPosition` | `Vector2` | (Read-only) 월드 공간에서의 절대 위치입니다. |
| `WorldRotation` | `float` | (Read-only) 월드 공간에서의 절대 회전값입니다. |
| `Parent` | `Transform?` | 부모 트랜스폼입니다. 변경 시 자식은 부모의 변환을 따라갑니다. 순환 참조(Cycle) 시 오류가 발생합니다. |
| `Children` | `List<Transform>` | (Read-only) 현재 트랜스폼을 부모로 둔 자식들의 리스트입니다. |

**Methods:**
| Name | Description |
| :--- | :--- |
| `SetParent(Transform parent, bool preserveWorldPosition)` | 부모를 변경합니다. `preserveWorldPosition`이 `true`이면 월드상 위치를 유지하기 위해 로컬 `Position`을 재계산합니다. |

---

## 2. Scripting API (`Verity.Core.ECS.Script`)

사용자가 게임 로직을 작성할 때 상속받는 기본 클래스입니다. `Component`를 상속받습니다.

### Lifecycle Flow
Verity uses a Unity-style lifecycle system. You do **not** need to use `override` for lifecycle methods; simply defining a method with the correct name (even if private) is enough.

1. **Awake**: 스크립트가 생성된 직후 호출 (초기화)
2. **Start**: 첫 번째 Update 실행 전 호출
3. **FixedUpdate**: 고정된 시간 간격(0.016s)마다 호출 (물리 연산 등)
4. **Update**: 매 프레임마다 호출 (게임 로직)
5. **LateUpdate**: 모든 Update가 끝난 후 호출 (카메라 추적 등)
6. **OnDestroy**: 삭제될 때 호출

### Code Example
```csharp
using Verity.Core;
using Verity.Core.ECS;
using Verity.Input;
using Verity.Graphics;

public class PlayerController : Script
{
    // 인스펙터에 노출됨 (Inspector View)
    public float moveSpeed = 5.0f;
    public Color playerColor = Color.Red;

    [SerializeField]
    private float _hiddenTimer; // SerializeField로 인해 인스펙터에 노출됨

    private SpriteRenderer _renderer;

    void Start()
    {
        // 다른 컴포넌트 가져오기
        _renderer = Owner.GetComponent<SpriteRenderer>();
        if (_renderer != null)
        {
            _renderer.Color = playerColor;
        }
    }

    void Update()
    {
        // 입력 처리
        if (Input.GetKey(KeyCode.W))
        {
            Transform.Position += new System.Numerics.Vector2(0, 1) * moveSpeed * Time.DeltaTime;
        }
    }
}
```

---

## 3. Core Data Types

### 3.1. Color (`Verity.Core.Color`)
RGBA 색상을 표현하는 구조체입니다. 값의 범위는 `0.0f` ~ `1.0f`입니다. `System.Numerics.Vector4` 및 `System.Drawing.Color`와 암시적 변환이 가능합니다.

*   **기본값**: 초기화되지 않은 경우 `R=1, G=1, B=1, A=1` (White)로 처리됩니다.
*   **Static Colors**: `White`, `Black`, `Red`, `Green`, `Blue`, `CornflowerBlue` 등 제공.

### 3.2. Sprite (`Verity.Core.Sprite`)
이미지 리소스의 경로를 래핑하는 구조체입니다. 인스펙터에서 이미지 파일을 드래그하여 할당할 수 있습니다.

*   `Path` (string): 에셋 폴더 기준 상대 경로 (예: `Assets/Images/player.png`).

### 3.3. Time (`Verity.Core.Engine.Time`)
시간 관련 정적 클래스입니다.

| Property | Description |
| :--- | :--- |
| `DeltaTime` | `float`. 지난 프레임부터 현재 프레임까지 걸린 시간(초)입니다. 이동 로직에 필수적입니다. |
| `TimeScale` | `float`. 시간의 흐름 속도입니다. `0`이면 일시정지, `1`이면 정상 속도입니다. |
| `TotalTime` | `float`. 게임 시작 후 경과된 누적 시간입니다. |

---

## 4. Graphics Components

### 4.1. SpriteRenderer (`Verity.Graphics.SpriteRenderer`)
엔티티 위치에 2D 이미지를 렌더링합니다.

| Property | Type | Description |
| :--- | :--- | :--- |
| `Sprite` | `Sprite` | 렌더링할 이미지 파일입니다. 인스펙터에서 드래그 가능합니다. |
| `Color` | `Color` | 텍스처에 곱해질 틴트(Tint) 색상입니다. 투명도 조절도 가능합니다. |
| `FlipX` / `FlipY` | `bool` | 이미지를 가로/세로로 반전합니다. |
| `Pivot` | `Vector2` | 회전 및 위치의 기준점입니다. `(0.5, 0.5)`가 중앙입니다. |
| `SortingLayerName` | `string` | 렌더링 순서 레이어 이름입니다. |
| `OrderInLayer` | `int` | 같은 레이어 내에서의 렌더링 우선순위입니다. 높을수록 나중에(앞에) 그려집니다. |

### 4.2. Camera (`Verity.Graphics.Camera`)
월드를 비추는 카메라입니다.

| Property | Type | Description |
| :--- | :--- | :--- |
| `OrthographicSize` | `float` | 카메라가 비추는 영역의 세로 크기 절반입니다. (Zoom과 유사) |
| `BackgroundColor` | `Color` | 아무것도 없는 영역을 채울 배경색입니다. |
| `Zoom` | `float` | 추가적인 확대/축소 배율입니다. |

---

## 5. Input System (`Verity.Input.Input`)

사용자의 키보드 및 마우스 입력을 처리하는 정적 클래스입니다.

| Method | Description |
| :--- | :--- |
| `GetKey(KeyCode key)` | 해당 키를 누르고 있는 동안 `true`를 반환합니다. |
| `GetKeyDown(KeyCode key)` | 해당 키를 누른 그 프레임에만 `true`를 반환합니다. |
| `GetKeyUp(KeyCode key)` | 해당 키를 뗀 그 프레임에만 `true`를 반환합니다. |
| `GetMouseButton(int button)` | 마우스 버튼(0:좌, 1:중, 2:우)을 누르고 있는지 확인합니다. |
| `MousePosition` | `Vector2`. 화면상의 마우스 좌표를 반환합니다. |

---

## 6. Attributes (Inspector & Serialization)

인스펙터의 표시 여부와 저장(직렬화) 동작을 제어하는 특성들입니다.

| Attribute | Applies To | Description |
| :--- | :--- | :--- |
| `[SerializeField]` | `private` Field | 비공개 필드를 인스펙터에 노출하고 월드 파일에 저장되게 합니다. |
| `[HideInInspector]` | `public` Field/Prop | 공개 멤버를 인스펙터에서 숨기고 저장하지 않습니다. |
| `[AssetReference(ext)]` | `string` | 문자열 필드에 특정 확장자(예: `.png;.jpg`) 파일만 드래그 앤 드롭 되도록 제한합니다. |

---

## 7. World Management (`Verity.Core.Engine.WorldLoader`)

씬(월드) 전환 및 로딩을 담당합니다.

| Method | Description |
| :--- | :--- |
| `LoadWorld(string path)` | 파일 경로(`.verity`)를 통해 월드를 로드합니다. 에디터와 런타임 모두 사용합니다. |
| `LoadWorldByName(string name)` | 스크립트에서 사용 권장. 다음 프레임에 해당 이름의 월드로 전환을 예약합니다. |

---

## 8. Editor Features

### 8.1. Project Window (Asset Browser)
*   **Create**: 빈 공간 우클릭 -> `Create` -> `Script`/`World`/`Folder`로 새 에셋을 생성합니다.
*   **Rename**: 파일 우클릭 -> `Rename`. (단, 최상위 Assets 폴더는 수정 불가)
*   **Show in Explorer**: 실제 파일 위치를 윈도우 탐색기에서 엽니다.
*   **Drag & Drop**: 파일을 드래그하여 폴더 간 이동하거나, 인스펙터의 필드에 할당할 수 있습니다.

### 8.2. Inspector Window
*   **Auto-Serialization**: 스크립트의 `public` 변수는 자동으로 UI에 표시됩니다.
*   **Picker**: 컴포넌트나 스프라이트 필드 옆의 `o` 버튼을 눌러 프로젝트 내의 리소스를 검색하고 할당할 수 있습니다.
*   **Color Picker**: `Color` 타입은 투명도(Alpha) 조절이 가능한 전용 피커를 제공합니다.

### 8.3. Build Settings
*   게임에 포함될 월드 목록을 관리합니다.
*   **Start World**: 목록 중 녹색으로 표시된 월드가 게임 시작 시 가장 먼저 로드됩니다.
*   **Add Active World**: 현재 편집 중인 월드를 빌드 목록에 추가합니다.

### 8.4. Build & Publish
*   **Menu**: `Build` -> `Publish (Single EXE)`
*   **Process**: 현재 프로젝트의 에셋과 코드를 엔진 코어와 결합하여 단일 실행 파일(`.exe`)로 추출합니다. 빌드 중에는 화면에 진행 상황 오버레이가 표시됩니다.
