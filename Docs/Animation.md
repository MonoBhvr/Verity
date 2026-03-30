# Verity Animation API Reference

`Verity.Core.ECS.Animator` 컴포넌트와 관련 에셋들을 사용하여 객체의 애니메이션 상태와 전이 로직을 제어합니다.

---

## 1. Animator Component
엔티티에 부착되어 실제 애니메이션 재생과 상태 머신을 담당합니다.

### Properties
| Name | Type | Description |
| :--- | :--- | :--- |
| `Controller` | `AnimatorController` | 애니메이션 상태와 전이 조건이 정의된 에셋입니다. |
| `Speed` | `float` | 애니메이션 전체 재생 속도입니다. (1.0이 기본) |
| `IsPlaying` | `bool` | 현재 애니메이션이 재생 중인지 여부입니다. (Read-only) |
| `CurrentTime` | `float` | 현재 애니메이션 재생 시간입니다. (초 단위) |
| `CurrentStateName` | `string` | 현재 활성화된 애니메이션 상태의 이름입니다. |

### Control Methods
| Method | Description |
| :--- | :--- |
| `Play(stateName, restart)` | 지정된 이름의 애니메이션 상태를 즉시 실행합니다. `restart`가 true이면 이미 실행 중이어도 처음부터 다시 재생합니다. |
| `Stop()` | 애니메이션 재생을 중단하고 시간을 초기화합니다. |

### Parameter Methods
상태 머신의 전이(Transition) 조건으로 사용되는 변수들을 제어합니다.
| Method | Description |
| :--- | :--- |
| `SetFloat(name, val)` | 실수형(float) 파라미터 값을 설정합니다. |
| `SetInt(name, val)` | 정수형(int) 파라미터 값을 설정합니다. |
| `SetBool(name, val)` | 불리언(bool) 파라미터 값을 설정합니다. |
| `SetTrigger(name)` | 트리거(trigger) 파라미터를 활성화합니다. 전이가 발생하면 자동으로 초기화됩니다. |
| `ResetTrigger(name)` | 활성화된 트리거 파라미터를 수동으로 초기화합니다. |

---

## 2. AnimatorController (Asset)
애니메이션 상태와 파라미터를 정의하는 데이터 구조입니다.

### Data Structures
- **States**: `AnimatorState` 목록으로, 각 상태는 하나의 `AnimationClip`과 연결됩니다.
- **Transitions**: 상태 간 이동 조건을 정의합니다. (`ExitTime`, `Conditions`)
- **Default State**: 컨트롤러 로드 시 자동으로 시작되는 기본 상태입니다.

---

## 3. AnimationClip (Asset)
시간에 따른 컴포넌트 프로퍼티의 변화를 담고 있는 파일입니다.

### Properties
| Name | Type | Description |
| :--- | :--- | :--- |
| `Duration` | `float` | 애니메이션의 총 길이(초)입니다. |
| `Loop` | `bool` | 애니메이션을 반복해서 재생할지 여부입니다. |
| `Tracks` | `List<Track>` | 애니메이션되는 개별 프로퍼티 경로(예: `Transform.Position`) 정보입니다. |

### Methods
| Method | Return | Description |
| :--- | :--- | :--- |
| `Evaluate(time)` | `object` | 특정 시점의 보간된 데이터를 계산하여 반환합니다. |
