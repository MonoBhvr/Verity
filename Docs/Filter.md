# Filter System Architecture

Verity의 필터 시스템은 수많은 객체 그룹을 나노초 단위로 분류하고 판별하기 위해 **64비트 정수 연산**을 기반으로 설계된 고성능 아키텍처입니다.

---

## 🏗️ System Architecture

### 1. 64-bit Bitmask Mapping
전통적인 문자열 비교(`tag == "Player"`)나 리스트 순회는 비용이 큽니다. Verity는 모든 분류 값을 비트 공간에 매핑합니다.

- **Unique Bit Index**: 엔진 시작 시 모든 열거형(Enum) 값과 등록된 문자열 태그에 대해 0부터 63 사이의 고유한 비트 인덱스를 할당합니다.
- **Ulong Representation**: 하나의 필터는 64비트 `ulong` 숫자로 표현됩니다. 특정 값이 필터에 포함되어 있다면 해당 비트가 1로 켜진 상태입니다.
- **Bitwise Logic**: `(FilterMask & ObjectMask) != 0` 연산 하나만으로 객체가 특정 그룹에 속하는지 즉시 판별할 수 있습니다.

### 2. Mixed Type Unification (`MixedFilter`)
서로 다른 데이터 타입을 하나의 논리 그룹으로 묶기 위해 추상화 레이어를 제공합니다.

- **Type Agnostic**: `KeyCode`와 같은 입력 데이터와 `PhysicsGroup`과 같은 물리 데이터를 하나의 비트마스크 필드 내에서 병합할 수 있습니다.
- **Global Registry**: `FilterRegistry`는 프로젝트 전역에서 이 비트 인덱스들이 겹치지 않도록 관리하는 중앙 통제소 역할을 합니다.

### 3. Whitelist & Blacklist Logic
단순한 포함 관계를 넘어 필터의 동작 모드를 지원합니다.

- **Whitelist**: 마스크에 비트가 켜진 값들**만** 통과시킵니다.
- **Blacklist**: 마스크에 비트가 켜진 값들**을 제외한** 모든 값을 통과시킵니다. 이는 `~Mask` 연산을 통해 비트 수준에서 반전 처리되어 동일한 성능을 유지합니다.

---

## 📚 Filter API Reference

(기존 필터 API 명세 유지...)

### Filter (`Verity.Input.Filter`)
| Name | Type | Description |
| :--- | :--- | :--- |
| `Name` | `string` | 필터 식별자. |
| `Mask` | `ulong` | 64비트 결과 마스크. |

(이하 생략 - 이전 API 명세와 동일하게 유지)
