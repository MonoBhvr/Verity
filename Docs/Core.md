# Verity Core 문서

이 문서는 Verity 엔진의 코어 레이어를 다룹니다.

범위는 다음과 같습니다.

- ECS 기본 타입
- 월드와 씬 로딩 구조
- 공용 디버그/시간/수학 타입
- 에셋 경로 유틸리티와 타일맵 데이터 구조
- 직렬화/인스펙터용 attribute 의미

이 문서의 목표는 단순한 API 목록이 아니라, “이 타입이 왜 존재하는가”, “언제 써야 하는가”, “현재 구현에서 어떤 비용 특성이 있는가”까지 함께 설명하는 것입니다.

---

## 1. Core 레이어 개요

Verity의 Core 레이어는 엔진의 최소 런타임 기반입니다. 다른 시스템은 대부분 이 계층 위에 올라갑니다.

| 영역 | 대표 타입 | 존재 이유 |
| :--- | :--- | :--- |
| ECS | `Entity`, `Component`, `Transform` | 런타임 오브젝트를 조립식으로 구성하기 위해 |
| World | `World`, `WorldManager`, `WorldLoader` | 엔티티 트리와 월드 단위 설정을 묶기 위해 |
| Script 기반 공용 기능 | `Time`, `Debug` | 스크립트가 공통으로 접근해야 하는 상태를 제공하기 위해 |
| 수학/값 타입 | `Color`, `Vector2`, `Vector3` | 엔진 전역에서 일관된 데이터 표현을 유지하기 위해 |
| 에셋/맵 데이터 | `Sprite`, `StyleAsset`, `Tilemap` | 렌더러와 툴이 공유하는 데이터 포맷이 필요해서 |

---

## 2. Attribute와 마커 타입

이 섹션의 타입들은 런타임 동작 그 자체보다는 “타입에 대한 메타데이터”를 제공합니다.

### 2.1 Attribute 목록

| 타입 | 적용 대상 | 존재 이유 |
| :--- | :--- | :--- |
| `RequireComponentAttribute` | 클래스 | 특정 컴포넌트가 붙을 때 필요한 다른 컴포넌트를 자동 보장하기 위해 |
| `SerializeFieldAttribute` | 필드/프로퍼티 | 직렬화 대상 멤버를 명시하기 위해 |
| `HideInInspectorAttribute` | 필드/프로퍼티 | 에디터 인스펙터에서 숨겨야 할 멤버를 표시하기 위해 |
| `AssetReferenceAttribute` | 필드/프로퍼티 | 특정 확장자의 에셋 선택 필드임을 표시하기 위해 |
| `SingleInstancePerWorldAttribute` | 클래스 | 월드당 하나만 존재해야 하는 컴포넌트를 제한하기 위해 |
| `NonDisableableAttribute` | 클래스 | `Enabled = false`가 허용되지 않는 컴포넌트를 표시하기 위해 |
| `ButtonAttribute` | 메서드 | 에디터에서 버튼으로 호출할 수 있는 메서드를 표시하기 위해 |
| 선택자 계열 Attribute | 필드/프로퍼티 | Tag, PhysicsGroup, SortingLayer, Filter 관련 선택 UI를 붙이기 위해 |

### 2.2 주요 타입과 API

#### `RequireComponentAttribute`

| 항목 | 형식 | 설명 |
| :--- | :--- | :--- |
| `RequiredType` | `Type` | 반드시 함께 존재해야 하는 컴포넌트 타입 |
| 생성자 | `RequireComponentAttribute(Type requiredType)` | 요구 타입을 지정 |

#### `AssetReferenceAttribute`

| 항목 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Extension` | `string` | 허용 또는 기본 대상으로 보는 에셋 확장자 |
| 생성자 | `AssetReferenceAttribute(string extension = "")` | 확장자 지정 |

#### `ButtonAttribute`

| 항목 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Label` | `string?` | 인스펙터 버튼 레이블 |
| `Undoable` | `bool` | 버튼 실행을 에디터 Undo 범위로 감쌀지 여부 |
| 생성자 | `ButtonAttribute(string? label = null, bool undoable = false)` | 레이블과 Undo 여부 지정 |

예시:

```csharp
[Button("Apply Native Aspect Ratio", undoable: true)]
public void ApplyNativeAspectRatio()
{
    // 상태를 변경하는 버튼 작업
}
```

- `undoable: true`면 버튼 실행 전/후로 에디터 Undo snapshot을 기록합니다.
- 필드/프로퍼티 값을 바꾸는 편집용 버튼에만 켜는 것이 안전합니다.
- 재생, 미리보기, 외부 IO, 런타임 트리거처럼 부작용이 큰 버튼은 기본값 `false`를 유지하는 편이 맞습니다.

### 2.3 마커 타입

- `Tag`
- `SortingLayer`
- `PhysicsGroup`

이 타입들은 일반 데이터 객체가 아니라 filter/selector 시스템이 “이 문자열 또는 enum-like 값이 어떤 도메인에 속하는가”를 구분하기 위한 식별자로 쓰입니다.

---

## 3. ECS 핵심 타입

## 3.1 `Entity`

`Entity`는 컴포넌트를 붙이는 최소 단위입니다. “게임 오브젝트”에 가장 가까운 개념이며, 이름/태그/활성 상태와 함께 컴포넌트 집합을 가집니다.

### 존재 이유

- 기능을 상속 대신 조합으로 구성하기 위해
- transform 계층과 개별 기능 컴포넌트를 분리하기 위해
- 직렬화, 복제, 검색, 에디터 편집 단위를 통일하기 위해

### 주요 프로퍼티

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Id` | `Guid` | 엔티티의 고유 식별자 |
| `Name` | `string` | 엔티티 이름 |
| `Tag` | `string` | 태그 |
| `Active` | `bool` | 엔티티 활성 상태 |
| `Transform` | `Transform` | 항상 존재하는 transform 컴포넌트 |
| `BlueprintAssetPath` | `string` | 블루프린트 원본 경로 |
| `BlueprintAssetGuid` | `string` | 블루프린트 GUID |
| `BlueprintSourceEntityId` | `Guid?` | 원본 엔티티 ID |
| `BlueprintInstanceRootId` | `Guid?` | 인스턴스 루트 엔티티 ID |
| `IsBlueprintInstance` | `bool` | 블루프린트 인스턴스인지 여부 |
| `IsBlueprintInstanceRoot` | `bool` | 블루프린트 인스턴스 루트인지 여부 |

### 정적 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `Entity? Find(string name)` | 이름으로 첫 번째 엔티티 검색 |
| `Entity? FindWithTag(string tag)` | 태그로 첫 번째 엔티티 검색 |
| `Entity[] FindEntitiesWithTag(string tag)` | 태그로 모든 엔티티 검색 |
| `T? FindObjectOfType<T>(bool includeInactive = false) where T : class` | 타입으로 첫 번째 컴포넌트 검색 |
| `T[] FindObjectsOfType<T>(bool includeInactive = false) where T : class` | 타입으로 모든 컴포넌트 검색 |
| `void Destroy(Entity entity)` | 엔티티 파괴 예약 |
| `void Destroy(Component component)` | 컴포넌트 제거 |
| `Entity Instantiate(string name = "New Entity")` | 새 엔티티 생성 |
| `Entity? Instantiate(Entity original)` | 엔티티 복제 |
| `T? Instantiate<T>(T original) where T : Component` | 해당 컴포넌트를 포함한 엔티티 복제 후 같은 타입 컴포넌트 반환 |

### 인스턴스 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `T AddComponent<T>() where T : Component, new()` | 컴포넌트 추가 |
| `Component AddComponent(Type componentType)` | 런타임 타입으로 컴포넌트 추가 |
| `bool CanAddComponent(Type componentType, out string? reason)` | 추가 가능 여부 검사 |
| `T? GetComponent<T>() where T : class` | 첫 번째 일치 컴포넌트 반환 |
| `Component? GetComponent(Type type)` | 런타임 타입 기반 단건 조회 |
| `IEnumerable<T> GetComponents<T>() where T : class` | 일치하는 모든 컴포넌트 반환 |
| `T? GetComponentInChildren<T>(bool includeInactive = false) where T : class` | 자식 포함 단건 검색 |
| `IEnumerable<T> GetComponentsInChildren<T>(bool includeInactive = false) where T : class` | 자식 포함 다건 검색 |
| `T? GetComponentInParent<T>(bool includeInactive = false) where T : class` | 부모 방향 단건 검색 |
| `IEnumerable<T> GetComponentsInParent<T>(bool includeInactive = false) where T : class` | 부모 방향 다건 검색 |
| `bool RemoveComponent<T>() where T : Component` | 타입으로 컴포넌트 제거 |
| `bool RemoveComponent(Component component)` | 인스턴스로 컴포넌트 제거 |
| `IReadOnlyList<Component> GetAllComponents()` | 모든 컴포넌트 반환 |

### 구현상 중요한 동작

- `GetComponent<T>()`와 `GetComponents<T>()`는 타입별 캐시를 사용합니다.
- `Transform`은 생성 시 자동으로 붙고, 두 번째 `Transform`은 추가할 수 없습니다.
- `RequireComponentAttribute`가 붙은 컴포넌트를 추가하면 요구 컴포넌트도 자동으로 붙습니다.
- `SingleInstancePerWorldAttribute`가 붙은 타입은 월드 전체 기준으로 중복이 막힙니다.

### 성능 메모

- 단일 엔티티 내부의 반복 `GetComponent<T>()`는 이제 상대적으로 싸지만, `FindObjectOfType<T>()`는 여전히 월드 전체를 순회합니다.
- `GetComponentInChildren` / `GetComponentInParent` 계열은 계층 재귀 탐색입니다.

---

## 3.2 `Component`

`Component`는 모든 기능성 타입의 공통 베이스입니다.

### 존재 이유

- 엔티티 소유 관계를 표준화하기 위해
- enabled/disabled 상태 전환 규칙을 통일하기 위해
- 각 컴포넌트가 owner 기준 convenience API를 공통으로 갖게 하기 위해

### 프로퍼티

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Owner` | `Entity` | 이 컴포넌트를 소유한 엔티티 |
| `Transform` | `Transform` | `Owner.Transform` shortcut |
| `CanBeDisabled` | `bool` | 비활성화 가능한 타입인지 여부 |
| `Enabled` | `bool` | 활성화 상태 |

