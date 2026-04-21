# Verity 입력 문서

이 문서는 입력 폴링 API를 설명합니다.

범위는 다음과 같습니다.

- `Input`
- `KeyCode`
- `MouseButton`

Filter 기반 입력 묶음은 [Filter 문서](./Filter.md)에서 상세히 설명합니다.

---

## 1. 입력 시스템 개요

Verity 입력 시스템은 render frame이 아니라 logic tick 기준으로 입력 상태를 정리합니다.

### 존재 이유

- 입력 의미를 스크립트 루프와 일치시키기 위해
- `Pressed`, `Released` 같은 edge-trigger 상태를 안정적으로 제공하기 위해

입력 이벤트는 SDL 이벤트로 수집되고, `NewLogicTick()` 시점에 “이번 tick에서 눌렸는지/떼졌는지” 상태로 고정됩니다.

---

## 2. `Input`

`Input`은 정적 입력 상태 접근점입니다.

### 프로퍼티

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Enabled` | `bool` | 입력 시스템 활성 여부 |
| `MousePosition` | `Vector2` | 현재 마우스 위치 |
| `MouseDelta` | `Vector2` | 이전 logic tick 대비 이동량 |
| `ScrollDelta` | `float` | 이번 tick의 휠 delta |
| `AnyKey` | `KeyCode` | 현재 눌린 키 중 하나 |
| `AnyMouseButton` | `MouseButton` | 현재 눌린 마우스 버튼 중 하나 |
| `AnyKeyDown` | `bool` | 이번 tick에 아무 키나 눌렸는지 |

### 키 관련 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `bool Down(KeyCode key)` | 현재 눌림 상태 |
| `bool Pressed(KeyCode key)` | 이번 tick에 눌렸는지 |
| `bool Released(KeyCode key)` | 이번 tick에 떼졌는지 |
| `bool Down(Filter? filter)` | filter 기준 눌림 검사 |
| `bool Pressed(Filter? filter)` | filter 기준 눌림 검사 |
| `bool Released(Filter? filter)` | filter 기준 떼짐 검사 |
| `bool Down(string filterName)` | 이름으로 등록된 filter 기준 검사 |
| `bool Pressed(string filterName)` | 이름 기반 눌림 검사 |
| `bool Released(string filterName)` | 이름 기반 떼짐 검사 |

### 마우스 관련 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `bool MouseDown(MouseButton button)` | 현재 눌림 상태 |
| `bool MousePressed(MouseButton button)` | 이번 tick에 눌렸는지 |
| `bool MouseReleased(MouseButton button)` | 이번 tick에 떼졌는지 |
| `bool MouseDown(Filter? filter)` | filter 기준 눌림 검사 |
| `bool MousePressed(Filter? filter)` | filter 기준 눌림 검사 |
| `bool MouseReleased(Filter? filter)` | filter 기준 떼짐 검사 |
| `bool MouseDown(string filterName)` | 이름 기반 검사 |
| `bool MousePressed(string filterName)` | 이름 기반 눌림 검사 |
| `bool MouseReleased(string filterName)` | 이름 기반 떼짐 검사 |

### 시스템 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `void NewLogicTick()` | 버퍼를 이번 tick 상태로 확정 |
| `void ProcessEvent(SDL.SDL_Event evt)` | SDL 이벤트 입력 |
| `void Reset()` | 입력 상태 초기화 |
| `void BeginFrame()` | obsolete |
| `void EndFrame()` | obsolete |

### obsolete / 마이그레이션 가이드

`Input`에는 이전 이름 체계를 유지하기 위한 obsolete 메서드가 남아 있습니다. 새 코드에서는 아래 대체 API를 사용하세요.

#### 1) polling 이름 변경

- `GetKey(...)` → `Down(...)`
- `GetKeyDown(...)` → `Pressed(...)`
- `GetKeyUp(...)` → `Released(...)`
- `GetMouseButton(...)` → `MouseDown(...)`
- `GetMouseButtonDown(...)` → `MousePressed(...)`
- `GetMouseButtonUp(...)` → `MouseReleased(...)`

이 치환 규칙은 `KeyCode`, `MouseButton`, `Filter`, `string filterName` 오버로드에 동일하게 적용됩니다.

```csharp
// 이전 방식
if (Input.GetKey(KeyCode.Space))
{
    Jump();
}

if (Input.GetKeyDown("PlayerJump"))
{
    Jump();
}

if (Input.GetMouseButtonUp(MouseButton.Left))
{
    Fire();
}

// 대체 방식
if (Input.Down(KeyCode.Space))
{
    Jump();
}

if (Input.Pressed("PlayerJump"))
{
    Jump();
}

if (Input.MouseReleased(MouseButton.Left))
{
    Fire();
}
```

#### 2) `BeginFrame()` / `EndFrame()` 대체

`BeginFrame()`와 `EndFrame()`은 더 이상 입력 상태를 갱신하지 않으며, `[Obsolete("Use NewLogicTick instead")]`로 표시되어 있습니다.

- deprecated: `BeginFrame()`, `EndFrame()`
- 대체 API: `NewLogicTick()`
- 호출 시점: render frame 기준이 아니라 logic tick 시작 시점

```csharp
// 이전 방식
Input.BeginFrame();
// 게임 로직 처리
Input.EndFrame();

// 대체 방식
Input.NewLogicTick();
// 이번 logic tick 기준으로 입력 처리
```

핵심은 입력 edge 상태(`Pressed`, `Released`, `MousePressed`, `MouseReleased`)가 render frame이 아니라 `NewLogicTick()` 호출 시점에 확정된다는 점입니다. 따라서 입력 갱신 루프를 프레임 시작/종료 훅에서 관리하던 기존 코드는 logic tick 진입 지점으로 옮겨야 합니다.

### 구현상 중요한 규칙

- `Enabled`가 false가 되면 눌림/버퍼 상태가 초기화됩니다.
- 마우스 버튼은 `KeyCode.MouseLeft` 같은 통합 키코드로도 반영됩니다.

---

## 3. `KeyCode`

`KeyCode`는 키보드와 일부 마우스 입력을 통합해서 표현하는 enum입니다.

### 주요 범주

- `A` ~ `Z`
- `Alpha0` ~ `Alpha9`
- `F1` ~ `F12`
- `Space`, `Return`, `Escape`, `Backspace`, `Tab`, `Delete`
- `UpArrow`, `DownArrow`, `LeftArrow`, `RightArrow`
- `LeftShift`, `RightShift`, `LeftCtrl`, `RightCtrl`, `LeftAlt`, `RightAlt`
- `MouseLeft`, `MouseRight`, `MouseMiddle`, `MouseX1`, `MouseX2`

### 존재 이유

- 키보드와 마우스 버튼을 하나의 입력 도메인으로 다루는 filter를 만들 수 있게 하기 위해

---

## 4. `MouseButton`

| 값 | 의미 |
| :--- | :--- |
| `Left` | 좌클릭 |
| `Middle` | 휠 클릭 |
| `Right` | 우클릭 |
| `X1` | 추가 버튼 1 |
| `X2` | 추가 버튼 2 |

### 존재 이유

- 마우스 전용 API에서는 키보드와 분리된 명시적 타입이 더 읽기 쉽기 때문입니다.
