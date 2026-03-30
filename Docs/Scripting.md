# Scripting & Coroutine Architecture

Verity Engine의 스크립팅 시스템은 사용자가 작성한 C# 코드가 엔진의 핵심 틱 루프에 효율적으로 통합될 수 있도록 설계되었습니다.

---

## 🏗️ System Architecture

### 1. Reflection-Based Delegate Binding
성능을 위해 Verity는 매 프레임 리플렉션을 수행하지 않습니다. 대신, 컴포넌트가 활성화(Init)되는 시점에 딱 한 번 리플렉션을 수행합니다.

- **Caching**: `Awake`, `Start`, `Update` 등의 메서드를 런타임에 검색하여 타입에 맞는 `Action` 델리게이트로 캐싱합니다.
- **Fast Call**: 이후 엔진 루프에서는 리플렉션 없이 캐싱된 델리게이트를 직접 호출하므로 네이티브에 가까운 호출 속도를 제공합니다.
- **Method Discovery**: 사용자가 굳이 `override` 키워드를 쓰지 않아도 메서드 이름만 맞으면 자동으로 찾아내는 유연한 설계를 가지고 있습니다.

### 2. Coroutine State Machine (`IEnumerator`)
Verity의 코루틴은 Unity와 유사하게 C#의 `IEnumerator` 상태 머신을 활용하여 비동기적인 흐름을 제어합니다.

- **Wait Instruction**: `yield return`을 통해 반환되는 객체(`WaitForSeconds`, `WaitUntil` 등)에 따라 코루틴의 재개 시점을 결정합니다.
- **Manual Advancement**: 매 논리 틱마다 활성 코루틴 리스트를 순회하며 `MoveNext()`를 호출합니다. 조건이 충족되지 않은 코루틴은 대기 상태로 남습니다.
- **Lifecycle Integration**: 엔티티가 비활성화되거나 파괴될 때, 해당 스크립트에 속한 코루틴들도 함께 정리되어 메모리 누수를 방지합니다.

---

## 📚 Scripting API Reference

(기존 라이프사이클 및 코루틴 API 명세 유지...)

### 1. Lifecycle Methods
| Method | Return | Description |
| :--- | :--- | :--- |
| `Awake()` | `void` | 컴포넌트 생성 시 최초 1회 호출. |
| `Start()` | `void / IEnumerator` | 첫 `Update` 전 호출. `IEnumerator` 반환 시 자동 코루틴 시작. |

(이하 생략 - 이전 API 명세와 동일하게 유지)
