# Verity 입력 문서

이 문서는 입력 폴링 API를 설명합니다.

범위는 다음과 같습니다.

- `Input`
- `KeyCode`
- `MouseButton`

Filter 기반 입력 묶음은 [Filter 문서](D:/Verity/Docs/Filter.md)에서 상세히 설명합니다.

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