### override 가능한 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `protected virtual void OnEnable()` | enabled 전환 시 호출 |
| `protected virtual void OnDisable()` | disabled 전환 시 호출 |
| `public virtual void OnDestroy()` | 제거 시 호출 |

### convenience 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `T? GetComponent<T>() where T : class` | owner 기준 단건 조회 |
| `Component? GetComponent(Type type)` | owner 기준 런타임 타입 조회 |
| `IEnumerable<T> GetComponents<T>() where T : class` | owner 기준 다건 조회 |
| `T? GetComponentInChildren<T>(bool includeInactive = false) where T : class` | 자식 포함 검색 |
| `IEnumerable<T> GetComponentsInChildren<T>(bool includeInactive = false) where T : class` | 자식 포함 다건 검색 |
| `T? GetComponentInParent<T>(bool includeInactive = false) where T : class` | 부모 포함 검색 |
| `IEnumerable<T> GetComponentsInParent<T>(bool includeInactive = false) where T : class` | 부모 포함 다건 검색 |

### 구현상 중요한 규칙

- `Enabled`가 바뀌면 world script cache 또는 state version이 적절히 갱신됩니다.
- `NonDisableableAttribute`가 붙은 타입은 `Enabled = false`가 무시됩니다.

---

## 3.3 `Transform`

`Transform`은 모든 엔티티에 항상 존재하는 계층/좌표 컴포넌트입니다.

### 존재 이유

- 렌더, 물리, 오디오, UI 부착형 시스템이 공통 공간 좌표를 공유해야 해서
- 엔티티 계층 구조를 ECS와 분리된 표준 방식으로 제공하기 위해

### 프로퍼티

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Position` | `Vector2` | 로컬 위치 |
| `Rotation` | `float` | 로컬 회전, degree |
| `Scale` | `Vector2` | 로컬 스케일 |
| `Parent` | `Transform?` | 부모 transform |
| `Children` | `IReadOnlyList<Transform>` | 자식 transform 목록 |
| `WorldPosition` | `Vector2` | 월드 위치 |
| `WorldRotation` | `float` | 월드 회전 |
| `WorldScale` | `Vector2` | 월드 스케일 |

### 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `void SetParent(Transform? newParent, bool preserveWorldPosition = true)` | 부모 설정 |
| `void SetParent(Transform? newParent, bool preserveWorldPosition, int siblingIndex)` | 부모와 sibling index 동시 설정 |
| `int GetSiblingIndex()` | 현재 sibling index 반환 |
| `void SetSiblingIndex(int siblingIndex)` | sibling index 변경 |
| `Matrix4x4 GetLocalMatrix()` | 로컬 행렬 계산/반환 |
| `Matrix4x4 GetWorldMatrix()` | 월드 행렬 계산/반환 |

### 구현상 중요한 규칙

- 부모 변경 시 cycle detection을 수행합니다.
- `preserveWorldPosition`이 true이면 local 값이 다시 계산됩니다.
- local/world matrix, world rotation, world scale은 dirty-cache입니다.
- 부모가 바뀌면 하위 트리 전체 world cache가 무효화됩니다.

### 성능 메모

- 반복적인 world transform 접근 비용은 크게 줄었지만, parent를 자주 갈아끼우는 패턴은 여전히 비쌉니다.

---

## 3.4 Blueprint/Prefab 시스템

Verity의 Blueprint는 “엔티티 트리를 재사용 가능한 에셋으로 저장하고, 월드에서는 그 에셋의 인스턴스로 다루는 방식”입니다. 에디터 UI와 파일 확장자는 `Blueprint`, 일반적인 엔진 용어로는 `Prefab`에 가까운 개념입니다.

### 존재 이유

- 자주 쓰는 엔티티 조합과 계층 구조를 에셋으로 재사용하기 위해
- 월드에는 공통 원본을 유지하고, 인스턴스별 차이만 override로 저장하기 위해
- 에디터에서 원본 수정과 인스턴스 배치를 분리해 작업 흐름을 단순화하기 위해

### `Entity`의 Blueprint 관련 프로퍼티

| 이름 | 형식 | 설명 | 존재 이유 |
| :--- | :--- | :--- | :--- |
| `BlueprintAssetPath` | `string` | 블루프린트 원본 에셋 경로 | 인스턴스가 어느 `.blueprint` 에셋을 참조하는지 알기 위해 |
| `BlueprintAssetGuid` | `string` | 블루프린트 GUID | 경로 변경이나 이동 이후에도 같은 에셋을 안정적으로 추적하기 위해 |
| `BlueprintSourceEntityId` | `Guid?` | 원본 엔티티 ID | 인스턴스 내부 각 엔티티가 원본의 어느 엔티티에서 왔는지 매핑하기 위해 |
| `BlueprintInstanceRootId` | `Guid?` | 인스턴스 루트 엔티티 ID | 같은 블루프린트 인스턴스에 속한 하위 엔티티를 한 묶음으로 식별하기 위해 |
| `IsBlueprintInstance` | `bool` | 블루프린트 인스턴스인지 여부 | 일반 엔티티와 블루프린트 인스턴스를 런타임/에디터에서 빠르게 구분하기 위해 |
| `IsBlueprintInstanceRoot` | `bool` | 블루프린트 인스턴스 루트인지 여부 | override 계산, 저장, 새로고침을 루트 단위로 처리하기 위해 |

### 생성 워크플로우

| 단계 | 설명 | 존재 이유 |
| :--- | :--- | :--- |
| 1 | 에디터에서 엔티티를 `SaveEntityAsBlueprint`로 저장하면 선택한 엔티티 트리가 `.blueprint` 파일로 직렬화됩니다. | 재사용 가능한 원본 에셋을 만들기 위해 |
| 2 | 저장 직후 현재 월드의 원본 엔티티와 모든 자식 엔티티에 Blueprint 메타데이터가 기록됩니다. | 방금 저장한 오브젝트를 즉시 “원본 기반 인스턴스”로 전환하기 위해 |
| 3 | 이때 각 엔티티의 `BlueprintSourceEntityId`는 저장 시점의 엔티티 `Id`를 가리키고, 하위 엔티티들은 공통 `BlueprintInstanceRootId`를 공유합니다. | 이후 override 비교와 새로고침 시 원본-인스턴스 매핑을 유지하기 위해 |

### 인스턴스화 워크플로우

| 단계 | 설명 | 존재 이유 |
| :--- | :--- | :--- |
| 1 | 에디터에서 `.blueprint` 에셋을 열거나 드래그 앤 드롭/버튼으로 배치하면 `InstantiateBlueprint`가 호출됩니다. | 에셋 기반 배치를 표준화하기 위해 |
| 2 | 내부적으로 `SceneSerializer.InstantiateBlueprintInstance`가 블루프린트 파일을 임시 월드에 deserialize한 뒤, 현재 월드로 clone합니다. | 원본 데이터를 안전하게 읽고 실제 월드에는 독립 인스턴스를 만들기 위해 |
| 3 | clone된 엔티티들은 `BlueprintAssetPath`, `BlueprintAssetGuid`, `BlueprintSourceEntityId`, `BlueprintInstanceRootId`를 유지한 채 월드에 추가됩니다. | 인스턴스가 원본과의 연결 정보를 잃지 않게 하기 위해 |
| 4 | 블루프린트 편집 모드에서 새 엔티티를 만들면 기본적으로 첫 루트 엔티티 아래에 붙습니다. | 블루프린트 에셋이 하나의 루트 중심 구조를 유지하도록 돕기 위해 |

### 수정 및 저장 워크플로우

| 단계 | 설명 | 존재 이유 |
| :--- | :--- | :--- |
| 1 | 월드에 배치된 블루프린트 인스턴스는 이름, 활성 상태, transform, 컴포넌트 enabled 상태, 필드 값, 추가/삭제된 컴포넌트를 원본과 다르게 수정할 수 있습니다. | 같은 원본을 공유하되 배치별 차이를 허용하기 위해 |
| 2 | 이 차이는 저장 시 전체 복사본으로 덮어쓰지 않고 `CaptureBlueprintInstanceOverrides`가 override 목록으로 계산합니다. | 공통 데이터와 인스턴스별 변경점을 분리해 저장하기 위해 |
| 3 | 월드를 저장할 때 블루프린트 인스턴스 루트는 일반 엔티티 전체 직렬화 대신 `BlueprintInstance` 노드와 override 목록으로 기록됩니다. | 월드 파일 크기와 중복 데이터를 줄이기 위해 |
| 4 | 블루프린트 원본을 저장하면 에디터는 같은 에셋을 참조하는 모든 인스턴스 루트의 override 상태를 먼저 캡처한 뒤, 새 원본으로 다시 clone하고 override를 재적용합니다. | 원본 수정 후에도 각 인스턴스의 개별 변경을 최대한 보존하기 위해 |

### 에디터에서의 Blueprint 편집 방식

| 항목 | 설명 | 존재 이유 |
| :--- | :--- | :--- |
| 에셋 열기 | Inspector의 Blueprint 에셋 UI에서 `Open Blueprint`를 누르면 해당 `.blueprint` 파일이 별도 월드처럼 열립니다. | 원본 에셋을 직접 편집하는 모드를 분리하기 위해 |
| 편집 컨텍스트 | 에디터는 Blueprint를 열 때 새 월드를 만들고 파일 내용을 `preserveEntityIds: true`로 deserialize합니다. | 원본 엔티티 ID를 유지해 이후 인스턴스 override 비교가 가능하게 하기 위해 |
| 저장 | Blueprint 편집 모드에서 저장하면 `SerializeBlueprint(World)` 결과가 파일에 기록됩니다. | 블루프린트 에셋을 월드와 다른 저장 규칙으로 직렬화하기 위해 |
| 월드 복귀 | Hierarchy 상단의 Blueprint 모드 헤더에서 저장 후 이전 월드로 돌아갈 수 있습니다. | 원본 편집과 실제 배치 작업을 자연스럽게 오갈 수 있게 하기 위해 |
| 인스턴스 확인 | 월드에서 블루프린트 인스턴스를 선택하면 Inspector가 원본 경로와 override 항목을 별도 헤더로 표시합니다. | 현재 수정이 원본이 아닌 인스턴스 override임을 명확히 보여주기 위해 |

### 구현상 중요한 규칙

- `IsBlueprintInstance`는 `BlueprintSourceEntityId`가 있고 `BlueprintAssetPath`가 비어 있지 않을 때만 참입니다.
- `IsBlueprintInstanceRoot`는 인스턴스이며 동시에 `BlueprintInstanceRootId == Id`인 루트 엔티티만 참입니다.
- 블루프린트 인스턴스 저장은 원본 전체 복제가 아니라 “원본 + override” 모델입니다.
- 블루프린트 원본 저장 후에는 같은 에셋을 참조하는 기존 인스턴스들이 새 원본 기준으로 refresh됩니다.

### 성능/운영 메모

- 블루프린트 인스턴스 생성과 새로고침은 파일 deserialize와 clone 과정을 포함하므로 일반 엔티티 생성보다 무겁습니다.
- 대신 월드 저장 시 중복 hierarchy 전체를 반복 기록하지 않아 대형 반복 오브젝트에서는 관리 비용을 줄일 수 있습니다.

## 4. 월드 관리 타입

## 4.1 `World`

`World`는 루트 엔티티 목록과 월드 단위 설정, 캐시, destroy 예약 큐를 관리합니다.

### 존재 이유

- 게임 상태를 엔티티 트리와 함께 하나의 단위로 묶기 위해
- 물리/로직/렌더가 공통으로 접근하는 상태 저장소가 필요해서
- active world 교체, scene load/unload를 명시적으로 다루기 위해

### 프로퍼티

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Name` | `string` | 월드 이름 |
| `UseCustomSettings` | `bool` | 월드 커스텀 물리/틱 설정 사용 여부 |
| `CustomTPS` | `int` | 월드 전용 logic TPS |
| `CustomPTPS` | `int` | 월드 전용 physics TPS |
| `CustomGravity` | `System.Numerics.Vector2` | 월드 전용 중력 |
| `CustomFriction` | `float` | 월드 전용 기본 마찰 |
| `CustomBounciness` | `float` | 월드 전용 기본 반발 |
| `CustomLinearDamping` | `float` | 월드 전용 기본 선형 감쇠 |
| `CustomAngularDamping` | `float` | 월드 전용 기본 각 감쇠 |
| `CustomPhysicsThreshold` | `float` | 월드 전용 물리 threshold |
| `RootEntities` | `IReadOnlyList<Entity>` | 루트 엔티티 목록 |
| `StateVersion` | `int` | 월드 상태 버전 |

