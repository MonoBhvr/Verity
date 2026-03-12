# Verity Physics Engine Design Specification

Verity 엔진의 강력한 2D 물리 엔진 설계 문서입니다.
초기 설계 문서로, 현재와 차이가 있을 수 있습니다.

## 1. 핵심 네이밍 가이드 (Naming Convention)

| 기존 용어 | Verity 용어 | 설명 |
| :--- | :--- | :--- |
| Rigidbody | **Physical** | 물리 법칙이 적용되는 실체 컴포넌트 |
| Shape | **PhysicalShape** | 충돌 영역을 정의하는 컴포넌트의 기반 |
| Box/Circle Shape | **BoxShape / CircleShape** | 상자 및 원형 충돌 형태 |
| Polygon Shape | **PolygonShape** | 다각형 충돌 형태 (SAT 알고리즘 사용) |
| Trigger | **Sensor** | 물리적 충돌 없이 감지만 수행하는 설정 |
| Restitution | **Bounciness** | 튕기는 정도 (0~1) |
| Layer / Filter | **Group** | 충돌 여부를 결정하는 그룹 (Matrix 기반) |
| Impulse | **Push** | 순간적인 힘을 가하는 메서드 (`Physical.Push()`) |
| Mass | **Mass** | 물체의 무게/질량 |
| Friction | **Friction** | 마찰 계수 |

---

## 2. 컴포넌트 상세 설계

### 2.1 Physical Component
물체의 운동학적 상태를 관리합니다.
- **Properties:**
    - `Mass`: 무게.
    - `Velocity`: 현재 이동 속도 (Vector2).
    - `Bounciness`: 탄성.
    - `Friction`: 마찰력.
    - `Group`: 소속된 충돌 그룹.
    - `GravityScale`: 개별 중력 배율 (기본 1.0).
    - `SleepThreshold`: 해당 속도 이하로 일정 시간 유지 시 연산 중지 (성능 최적화).
    - `IsStatic`: 고정된 물체(벽, 바닥) 여부.
    - `IsRotationLocked`: 물리 연산에 의한 회전 방지 여부.
- **Methods:**
    - `Push(Vector2 force)`: 물체에 힘을 가함.
    - `IsTouchingAnything()`: 현재 어떤 물체와든 닿아 있는가?
    - `IsTouching(Group group)`: 특정 그룹의 물체와 닿아 있는가?
    - `IsTouching(Entity entity)`: 특정 엔티티와 닿아 있는가?
    - `GetTouchingEntities()`: 현재 닿아 있는 모든 엔티티를 배열로 반환.

### 2.2 PhysicalShape (Base)
충돌 범위를 결정합니다. 에디터에서 **Physics Gizmos**를 통해 시각화 및 편집(Polygon 정점 편집 등)이 가능합니다.
- **Properties:**
    - `IsSensor`: true일 경우 `Sensor` 모드로 동작 (물리적 밀려남 없음).
    - `Offset`: Transform 위치로부터의 상대적 오프셋.
- **Subtypes:**
    - `BoxShape`: 가로, 세로 크기 지정.
    - `CircleShape`: 반지름 지정.
    - `PolygonShape`: 정점(Vertices) 배열로 자유로운 모양 생성.

---

## 3. 물리 쿼리 시스템 (Physics Query)

코드 어디서든 물리 환경을 검사할 수 있는 정적 시스템입니다.

- **Raycast:**
    - `RaycastHit Physics.Raycast(Vector2 origin, Vector2 direction, float distance, Group group)`
    - 지정된 방향으로 선을 쏘아 가장 먼저 닿는 물체 정보 반환.
  - **Overlap Checks:**
      - `Entity[] Physics.OverlapCircle(Vector2 center, float radius, Group group)`
      - `Entity[] Physics.OverlapBox(Vector2 center, Vector2 size, Group group)`
      - 특정 범위 내에 있는 모든 엔티티 목록 반환.

---

## 4. 물리 파이프라인 (Physics Pipeline)

1.  **Fixed Update:** `GameLoop`에서 프레임 레이트와 독립적인 고정 시간 간격으로 실행.
2. **Broad Phase (Spatial Hashing Optimization):**
    - 월드를 일정한 크기의 격자(Grid)로 관리하되, 메모리 효율을 위해 해시맵(Dictionary)을 사용.
    - 모든 `Physical` 오브젝트는 자신의 위치에 해당하는 해시 칸(Cell)에 등록됨.
    - 충돌 검사 시, 자신과 인접한 8개의 칸에 있는 오브젝트들하고만 정밀 판정을 수행하여 연산량을 획기적으로 절감.
    - 월드의 크기가 무한히 넓어져도 물체 밀도에 따라 성능이 일정하게 유지됨.

3.  **Narrow Phase (SAT - Separating Axis Theorem):**
    - AABB가 겹치는 경우, 볼록 다각형(Convex Polygon) 간의 정확한 충돌 지점 및 깊이(MTV) 계산.
4.  **Collision Resolution:**
    - 계산된 데이터를 바탕으로 속도 변경 및 위치 보정 수행.
5.  **Event Dispatching:**
    - 충돌 상태에 따라 적절한 이벤트 함수 호출.

---

## 5. 이벤트 및 스크립팅 (Events)

### Collision (IsSensor = false)
- `OnTouched(Collision collision)`: 처음 부딪혔을 때.
- `OnTouching(Collision collision)`: 부딪히고 있을 때 (매 프레임).
- `OnTouchEnd(Entity other)`: 떨어졌을 때.

### Sensor (IsSensor = true)
- `OnDetected(Entity other)`: 감지 범위에 들어왔을 때.
- `OnDetecting(Entity other)`: 감지 범위 안에 있을 때.
- `OnDetectEnd(Entity other)`: 감지 범위를 벗어났을 때.

---

## 6. 설정 및 관리 (Configuration)

- **Project Physical Settings:**
    - `ProjectSettings.json`에 저장.
    - 기본 중력(Gravity), 기본 마찰력, **Group Collision Matrix**(그룹 간 충돌 허용 여부) 정의.
- **World Physical Settings:**
    - `scene.json` (월드 파일)에 저장.
    - 해당 월드의 중력 방향, 강도 등 프로젝트 기본값을 오버라이드.
