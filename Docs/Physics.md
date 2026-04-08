# Verity 물리 문서

이 문서는 물리 엔진 구조와 스크립팅 API를 함께 설명합니다.

범위는 다음과 같습니다.

- 강체(`Physical`)와 shape 계층
- broad phase / narrow phase 구조
- contact 해석과 이벤트 dispatch
- 물리 query API

---

## 1. 물리 시스템 개요

현재 Verity 물리는 다음 단계로 동작합니다.

1. 월드에서 물리 객체 캐시 점검
2. rigid body 적분
3. spatial hash grid 재구성
4. broad phase 후보 추출
5. SAT 기반 narrow phase 충돌 판정
6. pair별 contact 정리와 impulse 해석
7. touch/detect 이벤트 dispatch

### 존재 이유

이 구조는 구현 복잡도와 성능 사이에서 균형을 잡기 위한 것입니다.

- broad phase 없이는 N^2 충돌 검사가 너무 비쌉니다.
- SAT는 2D polygon/circle 조합에 대해 비교적 단순하고 일반적입니다.
- pair 단위 contact 해석은 구현과 디버깅이 용이합니다.

---

## 2. `Physical`

`Physical`은 강체 상태를 보관하는 컴포넌트입니다.

### 존재 이유

- shape와 질량/속도/감쇠 같은 동적 상태를 분리하기 위해
- 같은 물리 body에 여러 shape를 붙일 수 있게 하기 위해

### 프로퍼티

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Mass` | `float` | 질량 |
| `Inertia` | `float` | 회전 관성 |
| `Velocity` | `Vector2` | 선속도 |
| `AngularVelocity` | `float` | 각속도 |
| `TorqueAccumulator` | `float` | 누적 토크 |
| `LinearDamping` | `float?` | 선형 감쇠 override |
| `AngularDamping` | `float?` | 각 감쇠 override |
| `Friction` | `float?` | 마찰 override |
| `Bounciness` | `float?` | 반발 override |
| `GroupName` | `string` | 물리 그룹 이름 |
| `GroupMask` | `ulong` | 그룹 비트마스크 |
| `GravityScale` | `float` | 중력 배율 |
| `SleepThreshold` | `float` | sleep 기준 |
| `IsStatic` | `bool` | 정적 body 여부 |
| `IsRotationLocked` | `bool` | 회전 잠금 여부 |

### 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `void Push(Vector2 force)` | 선형 힘 누적 |
| `void PushTorque(float torque)` | 토크 누적 |
| `void WakeUp()` | sleep 해제 |
| `bool IsTouchingAnything()` | 현재 아무것과나 접촉 중인지 |
| `bool IsTouching(string groupName)` | 특정 그룹과 접촉 중인지 |
| `bool IsTouchingGroup(string groupName)` | 그룹 기준 접촉 검사 alias |
| `bool IsTouching(Entity entity)` | 특정 엔티티와 접촉 중인지 |
| `bool IsGrounded(string groupName)` | 바닥 방향 접촉 여부 |
| `IEnumerable<Entity> GetTouchingEntities()` | 접촉 중 엔티티 열거 |
| `bool IsTouchingDirection(Vector2 direction, string? groupName = null)` | 월드 방향 접촉 검사 |
| `bool IsTouchingLocalDirection(Vector2 direction, string? groupName = null)` | 로컬 방향 접촉 검사 |
| `int GetTouchingCountDirection(Vector2 direction, string? groupName = null)` | 방향별 접촉 수 |
| `int GetTouchingCountLocalDirection(Vector2 direction, string? groupName = null)` | 로컬 방향 접촉 수 |
| `IEnumerable<Entity> GetTouchingEntitiesDirection(Vector2 direction, string? groupName = null)` | 방향별 접촉 엔티티 |
| `IEnumerable<Entity> GetTouchingEntitiesLocalDirection(Vector2 direction, string? groupName = null)` | 로컬 방향 접촉 엔티티 |

### 구현상 중요한 규칙

- `Inertia`를 명시적으로 설정하지 않으면 첫 shape 기준 계수가 사용됩니다.
- `Push` / `PushTorque`는 static body에 대해 무시됩니다.
- 매우 작은 force/torque는 잡음을 줄이기 위해 무시될 수 있습니다.

---

## 3. `PhysicalShape`

`PhysicalShape`는 충돌 형상을 나타내는 추상 베이스 타입입니다.

### 존재 이유

- body 상태(`Physical`)와 형상 정보(`Shape`)를 분리하기 위해
- circle, polygon, tilemap 같이 서로 다른 형상을 하나의 인터페이스로 다루기 위해

### 프로퍼티

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `IsSensor` | `bool` | 충돌 해석 없이 감지만 할지 여부 |
| `Offset` | `Vector2` | 로컬 오프셋 |
| `GroupName` | `string` | shape 그룹 이름 |
| `CastShadows` | `bool` | 그림자 occluder로 쓸지 여부 |
| `ShadowSelfMode` | `ShadowSelfMode` | 자기 자신 그림자 처리 방식 |
| `GroupMask` | `ulong` | 그룹 마스크 |

### 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `Vector2 GetBaseScale()` | transform과 size를 반영한 기본 스케일 계산 |
| `Vector2 GetWorldCenter()` | 월드 중심점 계산 |
| `abstract AABB GetAABB()` | broad phase용 AABB 반환 |
| `abstract Vector2[] GetVertices()` | narrow phase용 정점 반환 |
| `abstract float CalculateInertiaCoefficient()` | 관성 계수 계산 |

---

## 4. AABB

`AABB`는 broad phase에서 쓰는 축 정렬 bounding box입니다.

| 항목 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Min` | `Vector2` | 최소 좌표 |
| `Max` | `Vector2` | 최대 좌표 |
| 생성자 | `AABB(Vector2 min, Vector2 max)` | 박스 생성 |
| `Overlaps` | `bool Overlaps(AABB other)` | 다른 AABB와 겹치는지 검사 |
| `IsDefault` | `bool IsDefault()` | 기본값인지 검사 |

