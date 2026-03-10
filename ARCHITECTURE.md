# Verity Engine Architecture & Scripting API Reference

Verity Engine은 C# 기반의 Entity-Component-System (ECS) 아키텍처를 따르는 2D 게임 엔진입니다. 유니티 엔진과 유사한 워크플로우를 제공하며, 직관적인 스크립팅 API를 제공합니다.

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

### 2.1. Entity (`Verity.Core.ECS.Entity`)
월드(World)에 존재하는 모든 객체의 기본 단위입니다.

| Name | Type | Description |
| :--- | :--- | :--- |
| `Id` | `Guid` | 엔티티의 고유 식별자입니다. |
| `Name` | `string` | 엔티티의 이름입니다. |
| `Active` | `bool` | 엔티티의 활성화 상태입니다. `false`일 경우 업데이트가 중지됩니다. |
| `Transform` | `Transform` | 위치, 회전, 크기 정보입니다. |
| `GetComponent<T>()` | Method | 엔티티에 부착된 특정 컴포넌트를 가져옵니다. |
| `AddComponent<T>()` | Method | 엔티티에 새로운 컴포넌트를 추가합니다. |

### 2.2. Component (`Verity.Core.ECS.Component`)
엔티티에 기능을 부여하는 모든 객체의 기본 클래스입니다.

| Name | Type | Description |
| :--- | :--- | :--- |
| `Owner` | `Entity` | 해당 컴포넌트가 부착된 엔티티입니다. |
| `Enabled` | `bool` | 컴포넌트의 활성화 여부입니다. 상태 변경 시 `OnEnable`/`OnDisable`이 호출됩니다. |

---

## 3. Scripting API (`Verity.Core.ECS.Script`)

사용자가 게임 로직을 작성할 때 상속받는 기본 클래스입니다. `private` 메서드로 선언해도 엔진이 자동으로 찾아 실행합니다.

### 3.1. Attributes (에디터 연동)
필드나 프로퍼티에 적용하여 에디터 인스펙터에서의 동작을 제어합니다.

| Attribute | Target | Description |
| :--- | :--- | :--- |
| `[SerializeField]` | Field/Property | `private` 변수를 에디터에 노출하고 직렬화(저장)합니다. |
| `[HideInInspector]`| Field/Property | `public` 변수를 에디터 창에서 숨깁니다. |
| `[Button("Label")]` | Method | 에디터 인스펙터에 해당 메서드를 실행하는 버튼을 생성합니다. |
| `[AssetReference]` | Field/Property | 특정 확장자의 파일(이미지 등)을 드래그 앤 드롭으로 연결할 수 있게 합니다. |

### 3.2. Lifecycle Callbacks (실행 순서)
모든 콜백은 매개변수가 없으며 반환 타입은 `void`입니다. (단, `Start`는 `IEnumerator` 반환 가능)

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
| `yield return new WaitUntil(() => condition)` | 조건이 `true`가 될 때까지 대기합니다. |
| `yield return new WaitWhile(() => condition)` | 조건이 `true`인 동안 대기합니다. |
| `yield return coroutine` | 다른 코루틴이 완료될 때까지 대기합니다. |
| `yield return routine` | 중첩된 `IEnumerator` 루틴이 완료될 때까지 대기합니다. |

**Coroutine Examples (예제):**
```csharp
// 1. 단순 시간 대기 예제
IEnumerator Start() {
    Debug.Log("5초 후에 색상을 변경합니다...");
    yield return new WaitForSeconds(5.0f);
    GetComponent<SpriteRenderer>().Color = Color.Red;
}

// 2. 조건부 대기 및 중첩 예제
IEnumerator OnTouched(Physical other) {
    Debug.Log("충돌 감지! 다른 코루틴을 실행합니다.");
    yield return StartCoroutine(FadeOut(2.0f));
    Debug.Log("페이드 아웃이 완료되었습니다.");
}

IEnumerator FadeOut(float duration) {
    float timer = 0;
    while (timer < duration) {
        timer += Time.DeltaTime;
        // ... 알파값 감소 로직 ...
        yield return null; // 한 프레임 대기
    }
}
```

---

## 4. Physics System (`Verity.Core.Physics`)

### 4.1. Physical Component
| Property | Type | Description |
| :--- | :--- | :--- |
| `Mass` | `float` | 객체의 질량입니다. |
| `Velocity` | `Vector2` | 현재 이동 속도입니다. |
| `AngularVelocity` | `float` | 현재 회전 속도입니다. |
| `IsStatic` | `bool` | 고정 지형지물 여부입니다. |
| `IsSensor` | `bool` | 물리적 충돌 없이 영역 감지만 할지 여부입니다. |
| `Push(force)` | Method | 객체에 물리적인 힘을 가합니다. |

---

## 5. Input System (`Verity.Input.Input`)

### 5.1. Action Mapping & Direct Input
| Method | Description |
| :--- | :--- |
| `GetKey(key / name)` | 키가 눌려 있는 동안 `true`를 반환합니다. |
| `GetKeyDown(key / name)` | 키를 누른 순간 한 번 `true`를 반환합니다. |
| `GetKeyUp(key / name)` | 키를 뗀 순간 한 번 `true`를 반환합니다. |

---

## 6. Utilities

### 6.1. Debugging (`Verity.Core.Debug`)
| Method | Description |
| :--- | :--- |
| `Log(message)` | 일반 로그를 출력합니다. |
| `LogWarning(message)` | 경고 로그를 출력합니다. |
| `LogError(message)` | 오류 로그를 출력합니다. |
| `DrawLine(s, e, col, t)`| 월드 좌표에 선을 그립니다. |
| `DrawBox(ctr, sz, col, t)`| 월드 좌표에 상자를 그립니다. |

### 6.2. Transform Operations (`Verity.Core.ECS.Transform`)
| Property / Method | Description |
| :--- | :--- |
| `Position / Rotation / Scale` | 지역(Local) 좌표, 회전, 크기입니다. |
| `WorldPosition / WorldRotation / WorldScale` | 전역(World) 좌표, 회전, 크기입니다. |
| `SetParent(parent, preservePos)` | 부모 엔티티를 설정합니다. |
| `Children` | 자식 트랜스폼 목록을 가져옵니다. |

---

## 7. Blueprint System (Templates)

Blueprint는 엔티티 구성을 파일(`.blueprint`)로 저장하여 재사용하는 시스템입니다.

| Action | Description |
| :--- | :--- |
| **Save** | Hierarchy 우클릭 `Save as Blueprint` 또는 Project 창으로 드래그. |
| **Instantiate**| Project 창에서 월드로 드래그하거나 `InstantiateBlueprint` 코드 사용. |

---

## 8. Editor Features & Controls

| Input | Action |
| :--- | :--- |
| **F Key / Double Click** | 선택된 엔티티로 부드럽게 화면 이동 및 줌 포커스. |
| **Mouse Wheel** | 마우스 위치 중심 줌 조절 (보간 중단). |
| **Right Click Drag** | 월드 자유 이동 (Panning). |
| **Grid / Snap** | 동적 그리드 가이드 및 이동 스냅 기능. |