### 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `Entity CreateEntity(string name)` | 루트 엔티티 생성 |
| `void AddToRoot(Entity entity)` | 루트 목록에 추가 |
| `void AddToRoot(Entity entity, int index)` | 인덱스를 지정해 루트 추가 |
| `void RemoveFromRoot(Entity entity)` | 루트 목록에서 제거 |
| `int IndexOfRoot(Entity entity)` | 루트 인덱스 조회 |
| `void SetRootIndex(Entity entity, int index)` | 루트 순서 변경 |
| `void DestroyEntity(Entity entity)` | 파괴 예약 |
| `void ProcessPendingDestroys()` | 예약된 파괴 처리 |
| `IReadOnlyList<Entity> GetAllEntities()` | 전체 엔티티 플랫 캐시 반환 |
| `IEnumerable<T> GetAllComponents<T>() where T : class` | 월드 전체에서 타입 일치 컴포넌트 열거 |

### 구현상 중요한 규칙

- `GetAllEntities()`는 재귀 iterator가 아니라 플랫 캐시입니다.
- hierarchy 변경과 state 변경은 구분되며 둘 다 `StateVersion`에 반영될 수 있습니다.
- active script cache도 world 내부에서 유지됩니다.

---

## 4.2 `WorldManager`

`WorldManager`는 현재 로드된 월드들과 active world를 관리하는 전역 관리자입니다.

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `ActiveWorld` | `World?` | 현재 활성 월드 |
| `LoadedWorlds` | `IReadOnlyList<World>` | 현재 로드된 월드 목록 |

### 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `World? GetWorld(string name)` | 이름으로 월드 검색 |
| `World CreateWorld(string name)` | 새 월드 생성 |
| `World CreateOrReplaceWorld(string name)` | 기존 월드를 교체하거나 새로 생성 |
| `void SetActiveWorld(World world)` | 활성 월드 설정 |
| `void UnloadWorld(World world)` | 월드 언로드 |

### 존재 이유

- 스크립트와 시스템이 “현재 어느 월드에서 동작하는가”를 일관되게 알기 위해
- scene 교체를 하나의 명시적 API로 처리하기 위해

---

## 4.3 `WorldLoader`

`WorldLoader`는 world JSON을 읽고 실제 월드 객체를 만드는 로더입니다.

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `PendingWorldName` | `string?` | 다음에 로드할 월드 이름 예약 |
| `OnWorldLoaded` | `event Action<string>?` | 월드 로드 완료 이벤트 |

### 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `void LoadWorld(string worldPath, Assembly? userAssembly = null)` | 파일 경로 기준 월드 로드 |
| `void LoadWorldFromJson(string json, string name, Assembly? userAssembly = null)` | JSON 문자열에서 월드 로드 |
| `void LoadWorldByName(string name)` | 이름 기준 로드 예약 |

### 존재 이유

- 에디터/런타임 모두 동일한 world load 경로를 공유하기 위해
- 사용자 스크립트 어셈블리를 포함한 역직렬화를 처리하기 위해

---

## 4.4 `BuildSettings`

