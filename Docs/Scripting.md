# Verity 스크립팅 문서

이 문서는 `Script` 기반 스크립팅 모델을 설명합니다.

범위는 다음과 같습니다.

- lifecycle 메서드와 바인딩 규칙
- coroutine 동작 방식
- physics 이벤트 라우팅
- 스크립트가 직접 쓰는 shortcut API

---

## 1. 스크립팅 모델 개요

Verity의 스크립트는 `Component`를 상속하는 `Script` 타입 위에서 동작합니다.

### 왜 별도 `Script` 타입이 필요한가

모든 컴포넌트가 lifecycle과 coroutine을 가질 필요는 없습니다. 렌더러나 순수 데이터 컴포넌트까지 같은 비용과 규칙을 적용하면 불필요하게 무거워집니다. 그래서 Verity는 “일반 컴포넌트”와 “실행 가능한 스크립트 컴포넌트”를 분리합니다.

---

## 2. Lifecycle 바인딩 구조

스크립트는 이름 기반 메서드를 지원합니다. 하지만 매 tick마다 reflection으로 메서드를 찾으면 느립니다. 현재 구조는 다음과 같습니다.

1. 스크립트 생성 시 reflection으로 메서드를 한 번 찾음
2. 찾은 메서드를 delegate로 바인딩
3. 이후 루프에서는 delegate를 직접 호출

이 설계의 존재 이유는 다음과 같습니다.

- 사용자 입장에서는 Unity 스타일 이름 기반 메서드 사용 가능
- 런타임 입장에서는 반복 reflection 비용 제거

### lifecycle 메서드 목록

| 메서드 | 호출 시점 | 존재 이유 |
| :--- | :--- | :--- |
| `Awake()` | 최초 초기화 직후 한 번 | 컴포넌트 참조 캐시, 초기 내부 상태 구성 |
| `Start()` | 첫 활성 tick 직전 한 번 | 실제 실행 시작 시점의 초기화 |
| `FixedUpdate()` | 매 logic tick | 고정 주기 로직 |
| `Update()` | 매 logic tick | 일반 게임 로직 |
| `LateUpdate()` | coroutine 이후 | 다른 스크립트 결과 반영용 후처리 |
| `OnDrawGizmos()` | gizmo 렌더 시 | 디버그 시각화 |
| `OnDrawGizmosSelected()` | 선택 상태 gizmo 렌더 시 | 선택 대상 전용 디버그 표시 |

### 바인딩 규칙

- lifecycle 메서드는 파라미터가 없어야 합니다.
- 반환형은 `void` 또는 `IEnumerator`만 허용됩니다.
- `Start()`만 coroutine 반환을 별도로 허용하는 것이 아니라, 지원되는 lifecycle/physics 메서드는 `IEnumerator` 반환 시 자동 coroutine 시작 규칙을 탑니다.

---

## 3. Physics 이벤트 메서드

스크립트는 물리 접촉 상태에 반응할 수 있습니다.

| 메서드 | 파라미터 | 호출 조건 |
| :--- | :--- | :--- |
| `OnTouched(Physical other)` | 상대 physical | 첫 비-sensor 접촉 프레임 |
| `OnTouching(Physical other)` | 상대 physical | 접촉 지속 중 |
| `OnTouchEnd(Entity other)` | 상대 엔티티 | 비-sensor 접촉 종료 시 |
| `OnDetected(Entity other)` | 상대 엔티티 | 첫 sensor 감지 프레임 |
| `OnDetecting(Entity other)` | 상대 엔티티 | sensor 감지 지속 중 |
| `OnDetectEnd(Entity other)` | 상대 엔티티 | sensor 감지 종료 시 |

### 존재 이유

- 물리 충돌과 sensor 감지를 분리해 스크립트 의미를 분명히 하기 위해
- `Physical` 자체와 엔티티 식별이 필요한 경우를 구분하기 위해

### 구현상 중요한 규칙

- sensor 여부는 이벤트 대상 엔티티의 첫 번째 enabled `PhysicalShape` 기준으로 판정됩니다.
- physics 이벤트도 `IEnumerator`를 반환할 수 있고, 이 경우 자동으로 coroutine이 시작됩니다.

---

## 4. Coroutine 모델

Verity의 coroutine은 `IEnumerator` 상태 머신을 감싸는 `Coroutine` 래퍼 위에서 동작합니다.

### `Coroutine`

| 항목 | 형식 | 설명 |
| :--- | :--- | :--- |
| 생성자 | `Coroutine(IEnumerator routine)` | 새 coroutine 래퍼 생성 |

### 지원되는 wait instruction

| 타입 | 핵심 값 | 존재 이유 |
| :--- | :--- | :--- |
| `WaitForSeconds` | `Seconds` | 시간 기반 대기 |
| `WaitForTicks` | `Ticks` | logic tick 수 기준 대기 |
| `WaitForPhysicalTicks` | `Ticks` | physics tick 수 기준 대기 |
| `WaitUntil` | `Predicate` | 조건 만족까지 대기 |
| `WaitWhile` | `Predicate` | 조건이 참인 동안 대기 |

### wait 타입 상세

#### `WaitForSeconds`

- 프로퍼티
  - `float Seconds`
