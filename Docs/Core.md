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

## 8. UI Integration

Core now contains the ownership and settings structures that the UI runtime depends on.

### 8.1 `World.Ui`

Every `World` has a `Ui` property of type `WorldUi`.

This is the runtime owner of screen UI for that world.

Current responsibilities:

- open screens by asset
- open screens by logical role
- close and find open canvases
- set screen variables
- send commands to `UiScript`

This keeps screen UI attached to the active world instead of modeling screens as entities.

### 8.2 `ProjectSettings`

`ProjectSettings` now includes UI configuration:

- `UiCatalog`
- `UiRoleDefaults`

This is the shared/default UI selection layer.

### 8.3 `World`

`World` now includes:

- `UiRoleOverrides`

This is the per-world override layer used when a world needs to replace a common UI role with another asset.

### 8.4 Runtime resolution

The runtime resolves UI roles in this order:

1. `World.UiRoleOverrides`
2. `ProjectSettings.UiRoleDefaults`

This matches the current implementation and is the basis of `UI.OpenRole(...)`, `World.Ui.OpenRole(...)`, and the related role-based APIs.