`BuildSettings`는 번들 또는 실행 진입점이 참고할 월드 목록과 시작 월드를 보관합니다.

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Worlds` | `List<string>` | 빌드에 포함할 월드 목록 |
| `StartWorldIndex` | `int` | 시작 월드 인덱스 |
| `LogoPath` | `string?` | 로고 경로 |

### 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `static BuildSettings Load(string path)` | 파일에서 설정 로드 |
| `static BuildSettings LoadFromJson(string json)` | JSON 문자열에서 설정 로드 |
| `void Save(string path)` | 파일로 저장 |

---

## 5. 공용 상태와 디버그 타입

## 5.1 `Time`

`Time`은 게임 루프 상태를 스크립트가 읽을 수 있게 노출하는 정적 타입입니다.

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `TargetTPS` | `int` | 현재 logic tick 목표값 |
| `TargetPTPS` | `int` | 현재 physics tick 목표값 |
| `DeltaTime` | `float` | 마지막 프레임 기준 delta |
| `FixedDeltaTime` | `float` | 고정 시간 간격 |
| `TotalTime` | `float` | 엔진 시작 이후 누적 시간 |
| `TimeScale` | `float` | 시간 배율 |
| `FrameCount` | `int` | 렌더 프레임 수 |
| `LogicTickCount` | `int` | logic tick 수 |
| `PhysicsTickCount` | `int` | physics tick 수 |

메서드:

- `void Reset()`

### 존재 이유

- 스크립트에서 루프 내부 상태를 읽기 위한 전역 기준점이 필요해서

---

## 5.2 `Debug`

`Debug`는 로그와 간단한 gizmo 선 그리기를 담당하는 정적 유틸리티입니다.

### 이벤트

- `event Action<string, LogLevel>? OnLog`

### 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `void Log(string message)` | 일반 로그 |
| `void LogWarning(string message)` | 경고 로그 |
| `void LogError(string message)` | 오류 로그 |
| `void DrawLine(System.Numerics.Vector2 start, System.Numerics.Vector2 end, System.Numerics.Vector4? color = null, float thickness = 0.02f)` | 디버그 선 추가 |
| `void DrawBox(System.Numerics.Vector2 center, System.Numerics.Vector2 size, System.Numerics.Vector4? color = null, float thickness = 0.02f)` | 디버그 박스 추가 |
| `void ClearDrawCommands()` | 누적 draw command 초기화 |

### 보조 타입

- `LogLevel { Info, Warning, Error }`
- `LineCommand`
  - `System.Numerics.Vector2 Start`
  - `System.Numerics.Vector2 End`
  - `System.Numerics.Vector4 Color`
  - `float Thickness`
- `IReadOnlyList<LineCommand> Lines`

### 존재 이유

- 런타임에 에디터/스크립트가 공통으로 접근하는 최소 디버그 표면이 필요해서

---

## 6. 공용 값 타입

## 6.1 `Color`

`Color`는 엔진 전역 색상 표현입니다.

### 프로퍼티

- `float R`
- `float G`
- `float B`
- `float A`

### 생성/변환

- `Color(float r, float g, float b, float a = 1.0f)`
- `static Color FromRgba(int r, int g, int b, int a = 255)`
- `implicit operator Vector4(Color c)`
- `implicit operator Color(Vector4 v)`
- `implicit operator System.Drawing.Color(Color c)`

### 정적 색상

- `White`
- `Black`
- `Red`
- `Green`
- `Blue`
- `Yellow`
- `Cyan`
- `Magenta`
- `Gray`
- `Clear`
- `CornflowerBlue`

### 존재 이유

- 렌더러, UI, 디버그, 직렬화 경로에서 동일한 색 표현을 쓰기 위해

---

## 6.2 `Vector2`

`Vector2`는 `System.Numerics.Vector2`와 호환되는 Unity 스타일 래퍼입니다.

### 주요 프로퍼티

- `float X`, `float Y`
- `float x`, `float y`
- `float magnitude`
- `float sqrMagnitude`
- `Vector2 normalized`

### 주요 정적 값

- `Up`, `Down`, `Left`, `Right`
- `Zero`, `One`, `UnitX`, `UnitY`
- 소문자 alias들

### 주요 메서드

- `float Length()`
- `float LengthSquared()`
- `System.Numerics.Vector2 ToNumerics()`
- `static Vector2 FromNumerics(System.Numerics.Vector2 v)`
- `static float Distance(Vector2 a, Vector2 b)`
- `static float DistanceSquared(Vector2 a, Vector2 b)`
- `static Vector2 Normalize(Vector2 v)`
- `static float Dot(Vector2 a, Vector2 b)`
- `static Vector2 Lerp(Vector2 a, Vector2 b, float t)`
- `static Vector2 LerpUnclamped(Vector2 a, Vector2 b, float t)`
- `static Vector2 Min(Vector2 a, Vector2 b)`
- `static Vector2 Max(Vector2 a, Vector2 b)`
- `static Vector2 Scale(Vector2 a, Vector2 b)`
- `static Vector2 Reflect(Vector2 inDirection, Vector2 inNormal)`
- `static Vector2 Perpendicular(Vector2 inDirection)`
- `static Vector2 ClampMagnitude(Vector2 vector, float maxLength)`
- `static Vector2 MoveTowards(Vector2 current, Vector2 target, float maxDistanceDelta)`
- `static float Angle(Vector2 from, Vector2 to)`
- `static float SignedAngle(Vector2 from, Vector2 to)`

### 존재 이유

- System.Numerics의 SIMD 장점과 Unity 스타일 사용감을 동시에 얻기 위해

---

## 6.3 `Vector3`

`Vector3`는 `System.Numerics.Vector3`와 호환되는 Unity 스타일 래퍼입니다.

### 주요 프로퍼티

- `float X`, `float Y`, `float Z`
- `float x`, `float y`, `float z`
- `float magnitude`
- `float sqrMagnitude`
- `Vector3 normalized`

### 주요 정적 값

- `Zero`, `One`
- `Up`, `Down`, `Left`, `Right`
- `Forward`, `Back`
- `UnitX`, `UnitY`, `UnitZ`
- 소문자 alias들

### 주요 메서드

- `float Length()`
- `float LengthSquared()`
- `System.Numerics.Vector3 ToNumerics()`
- `static Vector3 FromNumerics(System.Numerics.Vector3 v)`
- `static float Dot(Vector3 a, Vector3 b)`
- `static Vector3 Cross(Vector3 a, Vector3 b)`
- `static float Distance(Vector3 a, Vector3 b)`
- `static float DistanceSquared(Vector3 a, Vector3 b)`
- `static Vector3 Normalize(Vector3 v)`
- `static Vector3 Lerp(Vector3 a, Vector3 b, float t)`
- `static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t)`
- `static Vector3 Min(Vector3 a, Vector3 b)`
- `static Vector3 Max(Vector3 a, Vector3 b)`
- `static Vector3 Transform(Vector3 position, System.Numerics.Matrix4x4 matrix)`
- `static Vector3 TransformNormal(Vector3 normal, System.Numerics.Matrix4x4 matrix)`
- `static Vector3 Scale(Vector3 a, Vector3 b)`
- `static Vector3 Reflect(Vector3 inDirection, Vector3 inNormal)`
- `static Vector3 Project(Vector3 vector, Vector3 onNormal)`
- `static Vector3 ClampMagnitude(Vector3 vector, float maxLength)`
- `static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDistanceDelta)`

---

## 7. 에셋/월드 데이터 타입

## 7.1 `Sprite`

`Sprite`는 텍스처 전체가 아니라, “에셋 경로 + GUID + slice 식별자”를 함께 들고 다니는 경량 참조 타입입니다.

### 프로퍼티

- `string Path`
- `string Guid`
- `string SpriteId`

### 생성자

- `Sprite(string path)`
- `Sprite(string path, string guid)`
- `Sprite(string path, string guid, string spriteId)`

### 존재 이유

- sprite sheet 내부 slice까지 안정적으로 식별해야 해서
- 경로 변경 시 GUID 기반 복구 가능성을 남겨야 해서

---

## 7.2 `ShaderAsset` / `StyleAsset`

두 타입 모두 “문자열 경로를 그냥 들고 다니는 것”보다 명시적인 에셋 참조 구조를 만들기 위해 존재합니다.

### `ShaderAsset`

- 프로퍼티
  - `string Path`
  - `string Guid`
- 생성자
  - `ShaderAsset(string path)`
  - `ShaderAsset(string path, string guid)`

### `StyleAsset`

- 프로퍼티
  - `string Path`
  - `string Guid`
- 생성자
  - `StyleAsset(string path)`
  - `StyleAsset(string path, string guid)`

---

## 7.3 `StyleData`

`StyleData`는 셰이더 경로와 uniform 성격의 값들을 직렬화 가능한 형태로 보관하는 데이터 타입입니다.

### 프로퍼티

- `string? ShaderPath`
- `Dictionary<string, float> Floats`
- `Dictionary<string, Vector2> Vector2s`
- `Dictionary<string, Vector3> Vector3s`
- `Dictionary<string, Vector4> Vector4s`
- `Dictionary<string, Color> Colors`
- `Dictionary<string, string> Textures`

### 메서드

- `static StyleData? FromJson(string json)`
- `string ToJson()`

### 존재 이유

- 렌더 스타일 데이터를 코드 타입이 아니라 에셋 데이터로 다룰 수 있게 하기 위해

---

## 7.4 `AssetPathUtility`

`AssetPathUtility`는 Verity의 경로/GUID/meta/sprite import 관련 작업을 담당하는 핵심 유틸리티입니다.

### 존재 이유

- 상대 경로와 절대 경로를 통일하기 위해
- GUID 기반 에셋 복구를 지원하기 위해
- sprite import metadata를 로딩/저장/resolve 하기 위해

### 주요 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `string Normalize(string? fullPath)` | Verity 표준 자산 경로 형태로 정규화 |
| `string DisplayName(string? path)` | 사용자 표시용 파일명 반환 |
| `bool IsMetaFile(string? path)` | meta 파일 여부 판정 |
| `string GetMetaPath(string assetPath)` | meta 경로 생성 |
| `string EnsureMetaAndGetGuid(string? assetPath)` | meta 보장 후 GUID 반환 |
| `string TryGetGuid(string? assetPath)` | 기존 GUID 조회 |
| `AssetReferenceData CreateReference(string? assetPath)` | 경로/GUID 레퍼런스 생성 |
| `JsonObject ToJsonNode(string? path, string? guid = null)` | 일반 에셋 참조 JSON 노드 생성 |
| `JsonObject ToSpriteJsonNode(Sprite sprite)` | sprite 참조 JSON 노드 생성 |
| `AssetReferenceData FromJsonNode(JsonNode? node)` | JSON에서 일반 참조 읽기 |
| `Sprite FromSpriteJsonNode(JsonNode? node)` | JSON에서 sprite 참조 읽기 |
| `string ResolvePath(string? projectRootOrAssetsPath, string? path, string? guid = null)` | 실제 파일 시스템 경로 해석 |
| `void InvalidateCache(string? projectRootOrAssetsPath = null)` | 경로/GUID/meta/slice 캐시 무효화 |
| `void InvalidateAssetCache(string? assetPath)` | 특정 에셋 캐시 무효화 |
| `AssetMeta LoadMeta(string? assetPath)` | meta 로드 |
| `void SaveMeta(string? assetPath, AssetMeta meta)` | meta 저장 |
| `SpriteImportSettings? TryGetSpriteImportSettings(string? assetPath)` | sprite import 설정 읽기 |
| `void SaveSpriteImportSettings(string? assetPath, SpriteImportSettings settings)` | sprite import 설정 저장 |
| `SpriteSlice ResolveSpriteSlice(string? assetPath, Sprite sprite, int textureWidth, int textureHeight)` | sprite id에 맞는 slice 결정 |
| `SpriteSlice ClampSlice(SpriteSlice slice, int textureWidth, int textureHeight, Vector2 defaultPivot)` | slice 범위 보정 |

### Asset Import 파이프라인

현재 코드 기준으로 Verity의 import 파이프라인은 모든 에셋에 공통으로 적용되는 `Path + Guid + .meta` 계층과, 그 위에 추가로 sprite texture만 가지는 `SpriteImportSettings` 계층으로 나뉩니다. 즉 “모든 에셋이 import된다”기보다는, 모든 에셋이 먼저 asset reference 체계에 편입되고, 이미지 에셋만 추가 가공 정보를 갖는 구조입니다.

### 지원 범위

| 에셋 종류 | 대표 타입 | import 시 실제로 저장되는 것 | 후처리 여부 |
| :--- | :--- | :--- | :--- |
| sprite texture (`.png` 등 이미지) | `Sprite`, `SpriteImportSettings` | `Guid`, `SpriteImport` 설정, slice 목록 | 있음 |
| texture 경로 참조 | `Sprite` | `Path`, `Guid`, 필요 시 `SpriteId` | slice 해석만 수행 |
| sound | `AudioClip` | `Path`, `Guid` | 별도 import 설정 없음 |
| UI/style/shader/animation controller 등 경로 기반 에셋 | `UiAsset`, `StyleAsset`, `ShaderAsset`, `AnimatorControllerAsset` | `Path`, `Guid` | 별도 import 설정 없음 |

핵심 포인트는 다음과 같습니다.

- `AssetPathUtility.EnsureMetaAndGetGuid(...)`는 경로 기반 에셋 전반에 공통으로 쓰입니다.
- 실제 import 설정 구조체가 존재하는 것은 현재 `SpriteImportSettings`뿐입니다.
- 따라서 “processed asset”이라는 말도 현재 구현에서는 sprite texture의 filter/slice/pivot/world size 계산을 뜻하는 경우가 대부분입니다.

### 1) raw asset이 프로젝트에 들어오는 단계

에디터는 파일을 프로젝트 `Assets` 아래에 두고, 필요할 때 `AssetPathUtility.EnsureMetaAndGetGuid(...)`를 호출해 같은 경로의 `.meta` 파일을 보장합니다.

이 단계에서 공통으로 일어나는 일:

- 원본 파일 경로를 `AssetPathUtility.Normalize(...)`로 `Assets/...` 형태로 정규화합니다.
- `.meta`가 없으면 생성하고 `AssetMeta.Guid`를 기록합니다.
- 복제/이동/생성 후에는 `InvalidateCache(...)` 또는 `InvalidateAssetCache(...)`로 경로/GUID/slice 캐시를 비웁니다.

이 GUID는 이후 경로가 바뀌어도 `AssetPathUtility.ResolvePath(projectRootOrAssetsPath, path, guid)`가 실제 파일을 다시 찾는 기준이 됩니다.

### 2) sprite texture import 설정 생성 단계

이미지 파일은 처음 inspector/project browser에서 조회될 때 `EditorApp.GetOrCreateSpriteImportSettings(...)` 경로를 통해 import 설정이 lazy initialization 됩니다.

동작 순서:

1. `TextureManager.GetRawPixels(fullPath)`로 원본 이미지 크기를 읽습니다.
2. 기존 `.meta`에 `SpriteImport`가 있으면 `Normalize(textureWidth, textureHeight)`로 값 범위를 정리합니다.
3. 설정이 없으면 `SpriteImportUtility.CreateDefaults(ProjectSettings, width, height)`로 새 `SpriteImportSettings`를 만듭니다.
4. 결과를 `AssetPathUtility.SaveSpriteImportSettings(fullPath, settings)`로 `.meta`에 저장합니다.

`ProjectSettings`가 기본값 공급원으로 쓰는 멤버:

- `DefaultSpritePixelsPerUnit`
- `DefaultPointFilterMaxDimension`
- `DefaultSpriteSizeMode`

즉 sprite import 기본값은 프로젝트 단위 정책이고, 실제 slice/filter/pivot 데이터는 각 texture의 `.meta`에 귀속됩니다.

### 3) `SpriteImportSettings` 구조

`SpriteImportSettings`는 “원본 texture를 runtime sprite reference로 어떻게 잘라 쓸 것인가”를 정의합니다.

| 멤버 | 의미 |
| :--- | :--- |
| `Filter` | `TextureManager.Load(...)`에 넘길 texture filter (`Point`/`Linear`) |
| `SpriteMode` | 전체 이미지 1장으로 쓸지(`Single`), 여러 slice로 쪼갤지(`Multiple`) |
| `SizeMode` | 월드 크기를 `FitInsideUnit` 또는 `PixelsPerUnit` 기준으로 계산할지 |
| `PixelsPerUnit` | `PixelsPerUnit` 모드일 때 픽셀-월드 단위 비율 |
| `DefaultPivot` | 기본 pivot |
| `NineSliceLeft/Right/Top/Bottom` | 9-slice border 메타데이터 |
| `Slices` | 실제 `SpriteSlice` 목록 |

각 `SpriteSlice`는 다음 정보를 가집니다.

- `Id`: 런타임/직렬화에서 slice를 안정적으로 가리키는 식별자
- `Name`: 에디터 목록과 picker에 노출되는 이름
- `X`, `Y`, `Width`, `Height`: 원본 texture 내부 rectangle
- `Pivot`: slice별 pivot

`Normalize(...)`는 잘못된 import 데이터를 저장하지 않도록 다음을 보정합니다.

- `PixelsPerUnit`와 9-slice border를 음수/0 이하에서 보정
- `DefaultPivot`, `Slice.Pivot`를 `0..1`로 clamp
- `Single` 모드에서 slice가 없으면 전체 이미지용 기본 slice 자동 생성
- 빈 `Name`, 빈 `Id`, 0 이하 `Width`/`Height`를 보정

### 4) 에디터가 import를 편집하는 방식

이미지 asset을 선택하면 `InspectorWindow.DrawImagePreview(...)`가 import 편집 UI를 엽니다. 여기서 에디터는 단순 미리보기만 하는 것이 아니라, import 데이터의 실질적인 authoring 도구 역할을 합니다.

편집 가능한 항목:

- `Filter`
- `SpriteMode`
- `SizeMode`
- `PixelsPerUnit`
- `DefaultPivot`
- 개별 `SpriteSlice`의 이름/rectangle/pivot
- grid slicing 결과 생성

변경이 생기면 inspector는 `settings.Normalize(...)` 후 `AssetPathUtility.SaveSpriteImportSettings(fullPath, settings)`를 호출합니다. 따라서 import 결과물은 별도 binary cache가 아니라 원본 파일 옆 `.meta`의 JSON 데이터로 영속화됩니다.

`ProjectWindow`도 이 데이터를 직접 사용합니다.

- `Multiple` 모드면 파일 아래에 각 slice를 별도 browser item으로 펼칩니다.
- slice 복제/삭제/이름 변경은 `SpriteSlice.Id`를 유지하거나 새로 만들면서 `.meta`를 다시 저장합니다.
- `TilePaletteWindow`와 sprite picker는 같은 `SpriteImportSettings`를 읽어 slice 목록을 메뉴로 보여 줍니다.

즉 import 데이터는 inspector 전용 정보가 아니라, project browser / tile palette / animation picker가 함께 공유하는 편집용 source of truth입니다.

### 5) 저장 포맷과 직렬화

저장 계층은 두 가지입니다.

1. 파일 옆 `.meta`
2. 월드/컴포넌트/에셋 직렬화 JSON

#### `.meta`

`.meta`는 `AssetMeta`를 저장하며, 현재 핵심 필드는 다음 둘입니다.

- `Guid`
- `SpriteImport`

즉 sprite texture의 import 정보는 원본 이미지 파일 자체를 바꾸지 않고 sidecar metadata로 저장됩니다.

#### 런타임/씬 직렬화

런타임 데이터나 scene JSON에는 texture 픽셀 데이터가 아니라 asset reference만 기록됩니다.

- 일반 `IPathAsset`는 `Path` + `Guid`
- `Sprite`는 `Path` + `Guid` + `SpriteId`
- `AudioClip`도 `Path` + `Guid`를 직렬화하고, load 시 `PostLoad(...)`로 실제 핸들을 엽니다.

이 구조 덕분에 scene/world 파일은 가볍게 유지되고, 실제 import 세부사항은 `.meta`에서 다시 읽어 조립됩니다.

### 6) runtime 사용 단계

runtime은 `Sprite` 자체를 “이미 잘린 텍스처 조각”으로 들고 있지 않습니다. 대신 `Sprite.Path`, `Sprite.Guid`, `Sprite.SpriteId`만 들고 있다가 필요 시 import metadata를 역조회합니다.

대표 흐름:

1. `SpriteRenderer` 등이 `Sprite` 참조를 보유합니다.
2. 에디터/런타임은 `ResolveAssetPath(...)` 또는 `AssetPathUtility.ResolvePath(...)`로 실제 파일을 찾습니다.
3. 텍스처 로드는 `EditorApp.LoadSpriteTexture(...)` 또는 렌더 파이프라인에서 `settings.Filter`를 반영해 수행됩니다.
4. `AssetPathUtility.ResolveSpriteSlice(...)`가 `Sprite.SpriteId`에 대응하는 `SpriteSlice`를 `.meta`에서 찾습니다.
5. `RenderPipeline.TryResolveSpriteSlice(...)`가 그 slice rectangle로 UV를 계산해 실제 draw에 사용합니다.
6. 월드 공간 크기가 필요하면 `SpriteImportUtility.ComputeWorldSize(settings, slice)`가 `SizeMode`와 `PixelsPerUnit`를 반영합니다.

즉 raw texture는 import 시점에 atlas나 별도 runtime asset으로 변환되지 않고, 원본 texture + `.meta`를 기준으로 “필요할 때 slice를 해석하는 방식”으로 소비됩니다.

### 7) 실제 구현을 기준으로 본 해석

- 현재 Verity의 import 파이프라인은 generic asset database + sprite-specific import 설정의 조합입니다.
- sprite는 `Guid`뿐 아니라 `SpriteId`까지 저장하므로 한 texture 안의 여러 slice를 안정적으로 참조할 수 있습니다.
- sound, UI, style, shader, animation controller 같은 다른 에셋은 현재 import 설정/가공 단계보다는 `Path`/`Guid` 유지와 로드 시점 해석이 중심입니다.
- 따라서 문서상 “sprites, textures, sounds 등을 import한다”는 표현은 맞지만, 실제 후처리 파이프라인이 깊게 구현된 대상은 현재 sprite texture입니다.

### 성능 메모

- 이 유틸리티는 캐시가 있을 때는 빠르지만, 캐시 미스 시 파일 시스템 접근이 발생합니다.
- 따라서 렌더 루프에서 반복 호출되지 않도록 상위 시스템이 별도 캐시를 둡니다.

---

## 7.5 `TileBase`, `Tile`, `AnimatedTile`, `RuleTile`

이 타입들은 타일맵 셀 하나가 어떤 시각/충돌 데이터를 가질지를 정의합니다.

### `TileBase`

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `AssetPath` | `string?` | 원본 자산 경로 |
| `AssetGuid` | `string?` | 원본 자산 GUID |
| `Name` | `string` | 타일 이름 |
| `IsCollidable` | `bool` | 충돌 여부 |
| `Color` | `Color` | 타일 색상 tint |

메서드:

- `abstract Sprite? GetSprite(int x, int y, Tilemap tilemap)`

### `Tile`

- 프로퍼티
  - `Sprite? Sprite`

존재 이유:

- 가장 단순한 “고정 sprite 타일” 표현을 제공하기 위해

### `AnimatedTile`

- 프로퍼티
  - `List<Sprite> Sprites`
  - `float AnimationSpeed`
  - `float StartOffset`

존재 이유:

- 타일 단위 애니메이션을 데이터만으로 표현하기 위해

### `RuleTile`

- 프로퍼티
  - `Sprite? DefaultSprite`
  - `List<RuleTile.Rule> Rules`

하위 타입:

- `Neighbor { Any, Required, NotRequired }`
- `Rule`
  - `Neighbor[] Neighbors`
  - `Sprite? Sprite`

존재 이유:

- 주변 타일 배치를 보고 자동으로 sprite를 바꾸는 autotile 규칙이 필요해서

---

## 7.6 `Tilemap`

`Tilemap`은 셀 좌표와 `TileBase`를 매핑하는 월드 데이터 컴포넌트입니다.

### 존재 이유

- 타일 데이터를 엔티티-컴포넌트 구조 안에 넣기 위해
- 렌더러와 물리 shape가 같은 소스를 읽게 하기 위해
- 편집기와 런타임이 동일한 데이터 구조를 공유하기 위해

### 프로퍼티

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Tiles` | `Dictionary<(int x, int y), TileBase>` | 셀-타일 맵 |
| `RenderDirty` | `bool` | 렌더 캐시 무효화 플래그 |
| `PhysicsDirty` | `bool` | 물리 캐시 무효화 플래그 |
| `ContentVersion` | `int` | 내용 변경 버전 |
| `TileSize` | `Vector2` | 셀 크기 |