### 존재 이유

- SAT 같은 정밀 판정보다 훨씬 싼 broad phase 후보 판정을 위해

---

## 5. Shape 구현체

## 5.1 `BoxShape`

| 항목 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Size` | `Vector2` | 박스 크기 |

메서드:

- `override AABB GetAABB()`
- `override Vector2[] GetVertices()`
- `override float CalculateInertiaCoefficient()`

### 존재 이유

- 가장 흔한 충돌 형상을 간단하게 표현하기 위해

## 5.2 `CircleShape`

| 항목 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Radius` | `float` | 반지름 |

메서드:

- `override AABB GetAABB()`
- `override Vector2[] GetVertices()`
- `override float CalculateInertiaCoefficient()`

### 존재 이유

- 회전과 무관한 단순 충돌 형상을 싸게 표현하기 위해

## 5.3 `PolygonShape`

| 항목 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Vertices` | `List<Vector2>` | 로컬 정점 목록 |

메서드:

- `void InvalidateShapeCache()`
- `void SyncWithRenderer()`
- `bool IsSelfIntersecting()`
- `override AABB GetAABB()`
- `override Vector2[] GetVertices()`
- `List<Vector2[]> GetConvexSubShapes()`
- `int[] Triangulate()`
- `override float CalculateInertiaCoefficient()`

### 존재 이유

- 임의 형상을 표현해야 하는 경우를 지원하기 위해
- concave polygon도 내부적으로 convex 단위로 분해해 처리하기 위해

## 5.4 `TilemapShape`

메서드:

- `override AABB GetAABB()`
- `override Vector2[] GetVertices()`
- `List<Vector2[]> GetWorldPolygons()`
- `override float CalculateInertiaCoefficient()`
- `void DrawGizmos(Color color)`

### 존재 이유

- 많은 collidable tile을 개별 body로 두지 않고 타일맵 기반 shape로 묶기 위해

---

## 6. `PhysicsMath`

`PhysicsMath`는 실제 충돌 판정과 ray test를 담당하는 정적 수학 유틸리티입니다.

### 보조 구조체

#### `CollisionResult`

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `IsColliding` | `bool` | 충돌 여부 |
| `Normal` | `Vector2` | 충돌 법선 |
| `Depth` | `float` | penetration depth |
| `Contacts` | `List<Vector2>` | 접촉점 목록 |

#### `RaycastHit`

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `IsHit` | `bool` | 히트 여부 |
| `Entity` | `Entity` | 맞은 엔티티 |
| `Point` | `Vector2` | 충돌 지점 |
| `Normal` | `Vector2` | 표면 법선 |
| `Distance` | `float` | ray 원점부터 거리 |

### 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `CollisionResult TestSAT(PhysicalShape shapeA, PhysicalShape shapeB)` | shape 조합 SAT 판정 |
| `CollisionResult TestSAT(Vector2[] verticesA, Vector2[] verticesB)` | polygon-polygon 판정 |
| `CollisionResult TestSAT(CircleShape circle, Vector2[] polygonVertices)` | circle-polygon 판정 |
| `CollisionResult TestSAT(AABB box, CircleShape circle)` | AABB-circle 판정 |
| `CollisionResult TestSAT(AABB box, Vector2[] vertices)` | AABB-polygon 판정 |
| `RaycastHit TestRay(Vector2 origin, Vector2 direction, float distance, PhysicalShape shape)` | shape 대상 ray test |

---

## 7. `PhysicsManager`

`PhysicsManager`는 전역 물리 스텝과 query를 담당하는 정적 관리자입니다.

### 존재 이유

- 물리 시스템을 월드 단위 캐시와 함께 중앙에서 관리하기 위해
- 충돌 상태 맵과 이벤트 dispatch를 공통으로 다루기 위해

### 프로퍼티

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `Gravity` | `Vector2` | 현재 중력 |
| `CollisionMatrix` | `ulong[]` | 그룹 간 충돌 허용 매트릭스 |

### 보조 구조체 `Contact`

| 이름 | 형식 | 설명 |
| :--- | :--- | :--- |
| `A` | `Physical` | 첫 번째 body |
| `B` | `Physical` | 두 번째 body |
| `Normal` | `Vector2` | 충돌 법선 |
| `Depth` | `float` | penetration depth |
| `Point` | `Vector2` | 접촉점 |

### 주요 메서드

| 시그니처 | 설명 |
| :--- | :--- |
| `bool CanCollide(ulong maskA, ulong maskB)` | 충돌 가능 여부 |
| `void Step(float deltaTime, World world, ProjectSettings settings)` | 물리 스텝 수행 |
| `void DrawGizmos(World world)` | 물리 gizmo 그리기 |
| `RaycastHit Raycast(Vector2 origin, Vector2 direction, float distance, ulong mask = ulong.MaxValue, Entity? ignoreEntity = null)` | 마스크 기반 raycast |
| `RaycastHit Raycast(Vector2 origin, Vector2 direction, float distance, Entity? ignoreEntity, params string[] layerOrGroupNames)` | 이름 기반 raycast |
| `RaycastHit Raycast(Vector2 origin, Vector2 direction, float distance, params string[] layerOrGroupNames)` | 이름 기반 raycast shorthand |
| `IEnumerable<Entity> OverlapCircle(Vector2 center, float radius, ulong mask = ulong.MaxValue)` | 원형 overlap query |
| `IEnumerable<Entity> OverlapCircle(Vector2 center, float radius, params string[] layerNames)` | 이름 기반 원형 overlap |
| `IEnumerable<Entity> OverlapBox(Vector2 center, Vector2 size, ulong mask = ulong.MaxValue)` | 박스 overlap query |
| `IEnumerable<Entity> OverlapBox(Vector2 center, Vector2 size, params string[] layerNames)` | 이름 기반 박스 overlap |
| `bool IsTouchingAnything(Physical physical)` | 접촉 여부 |
| `IEnumerable<Entity> GetTouchingEntities(Physical physical)` | 접촉 엔티티 목록 |
| `bool IsTouching(Physical physical, string groupName)` | 그룹 접촉 검사 |
| `bool IsTouching(Physical physical, Entity target)` | 엔티티 접촉 검사 |
| `bool IsTouchingDirection(Physical physical, Vector2 direction, string? groupName = null)` | 방향 접촉 검사 |
| `bool IsTouchingLocalDirection(Physical physical, Vector2 localDirection, string? groupName = null)` | 로컬 방향 접촉 검사 |
| `int GetTouchingCountDirection(Physical physical, Vector2 direction, string? groupName = null)` | 방향 접촉 수 |
| `int GetTouchingCountLocalDirection(Physical physical, Vector2 localDirection, string? groupName = null)` | 로컬 방향 접촉 수 |
| `IEnumerable<Entity> GetTouchingEntitiesDirection(Physical physical, Vector2 direction, string? groupName = null)` | 방향 접촉 엔티티 |
| `IEnumerable<Entity> GetTouchingEntitiesLocalDirection(Physical physical, Vector2 localDirection, string? groupName = null)` | 로컬 방향 접촉 엔티티 |
| `bool IsGrounded(Physical physical, string groupName)` | 바닥 접촉 검사 |

### 구현상 중요한 구조 변화

- 물리 객체/shape 캐시는 `World.StateVersion`이 바뀔 때만 재구축됩니다.
- contact pair grouping에서 LINQ 기반 임시 할당을 제거했습니다.
- 한 엔티티의 다중 `PhysicalShape`를 모두 처리합니다.

### 현재 남아 있는 제약

- spatial grid는 여전히 sub-step마다 다시 구축됩니다.
- continuous collision detection은 없습니다.
- rigid body island 분리와 warm starting은 아직 없습니다.

