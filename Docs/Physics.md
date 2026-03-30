# Physics & Spatial Optimization Architecture

Verity의 물리 엔진은 실시간 충돌 판정과 반응을 처리하기 위해 정교한 수학적 모델과 공간 분할 최적화 기법을 결합한 아키텍처를 가지고 있습니다.

---

## 🏗️ System Architecture

### 1. SAT (Separating Axis Theorem) 알고리즘
Verity는 볼록 다각형(Convex Polygon) 간의 충돌 판정을 위해 SAT 알고리즘을 사용합니다.

- **Axis Projection**: 두 물체의 모든 변에 수직인 법선을 분리축으로 설정하고, 각 물체를 투영하여 겹치는 구간을 확인합니다.
- **MTV (Minimum Translation Vector)**: 모든 축에서 겹침이 발생할 경우, 그중 가장 적게 겹친 축의 방향(Normal)과 깊이(Depth)를 추출하여 충돌 해소에 사용합니다.
- **Circle-Polygon Integration**: 원의 경우 다각형의 각 변뿐만 아니라 다각형의 각 정점과의 거리까지 축으로 고려하여 정밀한 판정을 수행합니다.

### 2. Spatial Hash Grid (공간 분할 최적화)
수많은 물리 객체를 매 프레임 전수 조사(`O(N^2)`)하는 것은 불가능하므로, Verity는 격자 기반의 공간 분할 기법을 사용합니다.

- **Grid Cell Mapping**: 월드를 고정된 크기의 격자(Grid)로 추상화하고, 각 물리 객체의 위치에 따라 속한 격자 키값을 해시맵에 저장합니다.
- **Narrow Phase Culling**: 충돌 검사 시 자신의 격자와 인접한 격자에 속한 객체들만 검사 대상으로 선별하여 성능을 극대화(`O(N)`)합니다.
- **Static vs Dynamic**: 고정 지형지물(Static)은 한 번만 격자에 등록하고, 움직이는 물체(Dynamic)는 매 틱 위치가 변할 때마다 격자 정보를 갱신하는 이원화된 설계입니다.

### 3. Impulse-Based Resolution
충돌이 감지되면 엔진은 뉴턴의 운동 법칙을 기반으로 객체의 속도를 보정합니다.

- **Impulse Response**: 충돌 지점의 법선 방향으로 두 물체의 질량과 탄성 계수를 고려한 충격량(Impulse)을 즉각 대입하여 튕겨나가는 속도를 계산합니다.
- **Position Projection**: 물체가 서로 겹쳐 있는 깊이(Depth)만큼 위치를 강제로 밀어내어(Separation), 중복 충돌 판정이 일어나는 것을 방지합니다.
- **Sleep System**: 에너지가 일정 임계값 이하로 떨어진 객체는 연산에서 제외하여 CPU 자원을 절약합니다.

---

## 📚 Physics API Reference

(기존 물리 API 명세 유지...)

### Physical Component (`Verity.Core.Physics.Physical`)
| Name | Type | Description |
| :--- | :--- | :--- |
| `Mass` / `Inertia` | `float` | 질량 및 관성 모멘트. |
| `Velocity` / `AngularVelocity` | `Vector2` / `float` | 선속도 및 각속도. |

(이하 생략 - 이전 API 명세와 동일하게 유지)