### 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `void SetTile(int x, int y, TileBase? tile)` | 셀에 타일 설정 |
| `TileBase? GetTile(int x, int y)` | 셀 타일 조회 |
| `bool HasTile(int x, int y)` | 셀 존재 여부 |
| `void Clear()` | 전체 비우기 |
| `IEnumerable<KeyValuePair<(int x, int y), TileBase>> GetAllTiles()` | 전체 타일 열거 |
| `IEnumerable<KeyValuePair<(int x, int y), TileBase>> GetTilesInRegion(int minX, int minY, int maxX, int maxY)` | 특정 영역 타일 열거 |
| `bool TryGetTileBounds(out int minX, out int minY, out int maxX, out int maxY)` | 타일 bounds 반환 |
| `(int x, int y) WorldToCell(Vector2 worldPos)` | 월드 좌표를 셀 좌표로 변환 |
| `Vector2 CellToWorld(int x, int y)` | 셀 좌표를 월드 좌표로 변환 |
| `Vector2 GetCellCenterWorld(int x, int y)` | 셀 중심 월드 좌표 반환 |

### 구현상 중요한 규칙

- 타일 변경 시 `RenderDirty`, `PhysicsDirty`, `ContentVersion`이 갱신됩니다.
- bounds는 항상 즉시 다시 계산하지 않고 필요 시 갱신합니다.
---