- 생성자
  - `WaitForSeconds(float seconds)`

#### `WaitForTicks`

- 프로퍼티
  - `int Ticks`
- 생성자
  - `WaitForTicks(int ticks)`

#### `WaitForPhysicalTicks`

- 프로퍼티
  - `int Ticks`
- 생성자
  - `WaitForPhysicalTicks(int ticks)`

#### `WaitUntil`

- 프로퍼티
  - `Func<bool> Predicate`
- 생성자
  - `WaitUntil(Func<bool> predicate)`

#### `WaitWhile`

- 프로퍼티
  - `Func<bool> Predicate`
- 생성자
  - `WaitWhile(Func<bool> predicate)`

### coroutine이 기다릴 수 있는 대상

- 위 wait instruction 객체
- 다른 `Coroutine`
- 중첩 `IEnumerator`

### 중요한 동작 규칙

- coroutine은 render frame이 아니라 logic tick 기준으로 전진합니다.
- `WaitForTicks`는 `Time.LogicTickCount`를 기준으로 계산됩니다.
- `WaitForPhysicalTicks`는 `Time.PhysicsTickCount`를 기준으로 계산됩니다.
- 중첩 `IEnumerator`는 별도 스케줄러를 만들지 않고 현재 coroutine 안에서 직접 전진합니다.

---

## 5. `Script` API 레퍼런스

## 5.1 타입 개요

`Script`는 스크립트 실행 기능을 가진 컴포넌트 베이스 클래스입니다.

### 존재 이유

- 일반 컴포넌트와 실행형 로직 컴포넌트를 분리하기 위해
- lifecycle, physics event, coroutine 기능을 한 곳에 모으기 위해

### 주요 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `Coroutine StartCoroutine(IEnumerator routine)` | coroutine 시작 |
| `void StopCoroutine(Coroutine coroutine)` | 특정 coroutine 중지 예약 |
| `void StopAllCoroutines()` | 모든 coroutine 즉시 비우기 |

### override 가능한 lifecycle 메서드

- `virtual void Awake()`
- `virtual void Start()`
- `virtual void Update()`
- `virtual void FixedUpdate()`
- `virtual void LateUpdate()`
- `virtual void OnDrawGizmos()`
- `virtual void OnDrawGizmosSelected()`

### override 가능한 physics 메서드

- `virtual void OnTouched(Physical other)`
- `virtual void OnTouching(Physical other)`
- `virtual void OnTouchEnd(Entity other)`
- `virtual void OnDetected(Entity other)`
- `virtual void OnDetecting(Entity other)`
- `virtual void OnDetectEnd(Entity other)`

### 정적 shortcut API

| 시그니처 | 설명 |
| :--- | :--- |
| `static Entity? Find(string name)` | 엔티티 이름 검색 |
| `static Entity? FindWithTag(string tag)` | 태그 검색 |
| `static Entity[] FindEntitiesWithTag(string tag)` | 태그 다건 검색 |
| `static T? FindObjectOfType<T>(bool includeInactive = false) where T : Component` | 컴포넌트 전역 검색 |
| `static T[] FindObjectsOfType<T>(bool includeInactive = false) where T : Component` | 컴포넌트 전역 다건 검색 |
| `static void Destroy(Entity entity)` | 엔티티 파괴 예약 |
| `static void Destroy(Component component)` | 컴포넌트 제거 |
| `static Entity Instantiate(string name = "New Entity")` | 새 엔티티 생성 |
| `static Entity? Instantiate(Entity original)` | 엔티티 복제 |
| `static T? Instantiate<T>(T original) where T : Component` | 컴포넌트 포함 엔티티 복제 |
| `static Canvas? FindCanvas(string screenNameOrId)` | 캔버스 검색 |
| `static UiNode? FindUi(string nameOrId)` | UI 노드 검색 |
| `static T? FindUi<T>(string nameOrId) where T : UiNode` | 타입 지정 UI 노드 검색 |
| `static Canvas ShowUiScreen(UIScreenAsset screen)` | UI 스크린 표시 |
| `static Canvas ShowUiScreen(string path, string? guid = null)` | 경로 기준 UI 스크린 표시 |

### 구현상 중요한 규칙

- `OnDestroy()` 기본 구현은 `StopAllCoroutines()`를 호출합니다.
- `StopCoroutine()`는 보통 즉시 리스트를 뜯지 않고 안전한 제거 큐를 사용합니다.
- shortcut API는 편리하지만 대부분 전역 검색이므로 남용하면 비쌉니다.

---

## 6. 실전 사용 지침

### 6.1 이런 경우에 `Awake`를 사용

- 같은 엔티티 내부 컴포넌트 캐시
- 초기 내부 리스트/딕셔너리 생성
- 런타임 중 변하지 않을 참조 구성

### 6.2 이런 경우에 `Start`를 사용

- 다른 엔티티 검색이 필요한 초기화
- world가 이미 활성화된 뒤 동작해야 하는 초기화
- 첫 프레임에만 실행할 coroutine 시작

### 6.3 성능상 피해야 할 패턴

- 매 tick마다 `FindObjectOfType`
- 매 tick마다 부모 체인/자식 트리 탐색
- coroutine 내부에서 짧은 간격으로 전역 검색 반복

