# Verity Input API Reference

## 1. Static Methods (Keyboard & Mouse)

### Key State
| Method | Return | Description |
| :--- | :--- | :--- |
| `GetKey(KeyCode / filterName)` | `bool` | 키가 눌려 있는 상태인지 확인합니다. |
| `GetKeyDown(KeyCode / filterName)`| `bool` | 이번 프레임에 키가 눌렸는지 확인합니다. |
| `GetKeyUp(KeyCode / filterName)` | `bool` | 이번 프레임에 키가 떨어졌는지 확인합니다. |

### Mouse State
| Method | Return | Description |
| :--- | :--- | :--- |
| `GetMouseButton(MouseButton / filterName)` | `bool` | 마우스 버튼이 눌려 있는지 확인합니다. |
| `GetMouseButtonDown(MouseButton / filterName)`| `bool` | 이번 프레임에 버튼이 눌렸는지 확인합니다. |
| `GetMouseButtonUp(MouseButton / filterName)` | `bool` | 이번 프레임에 버튼이 떨어졌는지 확인합니다. |

---

## 2. Static Properties
- `MousePosition`: 화면 공간의 현재 마우스 좌표.
- `MouseDelta`: 이전 프레임 대비 마우스 이동량.
- `ScrollDelta`: 이번 프레임의 휠 스크롤 량.
- `AnyKeyDown`: 아무 키나 새로 눌렸는지 여부.
- `AnyKey`: 현재 눌려 있는 키 중 하나를 반환.
- `Enabled`: 입력 시스템 활성화 여부.

---

## 3. KeyCode & MouseButton
- **KeyCode**: 마우스 버튼을 포함한 통합 열거형. (`MouseLeft`, `MouseRight`, `A`~`Z`, `Escape` 등)
- **MouseButton**: 마우스 전용 타입. (`Left`, `Right`, `Middle`, `X1`, `X2`)