## 8. UI 통합

이제 Core에는 UI 런타임이 의존하는 소유권 및 설정 구조가 포함됩니다.

### 8.1 `World.Ui`

모든 `World`는 `WorldUi` 형식의 `Ui` 프로퍼티를 가집니다.

이것은 해당 월드의 스크린 UI 런타임 소유자입니다.

현재 책임:

- asset으로 screen 열기
- 논리적 role로 screen 열기
- 열려 있는 canvas 닫기 및 찾기
- screen 변수 설정
- `UiScript`에 명령 보내기

이 구조는 screen을 entity로 모델링하는 대신 스크린 UI를 활성 월드에 연결해 둡니다.

### 8.2 `ProjectSettings`

이제 `ProjectSettings`에는 다음 UI 구성이 포함됩니다:

- `UiCatalog`
- `UiRoleDefaults`

이것은 공유/default UI 선택 레이어입니다.

### 8.3 `World`

이제 `World`에는 다음이 포함됩니다:

- `UiRoleOverrides`

이것은 월드마다 공통 UI role을 다른 asset으로 교체해야 할 때 사용하는 월드별 override 레이어입니다.

### 8.4 런타임 해석

런타임은 다음 순서로 UI role을 해석합니다:

1. `World.UiRoleOverrides`
2. `ProjectSettings.UiRoleDefaults`

이 순서는 현재 구현과 일치하며, `UI.OpenRole(...)`, `World.Ui.OpenRole(...)`, 그리고 관련 role 기반 API의 기준이 됩니다.

