# Verity 스크립팅 문서

이 문서는 현재 Verity에 구현되어 있는 게임플레이 스크립팅 API를 설명합니다.

범위는 다음과 같습니다.

- lifecycle
- coroutine 동작
- physics callback
- 정적 shortcut API
- 스크립트에서의 UI 접근

---

## 1. Script 모델

게임플레이 스크립트는 `Script`를 상속하며, `Script`는 다시 `Component`를 상속합니다.

`Script`가 존재하는 이유는 다음과 같습니다.

- lifecycle callback 제공
- physics callback 제공
- coroutine 지원
- 자주 쓰는 엔진 접근 경로에 대한 정적 편의 API 제공

---

## 2. Lifecycle 바인딩

Verity는 스크립트가 생성될 때 lifecycle 메서드를 이름으로 해석하고, delegate를 미리 바인딩합니다.

즉 현재 구조는 다음과 같습니다.

1. 초기화 시 reflection을 한 번 수행
2. 스크립트 인스턴스에 delegate를 캐시
3. 실제 tick에서는 delegate를 직접 호출

지원되는 lifecycle 메서드는 다음과 같습니다.

- `Awake()`
- `Start()`
- `FixedUpdate()`
- `Update()`
- `LateUpdate()`
- `OnDrawGizmos()`
- `OnDrawGizmosSelected()`

규칙은 다음과 같습니다.

- 메서드는 파라미터가 없어야 합니다.
- 반환형은 `void`일 수 있습니다.
- `Start()`는 `IEnumerator`를 반환할 수도 있으며, 이 경우 자동으로 coroutine으로 시작됩니다.

---

## 3. Physics Callback

현재 지원되는 physics callback은 다음과 같습니다.

- `OnTouched(Physical other)`
- `OnTouching(Physical other)`
- `OnTouchEnd(Entity other)`
- `OnDetected(Entity other)`
- `OnDetecting(Entity other)`
- `OnDetectEnd(Entity other)`

이 메서드들은 다음 반환형을 가질 수 있습니다.

- `void`
- `IEnumerator`

physics callback이 `IEnumerator`를 반환하면 자동으로 coroutine으로 시작됩니다.

---

## 4. Coroutine

`Script`는 coroutine 관리를 포함합니다.

사용 가능한 API는 다음과 같습니다.

- `Coroutine StartCoroutine(IEnumerator routine)`
- `void StopCoroutine(Coroutine coroutine)`
- `void StopAllCoroutines()`

지원되는 대기 명령은 다음과 같습니다.

- `WaitForSeconds`
- `WaitForTicks`
- `WaitForPhysicalTicks`
- `WaitUntil`
- `WaitWhile`

Coroutine은 render frame이 아니라 logic tick 기준으로 전진합니다.

---

## 5. 정적 Shortcut API

`Script`는 자주 쓰는 게임플레이 작업을 위한 정적 helper를 제공합니다.

### 엔티티 검색과 생성/파괴

- `Entity? Find(string name)`
- `Entity? FindWithTag(string tag)`
- `Entity[] FindEntitiesWithTag(string tag)`
- `T? FindObjectOfType<T>(bool includeInactive = false) where T : Component`
- `T[] FindObjectsOfType<T>(bool includeInactive = false) where T : Component`
- `void Destroy(Entity entity)`
- `void Destroy(Component component)`
- `Entity Instantiate(string name = "New Entity")`
- `Entity? Instantiate(Entity original)`
- `T? Instantiate<T>(T original) where T : Component`

이 API들은 편의성은 높지만, 전역 검색 계열은 hot path에서 남용하지 않는 편이 맞습니다.

---

## 6. 스크립트에서의 UI 접근

스크립트의 UI 접근은 현재 UI 아키텍처와 직접 연결되어 있습니다.

핵심 원칙은 다음과 같습니다.

- 게임플레이 스크립트는 가능하면 화면 단위로 접근합니다.
- UI 노드 직접 접근은 존재하지만 기본 경로가 되어서는 안 됩니다.

### 노드와 Canvas 검색

- `Canvas? FindCanvas(string screenNameOrId)`
- `UiNode? FindUi(string nameOrId)`
- `T? FindUi<T>(string nameOrId) where T : UiNode`

### 역할 기반 화면 접근

- `Canvas? OpenUiRole(string role)`
- `Canvas? FindUiRole(string role)`
- `void CloseUiRole(string role)`
- `void SetUiRole(string role, string variable, object? value)`
- `void SendUiRole(string role, string command, object? payload = null)`

### 직접 화면 핸들 접근

- `void SetUi(string screenNameOrId, string variable, object? value)`
- `void SendUi(string screenNameOrId, string command, object? payload = null)`

현재 권장 방식은 구체적인 화면 이름보다 역할 기반 접근입니다.

예:

- `OpenUiRole("Hud")`
- `SetUiRole("Hud", "Health", hp)`
- `SendUiRole("Inventory", "OpenTab", "Equipment")`

즉 스크립트는 UI 내부 노드를 직접 만지기보다, 화면 변수와 command를 통해 상호작용하는 편이 맞습니다.
