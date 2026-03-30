# Core & ECS Architecture

Verity Engine의 핵심 아키텍처는 **Entity-Component-System(ECS)** 패턴을 경량화하여 적용하였으며, 프레임 가변성에 유연하게 대처하는 실행 루프를 가지고 있습니다.

---

## 🏗️ System Architecture

### 1. Dual-Accumulator Game Loop
Verity는 성능이 다른 여러 환경에서 동일한 게임 물리와 로직을 보장하기 위해 두 개의 독립적인 누적기(Accumulator)를 사용합니다.

- **Logic Accumulator**: `Time.TargetTPS`(기본 60) 주기로 논리 업데이트를 수행합니다. 델타 타임이 누적되면 한 프레임 내에서도 여러 번의 `Update`가 실행될 수 있어 일정한 로직 속도를 보장합니다.
- **Physics Accumulator**: `Time.TargetPTPS`(기본 50) 주기로 물리 시뮬레이션을 수행합니다. 물리 연산은 논리보다 더 엄격한 고정 주기를 가져야 안정적이므로 별도로 관리됩니다.
- **Render Frame**: 누적기와 관계없이 시스템이 허용하는 최대 속도로 화면을 그립니다. 논리/물리 틱 사이의 시간을 계산하여 부드러운 보간 렌더링이 가능하도록 설계되었습니다.

### 2. Entity-Component Architecture
- **Entity as Container**: 엔티티는 자체적인 로직이나 데이터를 거의 가지지 않는 가벼운 컨테이너입니다. 유일하게 강제되는 데이터는 공간 정보인 `Transform`입니다.
- **Component-Based Logic**: 모든 실질적인 데이터와 동작은 컴포넌트에 정의됩니다. `Script` 역시 하나의 컴포넌트이며, 엔진은 엔티티 내부의 컴포넌트 리스트를 순회하며 필요한 정보를 추출합니다.
- **Hierarchical Transform**: 부모-자식 관계는 트랜스폼을 통해 관리되며, 행렬 곱셈을 통해 하위 계층으로 갈수록 변환이 누적되는 트리 구조 아키텍처입니다.

### 3. Reflection-Based Serialization (`SceneSerializer`)
Verity는 게임 상태를 JSON으로 저장하고 불러오기 위해 리플렉션을 활용한 재귀적 직렬화 시스템을 사용합니다.

- **Member Collection**: `[SerializeField]` 특성이 붙은 멤버를 런타임에 찾아 데이터 트리를 구성합니다.
- **Type Discovery**: 저장된 타입 이름을 기반으로 어셈블리 내에서 클래스를 찾아 인스턴스화합니다. 이는 유저가 작성한 커스텀 스크립트에도 동일하게 적용됩니다.
- **Guid Tracking**: 모든 엔티티와 컴포넌트는 고유한 `Guid`를 가져, 직렬화 후에도 객체 간의 참조(Reference) 관계를 정확하게 복구할 수 있습니다.

---

## 📚 Core API Reference

(기존 API 테이블 내용 유지...)

### Entity (`Verity.Core.ECS.Entity`)
| Name | Type | Description |
| :--- | :--- | :--- |
| `Id` | `Guid` | 엔티티 고유 식별자. |
| `Name` | `string` | 엔티티 이름. |
| `Tag` | `string` | 엔티티 태그. |
| `Active` | `bool` | 활성화 여부. |
| `Transform` | `Transform` | 공간 정보 컴포넌트. |

(이하 생략 - 이전 API 명세와 동일하게 유지)