---

## 9. `ProjectSettings`

`ProjectSettings`는 프로젝트 전체에 적용되는 기본 실행/에디터/UI 설정 컨테이너입니다. 저장 위치는 프로젝트의 `Assets/ProjectSettings.json`이며, 에디터와 런타임이 같은 구조를 공유합니다.

### 9.1 어디에 쓰이는가

| 사용처 | 관련 멤버 | 설명 |
| :--- | :--- | :--- |
| `GameLoop` | `TargetTPS`, `TargetPTPS` | 월드가 custom tick 설정을 쓰지 않을 때 기본 logic/physics tick rate로 사용됩니다. |
| `PhysicsManager` | `DefaultGravity`, `DefaultFriction`, `DefaultBounciness`, `DefaultLinearDamping`, `DefaultAngularDamping`, `DefaultPhysicsThreshold` | 월드별 override가 없을 때 물리 기본값으로 사용됩니다. |
| `SpriteImportUtility` | `DefaultSpritePixelsPerUnit`, `DefaultPointFilterMaxDimension`, `DefaultSpriteSizeMode` | 새 sprite import 기본값을 만듭니다. |
| `UiSystem`, `UiRenderer` | `DefaultUiFontPath`, `DefaultUiFontGuid`, `UiRoleDefaults`, `StartupUiRoles` | UI 기본 폰트, role 바인딩, 시작 시 열 UI를 제공합니다. |
| 에디터 | `EditorFontSize`, `EditorWorldBackgroundColor`, `LastOpenedWorldAssetPath`, `EditorDockLayout` | 에디터 표시와 마지막 작업 상태를 저장합니다. |

### 9.2 일반/에디터 설정

| 이름 | 형식 | 기본값 | 설명 |
| :--- | :--- | :--- | :--- |
| `TargetTPS` | `int` | `60` | 프로젝트 기본 logic tick rate입니다. |
| `TargetPTPS` | `int` | `50` | 프로젝트 기본 physics tick rate입니다. |
| `EditorFontSize` | `float` | `18f` | 에디터 ImGui 폰트 크기입니다. |
| `EditorWorldBackgroundColor` | `Verity.Core.Color` | `new(0.15f, 0.15f, 0.15f, 1.0f)` | `WorldViewWindow` 배경색입니다. |
| `LastOpenedWorldAssetPath` | `string` | `string.Empty` | 에디터가 마지막으로 열었던 월드 asset 상대 경로입니다. |
| `EditorDockLayout` | `EditorDockLayoutSettings` | `new()` | 도킹 레이아웃과 열려 있던 창 목록을 저장합니다. |

`EditorDockLayoutSettings`는 다음 멤버를 가집니다.

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Ini` | `string` | Dear ImGui dock layout ini 문자열입니다. |
| `OpenWindowIds` | `List<string>` | 복원할 창 ID 목록입니다. |

### 9.3 Physics 설정

| 이름 | 형식 | 기본값 | 설명 |
| :--- | :--- | :--- | :--- |
| `DefaultGravity` | `Vector2` | `new(0, -9.81f)` | 월드가 custom gravity를 쓰지 않을 때의 기본 중력입니다. |
| `DefaultFriction` | `float` | `0.5f` | 물리 접촉 기본 마찰값입니다. |
| `DefaultBounciness` | `float` | `0.0f` | 물리 접촉 기본 반발값입니다. |
| `DefaultLinearDamping` | `float` | `0.1f` | `Physical`에 별도 값이 없을 때 선형 감쇠 기본값입니다. |
| `DefaultAngularDamping` | `float` | `0.1f` | `Physical`에 별도 값이 없을 때 각 감쇠 기본값입니다. |
| `DefaultPhysicsThreshold` | `float` | `0.01f` | 현재 구현에서는 sleep 판정 threshold로 사용됩니다. |
| `DefaultSleepThreshold` | `float` | `0.01f` | `ProjectSettings`에 선언되어 있지만, 현재 코드 경로에서는 직접 사용되지 않습니다. |
| `PhysicsGroups` | `List<string>` | `{"Default"}` | 충돌 그룹 이름 목록입니다. selector UI와 그룹 선택 팝업에서 사용됩니다. |

추가 메모:

- 현재 `ProjectSettings`에는 physics collision group matrix 자체는 저장되지 않습니다.
- 전역 충돌 마스크는 `PhysicsManager.CollisionMatrix`에 있으며, `ProjectSettings`는 그룹 이름 목록만 제공합니다.

### 9.4 Sprite/UI 에셋 기본값

| 이름 | 형식 | 기본값 | 설명 |
| :--- | :--- | :--- | :--- |
| `DefaultSpritePixelsPerUnit` | `int` | `32` | 새 sprite import의 기본 `PixelsPerUnit`입니다. |
| `DefaultPointFilterMaxDimension` | `int` | `256` | 텍스처 최대 변 길이가 이 값 이하이면 기본 filter를 `Point`로 선택합니다. |
| `DefaultSpriteSizeMode` | `SpriteSizingMode` | `SpriteSizingMode.FitInsideUnit` | 새 sprite import의 기본 size mode입니다. |
| `DefaultUiFontPath` | `string` | `string.Empty` | 프로젝트 기본 UI 폰트 asset 경로입니다. |
| `DefaultUiFontGuid` | `string` | `string.Empty` | 기본 UI 폰트 GUID입니다. 경로 변경 후에도 asset을 추적할 때 사용됩니다. |

### 9.5 프로젝트 정의와 UI 설정

| 이름 | 형식 | 기본값 | 설명 |
| :--- | :--- | :--- | :--- |
| `Tags` | `List<string>` | `{"Untagged", "MainCamera", "Player", "GameController"}` | 엔티티 tag 선택 목록입니다. |
| `SortingLayers` | `List<string>` | `{"Default"}` | 렌더 정렬 레이어 목록입니다. `SortingLayer.SyncWithSettings(...)`의 입력이 됩니다. |
| `UiCatalog` | `List<UiAssetReference>` | `new()` | 프로젝트 차원의 UI asset 카탈로그입니다. |
| `UiRoleDefaults` | `List<UiRoleBinding>` | `new()` | UI role 이름을 기본 `UiAsset`에 바인딩하는 목록입니다. |
| `StartupUiRoles` | `List<string>` | `new()` | 게임 시작 시 자동으로 열 role 이름 목록입니다. |

보조 타입은 다음과 같습니다.

| 타입 | 멤버 | 설명 |
| :--- | :--- | :--- |
| `UiAssetReference` | `Name`, `Asset` | UI asset을 이름으로 식별해 카탈로그에 넣는 항목입니다. |
| `UiRoleBinding` | `Role`, `Asset` | 특정 role을 어떤 `UiAsset`으로 열지 정의하는 항목입니다. |

### 9.6 입력 설정

현재 `ProjectSettings` 클래스에는 전용 입력 설정 프로퍼티가 없습니다. 입력 처리는 `Verity.Input.Input`과 `GameLoop` tick 흐름에서 직접 관리되며, 키맵/입력 축/액션 바인딩 같은 구조는 아직 `ProjectSettings`에 포함되지 않습니다.

즉, 문서상으로는 “입력 설정 섹션이 존재해야 한다”기보다 “현재 구조상 별도 입력 설정이 아직 없다”는 점을 명시하는 편이 정확합니다.

### 9.7 에디터에서 접근하는 방법

에디터에서는 프로젝트의 `Assets/ProjectSettings.json`을 선택하면 Inspector가 일반 asset preview 대신 `DrawProjectSettingsInspector()`를 열어 `ProjectSettings` 전용 편집 UI를 보여 줍니다.

실무적으로는 다음 흐름입니다.

1. Project 창에서 `ProjectSettings.json` 선택
2. Inspector에서 일반/physics/sprite/UI 목록을 편집
3. 변경 시 `SaveProjectSettings()`가 호출되어 같은 파일로 직렬화

런타임이나 에디터 코드에서 직접 접근할 때는 보통 `EditorApp.ProjectSettings`, `GameLoop.ProjectSettings`, `UiSystem.ProjectSettings`를 통해 현재 프로젝트 설정 인스턴스를 참조합니다.

### 9.8 기본 인스턴스

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `ProjectSettings.Default` | `ProjectSettings` | 새 기본 설정 인스턴스를 반환하는 convenience 프로퍼티입니다. |

---

## 10. `ObjectPool<T>`

`ObjectPool<T>`는 짧은 수명의 참조 타입 인스턴스를 재사용하기 위한 범용 풀입니다. 현재 구현은 가장 단순한 `Stack<T>` 기반이며, preallocation과 get/return 훅을 함께 제공합니다.

### 존재 이유

- 매 프레임 반복 생성되는 임시 객체의 할당과 GC 압력을 줄이기 위해
- 파티클, contact 버퍼, 임시 리스트 같은 재사용 패턴을 엔진 전역에서 통일하기 위해
- 별도 복잡한 lease 시스템 없이 `Get()` / `Return()` 두 단계만으로 충분한 곳에 쓰기 위해

### public API

| 시그니처 | 설명 |
| :--- | :--- |
| `ObjectPool(int initialCapacity = 0)` | 기본 생성자를 사용하는 풀을 만듭니다. |
| `ObjectPool(Func<T>? factory, Action<T>? onGet = null, Action<T>? onReturn = null, int initialCapacity = 0)` | 커스텀 factory와 획득/반납 훅을 포함한 풀을 만듭니다. |
| `int Count` | 현재 풀에 반환되어 대기 중인 인스턴스 수입니다. |
| `T Get()` | 반환 대기 중 인스턴스가 있으면 재사용하고, 없으면 새로 만듭니다. |
| `void Return(T item)` | 인스턴스를 풀로 돌려보냅니다. `null`은 허용되지 않습니다. |
| `void Clear()` | 현재 반환 대기 중인 인스턴스를 모두 버립니다. |

### 사용 예시

```csharp
var pool = new ObjectPool<List<int>>(
    factory: static () => [],
    onGet: static list => list.Clear(),
    onReturn: static list => list.Clear(),
    initialCapacity: 8);

