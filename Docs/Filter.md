# Verity 필터 시스템 문서

이 문서는 입력, 물리 그룹, sorting layer 등에서 공용으로 쓰는 filter 시스템을 설명합니다.

범위는 다음과 같습니다.

- `FilterMode`
- `FilterValue`
- `Filter`
- `MixedFilter`
- `FilterRegistry`

---

## 1. 필터 시스템 개요
    
Verity의 filter 시스템은 64비트 비트마스크 기반입니다.

### 왜 이런 구조가 필요한가

문자열 비교나 리스트 탐색으로 그룹을 검사하면 반복 연산 비용이 커집니다. 반면 비트마스크는 다음처럼 매우 낮은 비용으로 판정할 수 있습니다.

- `maskA & maskB`
- `(filterMask & objectMask) != 0`

즉, 입력 그룹, 물리 그룹, sorting layer 그룹을 공통 비트마스크 모델로 통합해 처리하는 것이 이 시스템의 핵심 목적입니다.

---

## 2. `FilterMode`

| 값 | 의미 |
| :--- | :--- |
| `Whitelist` | 등록된 값만 허용 |
| `Blacklist` | 등록된 값을 제외한 나머지 허용 |

### 존재 이유

- 단순 포함 규칙뿐 아니라 제외 규칙도 같은 시스템 안에서 처리하기 위해

---

## 3. `FilterValue`

`FilterValue`는 mixed filter에서 타입과 값을 함께 들고 다니는 데이터 조각입니다.

### 프로퍼티

- `string TypeName`
- `string Value`

### 생성자

- `FilterValue()`
- `FilterValue(Type type, string value)`

### 존재 이유

- 서로 다른 enum 도메인을 하나의 filter에 함께 넣기 위해

---

## 4. `Filter`

`Filter`는 하나 이상의 값 집합을 비트마스크로 캐시하는 기본 타입입니다.

### 프로퍼티

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Name` | `string` | 필터 이름 |
| `EnumTypeName` | `string` | 단일 타입 필터의 enum 타입명 |
| `Values` | `List<string>` | 단일 타입 값 목록 |
| `MixedValues` | `List<FilterValue>` | mixed 값 목록 |
| `Mode` | `FilterMode` | whitelist / blacklist |
| `Mask` | `ulong` | 계산된 비트마스크 |
| `WhiteList` | `const FilterMode` | 호환용 상수 |
| `BlackList` | `const FilterMode` | 호환용 상수 |

### 정적 메서드

- `static Filter? Get(string name)`
- `static void Register(Filter filter)`

### 생성자

- `Filter()`
- `Filter(string name, Type enumType, Array values, FilterMode mode)`

### 인스턴스 메서드

- `virtual bool Check<T>(T value) where T : struct, Enum`
- `virtual IEnumerable<T> GetValues<T>() where T : struct, Enum`
- `void UpdateCache()`

### 존재 이유

- 입력, 물리 그룹, sorting layer 등에서 같은 판정 방식을 공유하기 위해

### 구현상 중요한 규칙

- `UpdateCache()`는 문자열/타입 정보를 읽어 실제 `Mask`를 다시 계산합니다.
- blacklist 모드에서도 내부적으로는 bitmask를 사용하고, 판정 시 논리 반전만 적용합니다.

---

## 5. `MixedFilter`

`MixedFilter`는 서로 다른 enum 타입 값을 하나의 필터에 넣기 위한 특수 필터입니다.

### 생성자

- `MixedFilter()`
- `MixedFilter(string name, FilterMode mode)`

### 메서드

- `void AddValue<T>(T value) where T : struct, Enum`

### 존재 이유

- 예를 들어 입력 필터에서 `KeyCode`와 `MouseButton`을 함께 다뤄야 할 수 있기 때문입니다.

---

## 6. `FilterRegistry`

`FilterRegistry`는 값과 비트 인덱스 사이의 전역 매핑을 관리합니다.

### 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `int GetBitIndex<T>(T value) where T : struct, Enum` | 값의 비트 인덱스 조회 |
| `int GetBitIndex(Type enumType, string valueName)` | 타입/이름 기준 인덱스 조회 |
| `int GetBitIndex(string typeName, string valueName)` | 문자열 기준 인덱스 조회 |
| `IEnumerable<T> GetValuesFromMask<T>(ulong mask) where T : struct, Enum` | 마스크에서 값 복원 |
| `ulong GetMask<T>(T value) where T : struct, Enum` | 값 하나의 마스크 |
| `ulong GetMask(Type enumType, string valueName)` | 타입/이름 기준 마스크 |
| `ulong GetMask(string typeName, string valueName)` | 문자열 기준 마스크 |
| `ulong GetGroupMask(string groupName)` | physics group 관용 마스크 |
| `void Clear()` | 전체 레지스트리 초기화 |

### 존재 이유

- 프로젝트 전역에서 같은 값이 항상 같은 비트 위치를 갖도록 보장해야 하기 때문입니다.

### 중요한 제약

- 현재 설계는 64비트 기반이므로 동시에 표현할 수 있는 전체 고유 값 수가 제한됩니다.

