# Verity Engine Architecture & Scripting API Reference

Verity Engine은 C# 기반의 Entity-Component-System (ECS) 아키텍처를 따르는 2D 게임 엔진입니다.
유니티 엔진과 유사한 워크플로우를 제공하며, 직관적인 스크립팅 API를 제공합니다.

[Irodori](https://github.com/R2turnTrue/irodori)(OpenGL, via Silk.Net)를 그래픽 엔진으로 사용합니다.


## >Simpler, Easier
여러 상용 엔진들과 달리, Verity는 학습을 위한 쉬운 스크립팅을 목표로 합니다. 복잡한 네이티브 코드나 엔진 구조 대신, C# 클래스 상속과 간단한 메서드 오버라이드만으로 게임 로직을 작성할 수 있습니다.

---

## 1. Execution Cycles (Frames & Ticks)

Verity Engine의 실행 루프는 세 가지 주요 주기로 나뉩니다.

### 1.1. Frame (프레임)
- **정의**: OS가 화면에 그림을 그리도록 요청하고 그래픽 카드가 이를 처리하는 단위입니다.
- **특징**: 하드웨어 성능과 OS 설정(V-Sync 등)에 따라 속도가 달라집니다. `SwapBuffers` 호출이 이 주기를 마무리합니다.

### 1.2. Logic Tick (논리 틱 / Update / FixedUpdate)
- **정의**: 게임의 데이터와 로직이 업데이트되는 기본 단위입니다.
- **관련 API**: `Update()`, `FixedUpdate()`, `LateUpdate()`, `Time.DeltaTime`.
- **특징**: `ProjectSettings.TargetTPS` 주기로 실행됩니다. 현재 `FixedUpdate` 역시 물리 연산 직전의 논리 틱 주기에 동기화되어 실행됩니다.

### 1.3. Physical Tick (물리 틱)
- **정의**: 물리 엔진이 실제 충돌 판정 및 속도 연산을 수행하는 고정 시간 단위입니다.
- **특징**: `ProjectSettings.TargetPTPS` 주기로 실행되어 물리 시뮬레이션의 안정성을 보장합니다.

---

## 2. Core Architecture (ECS)

### 2.1. World & Entity Management (`Verity.Core.World.WorldManager`)
월드와 엔티티의 생명주기를 관리하는 핵심 시스템입니다.

| Name | Type | Description |
| :--- | :--- | :--- |
| `WorldManager.ActiveWorld` | `World?` | 현재 활성화되어 물리 및 로직이 업데이트되는 월드입니다. |
| `world.CreateEntity(name)` | `Method` | 지정된 이름으로 새로운 엔티티를 생성하여 월드에 추가합니다. |
| `world.DestroyEntity(entity)` | `Method` | 엔티티를 월드에서 제거하고 관련 리소스를 해제합니다. (지연 삭제) |

#### 2.1.1. World Loading (`Verity.Core.Engine.WorldLoader`)
프로젝트의 씬(월드) 파일을 동적으로 로드하고 관리합니다.

| Method | Description |
| :--- | :--- |
| `LoadWorld(path, assembly)` | 지정된 경로의 `.verity` 파일을 로드하고 활성화합니다. |
| `LoadWorldFromJson(json, name)` | JSON 문자열로부터 월드를 복구합니다. |
| `LoadWorldByName(name)` | 다음 틱에 로드할 월드 이름을 예약합니다. |

### 2.2. Entity (`Verity.Core.ECS.Entity`)
월드(World)에 존재하는 모든 객체의 기본 단위입니다. 엔티티 자체는 데이터나 동작을 가지지 않으며, 컴포넌트들을 담는 컨테이너 역할을 합니다.

| Name | Type | Description |
| :--- | :--- | :--- |
| `Id` | `Guid` | 엔티티의 고유 식별자입니다. Undo/Redo 및 직렬화 시 엔티티를 정확히 추적하는 데 사용됩니다. |
| `Name` | `string` | 엔티티의 이름입니다. 에디터의 Hierarchy 창에 표시됩니다. |
| `Active` | `bool` | 엔티티의 활성화 상태입니다. `false`일 경우 해당 엔티티와 모든 컴포넌트의 업데이트가 중지됩니다. |
| `Transform` | `Transform` | 엔티티의 위치, 회전, 크기를 담당하는 필수 컴포넌트입니다. |

**Component Operations:**
| Method | Description |
| :--- | :--- |
| `GetComponent<T>()` | 엔티티에서 특정 타입의 첫 번째 컴포넌트를 반환합니다. |
| `GetComponents<T>()` | 엔티티에 부착된 특정 타입의 모든 컴포넌트 목록을 반환합니다. |
| `AddComponent<T>()` | 엔티티에 새로운 컴포넌트를 추가하고 반환합니다. |
| `RemoveComponent<T>()` | 엔티티에서 특정 타입의 컴포넌트를 제거합니다. |

### 2.3. Component (`Verity.Core.ECS.Component`)
모든 기능적 요소(Script, Renderer, Transform 등)의 최상위 부모 클래스입니다.

| Name | Type | Description |
| :--- | :--- | :--- |
| `Owner` | `Entity` | 이 컴포넌트가 부착된 엔티티를 가리킵니다. (Unity의 `gameObject`와 유사) |
| `Transform` | `Transform` | `Owner.Transform`에 대한 단축 접근 프로퍼티입니다. |
| `Enabled` | `bool` | 컴포넌트의 활성화 상태입니다. |

**Interfaces:**
*   `IHasSize`: `Vector2 Size` 속성을 가지며, 렌더러나 충돌체 등 크기 정보가 필요한 컴포넌트에서 사용됩니다.

### 2.4. Transform (`Verity.Core.ECS.Transform`)
엔티티의 공간적 정보와 계층 구조(Hierarchy)를 관리합니다.

| Name | Type | Description |
| :--- | :--- | :--- |
| `Position` / `Rotation` / `Scale` | `Vector2` / `float` / `Vector2` | 부모를 기준으로 한 로컬 변환 값입니다. |
| `WorldPosition` / `WorldRotation` | `Vector2` / `float` | 월드 공간에서의 절대 변환 값입니다. (Read-only) |
| `Parent` | `Transform?` | 부모 트랜스폼입니다. |
| `Children` | `IReadOnlyList<Transform>` | 자식 트랜스폼 리스트입니다. |

| Method | Description |
| :--- | :--- |
| `SetParent(parent, preserveWorld)` | 부모 트랜스폼을 설정합니다. `preserveWorldPosition`이 true이면 월드 좌표를 유지합니다. |
| `GetWorldMatrix()` | 렌더링이나 물리 연산에 사용될 3x3 변환 행렬을 반환합니다. |

### 2.5. Serialization System (`Verity.Core.Serialization.SceneSerializer`)
월드와 엔티티의 상태를 JSON 형식으로 저장하고 복구하는 Reflection 기반 시스템입니다.

*   **Recursive Serialization**: 부모 엔티티를 저장할 때 자식 계층 구조를 자동으로 포함합니다.
*   **Attribute-Based**: `[SerializeField]`가 붙은 `private` 필드나 `public` 프로퍼티를 자동으로 감지합니다.
*   **Cross-Assembly Support**: 유저 프로젝트에서 정의된 스크립트 타입도 런타임에 동적으로 찾아 복구합니다.
*   **Path Normalization**: 에셋 경로를 프로젝트 루트 기준(`Assets/...`)으로 정규화하여 저장합니다.

---

## 3. Scripting API (`Verity.Core.ECS.Script`)

사용자가 게임 로직을 작성할 때 상속받는 기본 클래스입니다. `Component`를 상속받으며, `private` 메서드로 선언해도 엔진이 자동으로 찾아 실행합니다.

### 3.1. Lifecycle Flow (실행 순서)
Verity의 로직 실행 단위는 **Tick**입니다.

1. **Awake**: 스크립트 생성 시 호출.
2. **Start**: 첫 번째 `Update` Tick 실행 전 호출.
3. **FixedUpdate**: 물리 Tick마다 호출. 고정 시간 간격(`Time.FixedDeltaTime`).
4. **Update**: 로직 Tick마다 호출. 매 프레임의 핵심 게임 로직.
5. **LateUpdate**: 모든 `Update`가 끝난 후 호출. 카메라 추적 등에 사용.

| Method | Timing | Description |
| :--- | :--- | :--- |
| `Awake` | Initialization | 컴포넌트 활성화 시, 첫 `Update` 직전에 한 번 호출됩니다. |
| `OnEnable` | Activation | 컴포넌트나 엔티티가 활성화될 때마다 호출됩니다. |
| `Start` | Initialization | `Awake` 직후, 첫 `Update` 직전에 한 번 호출됩니다. |
| `FixedUpdate` | Logic Tick | 매 논리 틱마다 물리 연산 이전에 호출됩니다. |
| `Update` | Logic Tick | 매 논리 틱마다 일반 게임 로직을 위해 호출됩니다. |
| `LateUpdate` | Logic Tick | 모든 `Update`가 끝난 후 화면 렌더링 직전에 호출됩니다. |
| `OnDisable` | Deactivation | 컴포넌트나 엔티티가 비활성화될 때마다 호출됩니다. |
| `OnDestroy` | Destruction | 엔티티나 컴포넌트가 제거될 때 한 번 호출됩니다. |

### 3.2. Attributes (에디터 연동)
필드나 프로퍼티에 적용하여 에디터 인스펙터에서의 동작을 제어합니다.

| Attribute | Target | Description |
| :--- | :--- | :--- |
| `[SerializeField]` | Field/Property | `private` 변수를 에디터에 노출하고 직렬화(저장)합니다. |
| `[HideInInspector]`| Field/Property | `public` 변수를 에디터 창에서 숨깁니다. |
| `[Button("Label")]` | Method | 에디터 인스펙터에 해당 메서드를 실행하는 버튼을 생성합니다. |
| `[AssetReference]` | Field/Property | 특정 확장자의 파일(이미지 등)을 드래그 앤 드롭으로 연결할 수 있게 합니다. |

### 3.3. Physics Events (물리 이벤트)
객체 간의 충돌 및 감지 발생 시 호출됩니다.

| Method | Parameter | Description |
| :--- | :--- | :--- |
| `OnTouched` | `Physical other` | 비센서 객체와 충돌이 시작된 순간 호출됩니다. |
| `OnTouching` | `Physical other` | 비센서 객체와 접촉이 유지되는 동안 매 틱 호출됩니다. |
| `OnTouchEnd` | `Entity other` | 비센서 객체와 충돌이 종료된 순간 호출됩니다. |
| `OnDetected` | `Entity other` | 센서 영역에 객체가 진입한 순간 호출됩니다. |
| `OnDetecting`| `Entity other` | 센서 영역 내에 객체가 체류하는 동안 매 틱 호출됩니다. |
| `OnDetectEnd` | `Entity other` | 센서 영역에서 객체가 이탈한 순간 호출됩니다. |

### 3.4. Debug & Gizmo Callbacks (디버그)
에디터 환경에서 시각적인 도움을 주기 위해 호출됩니다.

| Method | Visibility | Description |
| :--- | :--- | :--- |
| `OnDrawGizmos` | Always | 에디터 월드 뷰에서 항상 호출됩니다. |
| `OnDrawGizmosSelected` | On Selection | 해당 엔티티가 에디터에서 선택되었을 때만 호출됩니다. |

### 3.5. Coroutine Methods (코루틴)
비동기적 로직 흐름을 제어하기 위한 시스템입니다. 유니티와 유사한 `IEnumerator` 기반의 문법을 완벽히 지원합니다.

**자동 실행 기능:**
- `Start()` 메서드를 `IEnumerator` 타입으로 선언하면 엔진이 이를 자동으로 코루틴으로 실행합니다.
- 물리 이벤트(`OnTouched`, `OnDetected` 등)도 `IEnumerator`로 선언하여 즉시 코루틴을 시작할 수 있습니다.

**Control Methods:**
| Method | Description |
| :--- | :--- |
| `StartCoroutine(routine)`| 코루틴을 시작하고 `Coroutine` 객체를 반환합니다. |
| `StopCoroutine(coroutine)`| 실행 중인 특정 코루틴을 중단합니다. |
| `StopAllCoroutines()` | 해당 스크립트에서 실행 중인 모든 코루틴을 중단합니다. |

**Advanced Yield Instructions:**
| Instruction | Description |
| :--- | :--- |
| `yield return null` | 다음 논리 틱까지 대기를 지시합니다. |
| `yield return new WaitForSeconds(s)` | 지정된 초(s)만큼 대기합니다. |
| `yield return new WaitForTicks(n)` | 지정된 논리 틱(n)만큼 대기합니다. |
| `yield return new WaitForPhysicalTicks(n)` | 지정된 물리 틱(n)만큼 대기합니다. |
| `yield return new WaitUntil(() => condition)` | 조건이 `true`가 될 때까지 대기합니다. |
| `yield return new WaitWhile(() => condition)` | 조건이 `true`인 동안 대기합니다. |
| `yield return coroutine` | 다른 코루틴이 완료될 때까지 대기합니다. |
| `yield return routine` | 중첩된 `IEnumerator` 루틴이 완료될 때까지 대기합니다. |

---

## 4. Filter System (`Verity.Input.Filter`)

Verity Engine은 복잡한 조건 비교 로직을 데이터화하여 관리할 수 있는 **Filter** 시스템을 제공합니다. 필터는 코드에 하드코딩되는 복잡한 `if`문이나 `switch`문을 줄이고, 에디터에서 데이터 기반으로 게임 규칙을 동적으로 제어하기 위해 설계되었습니다.

### 4.1. 핵심 개념 (Core Concept)
필터는 **"여러 개의 값(Enum 또는 String)을 하나의 논리적 그룹으로 묶는 단위"**입니다.
예를 들어, "점프 가능한 모든 키"를 `JumpAction`이라는 이름의 필터로 묶거나, "플레이어가 충돌해야 하는 모든 레이어"를 `Obstacles` 필터로 묶을 수 있습니다.

### 4.2. Filter 구성 요소
- **Name**: 필터의 고유 식별자입니다. 코드에서 이 이름을 통해 필터를 참조합니다.
- **Mode (동작 모드)**:
    - **Whitelist**: 리스트에 **포함된** 값에 대해서만 `Check()` 결과가 `true`가 됩니다. (A OR B OR C...)
    - **Blacklist**: 리스트에 **포함되지 않은** 모든 값에 대해 `true`를 반환합니다. 특정 대상을 제외한 전체를 선택할 때 유용합니다.
- **Values**: 필터링 대상이 되는 실제 값들의 목록입니다.

### 4.3. Filter의 종류
1.  **단일 타입 필터 (Basic Filter)**:
    - 하나의 특정 Enum 타입(예: `KeyCode`)에 속한 값들만 가질 수 있습니다.
    - 주로 **입력 시스템(Input Mapping)**에서 여러 키를 하나의 동작으로 묶을 때 사용합니다.
2.  **혼합 타입 필터 (Mixed Filter)**:
    - 서로 다른 여러 타입의 Enum이나 문자열 값을 하나의 리스트에 담을 수 있습니다.
    - 주로 **물리 시스템(Collision Filtering)**이나 **태그 검사** 등 범용적인 분류에 사용됩니다.

### 4.4. 사용 예제 (Usage Examples)

#### 4.4.1. 입력 매핑 (Input Mapping)
에디터에서 `MoveRight`라는 필터를 만들고 `D`키와 `RightArrow`키를 추가했다면, 코드에서는 다음과 같이 간단하게 처리할 수 있습니다.

```csharp
using Verity.Input;

public class PlayerController : Script {
    void Update() {
        // 'MoveRight' 필터에 등록된 어떤 키라도 눌리면 true 반환
        if (Input.GetKey("MoveRight")) {
            Transform.Position += new Vector2(5 * Time.DeltaTime, 0);
        }
    }
}
```

#### 4.4.2. 물리 충돌 필터링 (Physics Masking)
특정 물리 그룹들만 골라서 충돌 검사를 수행하고 싶을 때 사용합니다.

```csharp
using Verity.Core.Physics;
using Verity.Input;

public class EnemyAI : Script {
    void Update() {
        // "Obstacles" 필터(MixedFilter)에 정의된 그룹들만 대상으로 레이캐스트나 오버랩 검사 수행
        // 예: "Wall" 그룹은 포함(Whitelist), "Ignore" 그룹은 제외 등
        var results = PhysicsManager.OverlapCircle(Transform.Position, 2.0f, "Obstacles");
        
        foreach (var entity in results) {
            Debug.Log($"Detected obstacle: {entity.Name}");
        }
    }
}
```

### 4.5. 데이터 저장 및 관리
- 모든 필터 데이터는 `Assets/Filters.json` 파일에 JSON 형식으로 직렬화되어 저장됩니다.
- 이 파일은 버전 관리 시스템(Git 등)을 통해 팀원 간에 쉽게 공유될 수 있으며, 텍스트 에디터로 직접 수정하는 것도 가능합니다.
- **Filter Registry**: 엔진 시작 시 모든 필터는 전역 레지스트리에 등록되어, 어디서든 이름만으로 즉시 접근할 수 있습니다.

---

## 5. Input System (`Verity.Input.Input`)

### 5.1. Unified Input
Verity에서는 마우스 버튼이 `KeyCode`로 통합되어 있습니다. 키보드 키와 마우스 버튼을 구분 없이 하나의 API로 처리할 수 있습니다.
*   `KeyCode.MouseLeft`, `KeyCode.MouseRight`, `KeyCode.MouseMiddle` 등

### 5.2. Action Mapping (Filter 활용)
특정 동작에 대해 여러 키를 바인딩하고 싶을 때 필터를 사용합니다. 에디터에서 필터를 만들고 여러 키를 등록하면, 코드에서는 이름 하나로 모든 입력을 감지합니다.

| Method | Description |
| :--- | :--- |
| `GetKey(key / name)` | 키/필터 입력이 유지되는 동안 `true`를 반환합니다. |
| `GetKeyDown(key / name)` | 키/필터 입력을 시작한 순간 한 번 `true`를 반환합니다. |
| `GetKeyUp(key / name)` | 키/필터 입력을 뗀 순간 한 번 `true`를 반환합니다. |

---

## 6. Core Data Types

### 6.1. Time (`Verity.Core.Engine.Time`)
시간 및 실행 횟수 관련 정적 클래스입니다.

| Property | Description |
| :--- | :--- |
| `DeltaTime` | 지난 Tick부터 현재 Tick까지 걸린 시간(초)입니다. |
| `FixedDeltaTime` | 고정 업데이트 간격입니다. (`FixedUpdate` 실행 주기) |
| `FrameCount` | 화면에 렌더링된 총 프레임 수입니다. |
| `LogicTickCount` | `Update`가 실행된 총 횟수입니다. |

---

## 7. Physics System (`Verity.Core.Physics`)

Verity 엔진의 물리 시스템은 실시간 충돌 판정 및 운동학적 시뮬레이션을 담당합니다.

### 7.1. Physical Component (`Verity.Core.Physics.Physical`)
물리 법칙이 적용되는 실체 컴포넌트입니다. (기존의 Rigidbody와 유사)

| Property | Type | Description |
| :--- | :--- | :--- |
| `Mass` | `float` | 객체의 질량입니다. |
| `Velocity` | `Vector2` | 현재 이동 속도입니다. |
| `AngularVelocity` | `float` | 현재 회전 속도입니다. |
| `Bounciness` | `float` | 탄성 계수 (0~1) 입니다. |
| `Friction` | `float` | 마찰 계수입니다. |
| `GravityScale` | `float` | 개별 중력 배율입니다. (기본 1.0) |
| `IsStatic` | `bool` | 고정 지형지물 여부입니다. (움직이지 않음) |
| `IsSensor` | `bool` | 물리적 충돌 없이 영역 감지만 할지 여부입니다. |
| `IsRotationLocked`| `bool` | 물리 연산에 의한 회전을 방지합니다. |

**Methods:**
| Method | Description |
| :--- | :--- |
| `Push(force)` | 객체에 순간적인 물리적인 힘(Impulse)을 가합니다. |
| `IsTouchingAnything()`| 현재 어떤 물체와든 닿아 있는지 여부를 반환합니다. |
| `GetTouchingEntities()`| 현재 닿아 있는 모든 엔티티 목록을 반환합니다. |

### 7.2. Physical Shapes
충돌 영역을 정의하는 컴포넌트들입니다. 모든 쉐이프는 `PhysicalShape`를 상속받습니다.

| Property | Type | Description |
| :--- | :--- | :--- |
| `Offset` | `Vector2` | 부착된 엔티티의 트랜스폼을 기준으로 한 충돌 영역의 상대 위치입니다. |
| `GroupName` | `string` | 해당 쉐이프가 속한 물리 그룹 이름입니다. |
| `IsSensor` | `bool` | 물리적 반발력 없이 충돌 감지만 수행할지 여부입니다. |

**Shape Types:**
*   **BoxShape**: `Size` 속성을 통해 사각형 충돌 영역을 정의합니다.
*   **CircleShape**: `Radius` 속성을 통해 원형 충돌 영역을 정의합니다.
*   **PolygonShape**: `List<Vector2>` 정점 배열을 통해 임의의 다각형 영역을 정의합니다.
    *   **SAT (Separating Axis Theorem)**: 정밀한 다각형 충돌 판정을 위해 SAT 알고리즘을 사용합니다.
    *   **Self-Intersection Safety**: 다각형의 선분이 서로 교차(Self-Intersect)하는 경우, 물리 버그 방지를 위해 해당 프레임의 물리 연산에서 제외됩니다.
    *   **Sync Logic**: 인스펙터의 "Sync With Renderer" 버튼을 통해 시각적 형태(`PolygonRenderer`)와 충돌 형태를 즉시 동기화할 수 있습니다.

### 7.3. Spatial Queries (`Verity.Core.Physics.PhysicsManager`)
특정 영역 내의 물리 객체들을 감지하기 위한 쿼리 기능을 제공합니다.

| Method | Description |
| :--- | :--- |
| `OverlapCircle(center, radius, mask)` | 지정된 원형 영역 내에 있는 모든 엔티티를 반환합니다. |
| `OverlapBox(center, size, mask)` | 지정된 사각형 영역 내에 있는 모든 엔티티를 반환합니다. |

### 7.3. Physics Optimization (`Verity.Core.Physics.SpatialHashGrid`)
Verity Engine은 수백 개의 물리 객체를 효율적으로 처리하기 위해 **Spatial Hashing** 기법을 사용합니다.
*   **작동 방식**: 월드를 고정된 크기의 격자(Grid)로 나누고, 객체가 속한 격자 키값을 해시맵에 저장하여 인접한 객체들만 충돌 검사 대상으로 선별합니다.
*   **성능**: 매 프레임 모든 객체를 전수 조사(O(N^2))하는 대신, 주변 객체만 조사(O(N))하므로 대규모 시뮬레이션에서도 안정적인 성능을 유지합니다.

---

## 8. Graphics & Camera

### 8.1. Sprite Renderer (`Verity.Graphics.SpriteRenderer`)
엔티티의 시각적 표현을 담당하는 컴포넌트입니다.

| Name | Type | Description |
| :--- | :--- | :--- |
| `Sprite` | `Sprite` | 렌더링할 스프라이트 에셋입니다. |
| `Color` | `Color` | 스프라이트에 적용할 곱셈 색상(Tint)입니다. |
| `FlipX` / `FlipY` | `bool` | 이미지를 가로 또는 세로로 반전시킵니다. |
| `OrderInLayer` | `int` | 동일한 레이어 내에서의 렌더링 순서입니다. (낮을수록 먼저 그림) |
| `SortingLayerName` | `string` | 렌더링 레이어의 이름입니다. |
| `Style` | `StyleAsset` | (Optional) 커스텀 셰이더와 파라미터를 적용하기 위한 스타일 에셋입니다. |

| Method | Description |
| :--- | :--- |
| `ApplyNativeAspectRatio()` | 텍스처의 원본 비율에 맞게 `Size`를 자동으로 조정합니다. |

### 8.2. Camera (`Verity.Graphics.Camera`)
월드를 비추는 카메라입니다. 모든 월드에는 최소 하나 이상의 활성화된 카메라가 필요합니다.

| Name | Type | Description |
| :--- | :--- | :--- |
| `Main` | `Camera?` | (Static) 현재 활성화된 월드의 메인 카메라를 반환합니다. |
| `Zoom` | `float` | 카메라 확대/축소 배율입니다. |
| `FixedAspectRatio` | `bool` | 고정 화면 비율(레터박스) 사용 여부입니다. |
| `AspectWidth / Height`| `float` | 고정할 화면 비율의 가로/세로 값입니다. |

### 8.3. Polygon Renderer (`Verity.Graphics.PolygonRenderer`)
선과 다각형을 렌더링하기 위한 컴포넌트입니다. 단순한 외곽선뿐만 아니라 내부 채우기 기능을 지원합니다.

| Name | Type | Description |
| :--- | :--- | :--- |
| `Vertices` | `List<Vector2>` | 다각형을 구성하는 정점 리스트입니다. (로컬 좌표계) |
| `Color` | `Color` | 다각형의 색상입니다. |
| `Fill` | `bool` | 다각형 내부를 색상으로 채울지 여부입니다. |
| `IsClosed` | `bool` | 마지막 정점과 첫 정점을 연결하여 닫힌 도형으로 만들지 여부입니다. |

**Advanced Rendering Features:**
*   **Ear Clipping Triangulation**: `Fill` 속성이 활성화되면 GPU 렌더링을 위해 **Ear Clipping** 알고리즘을 사용하여 임의의 단순 다각형(Simple Polygon)을 삼각형으로 분할합니다. 이를 통해 볼록(Convex) 및 오목(Concave) 다각형 모두 정상적으로 채워집니다.
*   **Intersection Handling**: 선분이 교차하여 형태가 꼬인 경우 렌더링이 중단되어 시각적 오류를 방지합니다.
*   **Shape Synchronization**: 
    *   **Auto-Sync**: 엔티티에 `PolygonShape`를 추가할 때, 이미 `PolygonRenderer`가 존재한다면 렌더러의 모양에 맞춰 자동으로 초기화됩니다.
    *   **Manual-Sync**: 인스펙터 버튼을 통해 쉐이프와 렌더러 간의 정점 데이터를 양방향으로 동기화할 수 있습니다. (쉐이프에는 "Sync With Renderer", 렌더러에는 "Sync With Shape" 버튼 제공)

---

## 9. Utilities

### 9.1. Debugging (`Verity.Core.Debug`)
| Method | Description |
| :--- | :--- |
| `Log(message)` | 일반 로그를 출력합니다. |
| `LogWarning(message)` | 경고 로그를 출력합니다. |
| `LogError(message)` | 오류 로그를 출력합니다. |
| `DrawLine(s, e, col, t)`| 월드 좌표에 선을 그립니다. |
| `DrawBox(ctr, sz, col, t)`| 월드 좌표에 상자를 그립니다. |

---

## 10. Blueprint System (Templates)

Blueprint는 엔티티 구성을 파일(`.blueprint`)로 저장하여 재사용하는 시스템입니다.

| Action | Description |
| :--- | :--- |
| **Save** | Hierarchy 우클릭 `Save as Blueprint` 또는 Project 창으로 드래그. |
| **Instantiate**| Project 창에서 월드로 드래그하거나 `InstantiateBlueprint` 코드 사용. |

---

## 11. Editor Features & Controls

### 11.1. Filter Editor Window
**Window > Filter Editor** 메뉴를 통해 프로젝트에서 사용할 모든 필터를 관리할 수 있습니다. 생성한 필터는 즉시 인스펙터와 `Input` API에서 사용할 수 있습니다.

### 11.2. Shortcuts
| Input | Action |
| :--- | :--- |
| **F Key / Double Click** | 선택된 엔티티로 부드럽게 화면 이동 및 줌 포커스. |
| **F2** | 선택된 엔티티/에셋 이름 변경. |
| **Ctrl + N** | 빈 엔티티 생성 또는 새 폴더 생성. |
| **W / E / R** | 이동 / 스케일 / 회전 도구 전환. |
| **Mouse Wheel** | 마우스 위치 중심 줌 조절 (보간 중단). |
| **Right Click Drag** | 월드 자유 이동 (Panning). |
| **Grid / Snap** | 동적 그리드 가이드 및 이동 스냅 기능. |

---

## 12. Branding & Customization

### 12.1. Editor & Build Logo
*   **에디터 로고**: `EditorResources/EditorLogo.png` 파일이 런처와 창 아이콘에 적용됩니다.
*   **빌드본 로고**: `BuildSettings.json`의 `LogoPath`에 지정된 파일이 사용됩니다.

---

## 13. Editor Internals (Advanced)

### 13.1. Undo/Redo System (`Verity.Editor.UndoSystem`)
에디터에서의 모든 편집 작업을 되돌리거나 다시 실행할 수 있는 시스템입니다.
*   **Snapshot-based**: 변경이 발생할 때마다 월드와 프로젝트 설정의 상태를 JSON 스냅샷으로 저장합니다.
*   **Continuous Action**: 기즈모 드래그와 같이 연속적인 변화가 일어나는 작업은 작업이 끝난 시점에 하나의 이력으로 통합하여 기록합니다.

### 13.2. Localization (L10n) (`Verity.Editor.L10n`)
에디터 UI의 다국어를 지원합니다.
*   **JSON-based**: `Locales/en.json`, `Locales/ko.json` 파일에 정의된 키-값 쌍을 로드합니다.
*   **Usage**: `L10n.Tr("Key")`를 통해 현재 설정된 언어에 맞는 텍스트를 즉시 반환합니다.

### 13.3. Script Compilation (`Verity.Editor.ScriptCompiler`)
에디터 내에서 C# 스크립트를 실시간으로 컴파일하여 DLL(`UserScripts.dll`)로 생성합니다.
*   **Roslyn API**: `Microsoft.CodeAnalysis`를 사용하여 에디터 실행 중에 사용자의 코드를 빌드하고 반영합니다.

## 14. Audio System (`Verity.Core.Audio`)

Verity 엔진의 오디오 시스템은 SDL_mixer 2.0을 기반으로 하며, ECS 아키텍처에 완전히 통합되어 씬별 독립적인 음향 환경과 실시간 공간 음향을 지원합니다.

### 14.1. Architecture & Lifecycle
오디오 시스템은 하드웨어 제어를 담당하는 로우레벨 시스템과 게임 로직을 담당하는 컴포넌트 계층으로 분리되어 있습니다.

*   **AudioSystem (Internal Static)**: SDL_mixer 장치 초기화 및 코덱 관리를 담당합니다. 엔진 시작 시 1회 호출되며, **WAV, OGG, MP3, FLAC, MOD** 등 주요 오디오 포맷의 디코딩을 활성화합니다.
*   **AudioManager (Script Component)**: 각 씬(World)에 하나씩 존재할 수 있는 오디오 제어 센터입니다. `Script`를 상속받아 엔진의 논리 틱(`Update`) 주기에 맞춰 공간 음향 수치를 갱신합니다.
    *   **Per-Scene Configuration**: 씬마다 독립적인 마스터 볼륨과 오디오 그룹 설정을 가질 수 있습니다.
    *   **Group Management**: `Master`, `BGM`, `SFX`, `UI` 등 논리적 그룹을 통해 여러 소스의 볼륨과 피치를 한꺼번에 제어합니다.
    *   **Voice Limiting (FIFO)**: 그룹별 `MaxVoices`를 설정하여 하드웨어 채널 낭비를 방지합니다. 제한 초과 시 가장 오래된 소리를 자동으로 정지시킵니다.

### 14.2. Audio Components
*   **AudioClip**: 로드된 사운드 에셋을 나타냅니다.
    *   `DefaultVolume / Pitch`: 에셋 자체에 기본값을 지정하여 여러 소스에서 재사용할 때 일관성을 유지합니다.
    *   `Preview()`: 게임 실행 없이 에디터에서 즉시 소리를 확인하는 기능을 제공합니다.
*   **AudioSource**: 월드 내의 특정 위치에서 소리를 출력하는 스피커 역할을 합니다.
    *   **PlayOnStart**: 컴포넌트가 활성화(Enable)되는 순간 자동으로 재생을 시작합니다.
    *   **Randomization**: 재생 시마다 `Min/Max Pitch` 및 `Volume` 범위 내에서 무작위 변조를 가해 반복적인 사운드의 단조로움을 해소합니다.
    *   **Mute**: 개별 소스 단위로 즉시 음소거가 가능합니다.
*   **AudioListener**: 소리를 수집하는 "귀"의 위치를 정의합니다. 보통 메인 카메라 엔티티에 부착하며, 월드에 활성화된 리스너가 없을 경우 공간 음향 효과가 중단됩니다.

### 14.3. Spatial Audio (공간 음향)
`AudioSource`의 `IsSpatial` 속성이 활성화되면, `AudioListener`와의 상대적 위치를 기반으로 실시간 음향 효과가 적용됩니다.
*   **Distance Attenuation (거리 감쇄)**: `MinDistance`와 `MaxDistance` 사이의 거리에 따라 볼륨이 0~255 단계로 감쇄됩니다.
*   **Stereo Panning (좌우 팬닝)**: 리스너를 기준으로 소스 엔티티의 X축 상대 위치를 계산하여 좌/우 스피커의 밸런스를 자동으로 조절합니다.

### 14.4. Serialization & Data Persistence
오디오 관련 모든 설정은 `.verity` 씬 파일 및 `.blueprint` 파일에 저장됩니다.
*   **Config Persistence**: 그룹별 볼륨, 피치, 뮤트 상태 및 오디오 소스의 모든 파라미터가 직렬화됩니다.
*   **Runtime Exclusion**: 현재 재생 중인 SDL 채널 ID나 활성 채널 큐(`ActiveChannels`)와 같은 런타임 데이터는 `[JsonIgnore]`를 통해 저장 대상에서 제외되어 데이터 무결성을 유지합니다.

### 14.5. Scripting API
| Method | Description |
| :--- | :--- |
| `AudioManager.Instance.Play(source)` | 오디오 소스의 설정값으로 재생을 시작합니다. |
| `AudioManager.Instance.StopGroup(name)` | 특정 그룹(예: "BGM")에 속한 모든 소리를 즉시 정지합니다. |
| `AudioManager.Instance.RemoveGroup(name)` | 동적으로 생성된 오디오 그룹을 제거하고 관련 채널을 정리합니다. |
| `audioSource.PlayOneShot(clip, scale)` | 현재 위치에서 클립을 중첩하여 1회 재생합니다. |

---

## 15. Animation System (`Verity.Core.Animation`)

Verity 엔진의 애니메이션 시스템은 키프레임 기반의 시퀀싱과 상태 머신(State Machine)을 결합하여 복잡한 객체 동작을 제어합니다. 리플렉션(Reflection)을 통해 엔진 내 모든 컴포넌트의 프로퍼티를 실시간으로 조작할 수 있도록 설계되었습니다.

### 15.1. Core Data Structures
애니메이션 데이터를 구성하는 핵심 클래스들입니다.

*   **AnimationClip**: 하나의 독립된 애니메이션 파일(`.anim`)을 나타냅니다.
    *   `Tracks`: 애니메이션되는 개별 프로퍼티 경로(예: `Transform.Position`)와 키프레임 리스트를 담고 있는 트랙 모음입니다.
    *   `Duration / Loop`: 애니메이션의 총 길이와 반복 여부를 설정합니다.
    *   `FrameRate`: 초당 프레임 수(FPS)를 정의하며, 에디터 타임라인의 눈금 기준이 됩니다.
*   **AnimationTrack**: 특정 컴포넌트의 특정 필드/프로퍼티를 시간에 따라 변화시키는 단위입니다.
    *   **Interpolation (보간)**: `float`, `int`, `Vector2`, `Color` 등 숫자 타입은 시간에 따라 부드럽게 선형 보간(Lerp)됩니다. `Sprite`, `bool`, `string` 등은 값이 즉시 변하는 Stepped 보간 방식을 사용합니다.
*   **Keyframe**: 특정 시점(`Time`)의 값(`Value`)을 저장하는 최소 단위입니다.
*   **AnimatorController**: 여러 애니메이션 상태와 전이(Transition) 조건을 관리하는 상태 머신 에셋입니다.
    *   `States`: `AnimatorState`들의 집합이며, 각 상태는 하나의 `AnimationClip`을 가집니다.
    *   `Parameters`: 스크립트에서 애니메이션 상태 변화를 제어하기 위한 변수(Float, Int, Bool)들을 정의합니다.

### 15.2. ECS Integration
애니메이션이 런타임에 실행되는 방식입니다.

*   **Animator Component**: 엔티티에 부착되어 실제 애니메이션 재생을 담당합니다.
    *   `Controller`: 재생할 애니메이션 상태 머신을 연결합니다.
    *   **Binding Cache**: 성능 최적화를 위해 애니메이션 트랙의 경로(Path)와 해당 컴포넌트/프로퍼티 정보를 캐싱하여 매 프레임 리플렉션 비용을 최소화합니다.
*   **AnimationSystem (Static)**: 전역 시스템으로, 매 논리 틱(`PerformLogicTick`)의 시작 부분에서 활성화된 모든 `Animator`를 업데이트합니다. 이는 게임 로직(`Update`)이 실행되기 전에 객체의 트랜스폼이나 시각적 상태를 최신 애니메이션 프레임으로 갱신하기 위함입니다.

### 15.3. Animation Editor Window
**Window > Animation** 메뉴를 통해 직관적인 애니메이션 제작 환경을 제공합니다.

*   **Timeline View**: 시간축에 따른 키프레임 배치를 시각적으로 확인하고 드래그하여 편집할 수 있습니다.
*   **Property Binding**: "Add Property" 버튼을 통해 엔티티에 부착된 모든 컴포넌트의 유효한 프로퍼티를 즉시 애니메이션 트랙으로 추가할 수 있습니다.
*   **Record Mode (REC)**: 녹화 버튼을 활성화한 상태에서 인스펙터 창의 값을 수정하면, 현재 타임라인 위치에 자동으로 키프레임이 생성됩니다.
*   **Real-time Preview**: 타임라인을 스크러빙(Scrubbing)하거나 재생 버튼을 눌러 에디터 뷰에서 즉시 결과를 확인할 수 있습니다.
*   **Sprite Animation Workflow**: 프로젝트 창의 이미지 에셋들을 타임라인으로 드래그 앤 드롭하면 자동으로 `SpriteRenderer.Sprite` 트랙이 생성되어 스프라이트 시트 애니메이션을 빠르게 제작할 수 있습니다.

### 15.4. Scripting API
스크립트에서 애니메이션을 제어하기 위한 주요 메서드입니다.

| Method | Description |
| :--- | :--- |
| `animator.Play(stateName)` | 지정된 이름의 애니메이션 상태를 즉시 실행합니다. |
| `animator.SetBool(name, val)` | 상태 머신의 불리언 파라미터 값을 설정합니다. |
| `animator.SetInt(name, val)` | 상태 머신의 정수형 파라미터 값을 설정합니다. |
| `animator.SetFloat(name, val)` | 상태 머신의 실수형 파라미터 값을 설정합니다. |