List<int> buffer = pool.Get();
buffer.Add(10);
buffer.Add(20);

pool.Return(buffer);
```

### 사용 규칙

- 제네릭 제약은 `where T : class, new()`이므로 값 타입은 직접 풀링하지 않습니다.
- `initialCapacity`는 시작 시점에 실제 인스턴스를 미리 만들어 `_items`에 넣습니다.
- `onGet`, `onReturn`은 상태 초기화 지점을 명시적으로 분리하고 싶을 때 사용합니다.

### 다른 시스템과의 통합

- `ParticleSystem`은 내부 `ParticleSlot` 재사용에 `ObjectPool<ParticleSlot>`를 사용합니다.
- 현재 구현은 thread-safe가 아니며, 엔진의 단일 스레드 루프 전제에 맞는 경량 유틸리티로 보는 편이 정확합니다.

---

## 11. `SceneTransition`

`SceneTransition`은 월드 전환을 즉시 끊어 바꾸지 않고, fade-out → 로드 → fade-in 순서로 다루는 상태 객체입니다.

### 존재 이유

- 씬 전환 순간의 시각적 끊김을 줄이기 위해
- 전환 중 입력을 잠시 막아 잘못된 중복 입력을 방지하기 위해
- 로더 호출 시점과 전환 완료 시점을 명확히 분리해 다른 시스템이 이벤트로 반응할 수 있게 하기 위해

### 관련 이벤트

- `SceneTransitionCompletedEvent(string SceneName)`

### public API

| 시그니처 | 설명 |
| :--- | :--- |
| `SceneTransition(float fadeDuration = 0.25f, Action<string>? sceneLoader = null)` | fade 시간과 커스텀 로더를 지정합니다. 로더를 주지 않으면 `WorldLoader.LoadWorldByName`를 사용합니다. |
| `bool IsTransitioning` | 현재 전환 중인지 여부입니다. |
| `float FadeAlpha` | 현재 페이드 알파 값입니다. `0`은 완전 투명, `1`은 완전 불투명입니다. |
| `void TransitionTo(string sceneName)` | 지정한 씬 이름으로 전환을 시작합니다. |
| `void Update(float deltaTime)` | 현재 phase를 진행하고 필요 시 실제 씬 로드를 수행합니다. |

### 동작 순서

1. `TransitionTo(sceneName)` 호출
2. 현재 입력 상태를 저장하고 `Verity.Input.Input.Enabled = false`로 전환
3. `Update(...)`가 `FadeAlpha`를 0에서 1까지 증가시켜 fade-out 진행
4. fade-out이 끝나면 `_sceneLoader(sceneName)` 호출
5. 같은 객체가 fade-in phase로 넘어가 `FadeAlpha`를 다시 0까지 감소
6. 완료 시 입력 상태를 복원하고 `EventBus.Publish(new SceneTransitionCompletedEvent(sceneName))` 실행

### 사용 예시

```csharp
var transition = new SceneTransition(0.35f);
transition.TransitionTo("BattleScene");

// 메인 루프 또는 호스트 업데이트 경로
transition.Update(Time.DeltaTime);

float alpha = transition.FadeAlpha;
```

### 다른 시스템과의 통합

- 기본 로더는 `WorldLoader.LoadWorldByName`이므로 기존 월드 로딩 체계와 직접 연결됩니다.
- 입력 차단은 `Verity.Input.Input.Enabled`를 사용해 별도 입력 훅 없이 적용됩니다.
- 완료 알림은 `EventBus`를 통해 발행되므로 UI, 사운드, 로그 시스템이 후처리를 느슨하게 연결할 수 있습니다.

---

## 12. `SaveManager` / `SaveData`

`SaveManager`와 `SaveData`는 슬롯 기반 파일 세이브를 위한 최소 직렬화 계층입니다. 복잡한 데이터베이스나 엔진 전용 binary 포맷 대신, 사람이 읽을 수 있는 JSON 파일을 기본 저장 형식으로 사용합니다.

### 존재 이유

- 게임 진행 상태를 간단하게 저장/로드할 공용 런타임 API가 필요해서
- 스크립트와 코어 시스템이 키-값 중심 데이터 구조를 공유할 수 있어야 해서
- `System.Text.Json`의 `object` 역직렬화 한계를 우회하면서 중첩 dictionary/list 데이터를 안정적으로 round-trip하기 위해

### `SaveData` public API

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `CurrentVersion` | `const int` | 현재 저장 데이터 버전 상수입니다. |
| `Version` | `int` | 실제 저장 파일 버전입니다. |
| `Data` | `Dictionary<string, object?>` | 사용자 데이터 본문입니다. |
| `object? this[string key]` | indexer | 키 기반으로 값을 읽고 씁니다. |
| `void Set(string key, object? value)` | 메서드 | 값을 저장합니다. |
| `T Get<T>(string key)` | 메서드 | 값이 없으면 예외를 던지고, 있으면 `T`로 변환해 반환합니다. |
| `bool TryGet<T>(string key, out T? value)` | 메서드 | 값 존재 여부와 함께 안전하게 읽습니다. |

### `SaveManager` public API

| 시그니처 | 설명 |
| :--- | :--- |
| `string SaveDirectory { get; set; }` | 세이브 파일 루트 디렉터리입니다. 기본값은 `AppContext.BaseDirectory/Saves`입니다. |
| `void Save(int slot, SaveData data)` | `slot-{n}.json` 파일로 저장합니다. |
| `SaveData Load(int slot)` | 지정 슬롯 파일을 읽어 `SaveData`로 복원합니다. |
| `bool HasSave(int slot)` | 슬롯 파일 존재 여부를 반환합니다. |
| `void DeleteSave(int slot)` | 지정 슬롯 파일을 삭제합니다. |
| `int[] GetUsedSlots()` | 현재 디렉터리에서 사용 중인 슬롯 번호를 오름차순으로 반환합니다. |

### 파일 형식과 규칙

- 저장 파일명 규칙은 `slot-{slot}.json`입니다.
- JSON 루트는 `Version`과 `Data` 두 필드를 가집니다.
- 내부 `SaveDataJsonConverter`가 dictionary, array, primitive, enum string, nested object를 읽고 쓰는 역할을 담당합니다.

### 사용 예시

```csharp
var save = new SaveData();
save.Set("PlayerName", "Verity");
save.Set("Level", 12);
save.Set("Inventory", new List<string> { "Sword", "Potion" });

SaveManager.Save(0, save);

SaveData loaded = SaveManager.Load(0);
int level = loaded.Get<int>("Level");
bool hasInventory = loaded.TryGet<List<string>>("Inventory", out var inventory);
```

### 다른 시스템과의 통합

- 스크립트에서는 `SaveData`에 primitive, list, dictionary, 직렬화 가능한 객체를 담아 저장 상태를 구성할 수 있습니다.
- `SaveDirectory`를 바꾸면 테스트, 에디터, 런타임 빌드가 서로 다른 저장 경로를 쉽게 분리할 수 있습니다.
- 현재 버전 관리는 `SaveData.Version` 필드만 제공하며, 자동 migration 파이프라인은 아직 별도로 없습니다.

---

## 13. 코어 유틸리티 상호작용 메모

최근 추가된 `ObjectPool<T>`, `SceneTransition`, `SaveManager`, `EventBus`는 서로 독립된 유틸리티처럼 보이지만, 실제로는 런타임 공통 기반을 구성합니다.

- `ObjectPool<T>`는 `ParticleSystem` 같은 고빈도 업데이트 시스템의 메모리 재사용 기반입니다.
- `SceneTransition`은 `WorldLoader`와 `EventBus` 사이를 잇는 전환 orchestration 계층입니다.
- `SaveManager`는 월드/스크립트 상태를 영속화하는 파일 IO 표면입니다.
- 이 타입들은 모두 엔티티 컴포넌트가 아니며, Core 레이어의 전역 서비스성 유틸리티에 가깝습니다.
