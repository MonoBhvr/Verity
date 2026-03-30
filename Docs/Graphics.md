# Graphics & Rendering Pipeline Architecture

Verity의 그래픽 아키텍처는 고전적인 2D 드로우 콜 방식에 현대적인 멀티 패스 조명 모델을 결합하여 가볍지만 미려한 시각 효과를 제공합니다.

---

## 🏗️ System Architecture

### 1. Multi-Pass Render Pipeline
화면 하나가 완성되기 위해 엔진은 다음과 같은 독립적인 렌더링 단계를 거칩니다.

1.  **World Pass (Base)**: 모든 스프라이트와 타일맵을 정렬된 순서대로 FBO(Framebuffer Object)에 그립니다.
2.  **Shadow Pass (Stencil/Light)**: 빛 차폐체(Occluder)의 정점 정보를 셰이더로 전달하여, 광원에서 투사된 기하학적 그림자 영역을 계산하고 마스킹합니다.
3.  **Lighting Pass (Additive)**: 가려지지 않은 영역에 광원 색상을 더하여 실시간 조명 효과를 입힙니다.
4.  **Post-Process Chain**: 완성된 전체 텍스처에 Bloom, Vignette, Blur 등의 화면 효과를 순차적으로 적용하여 최종 화면을 출력합니다.

### 2. CPU-Based Sorting Layers
Verity는 GPU의 깊이 테스트(Depth Test) 대신 CPU에서 소팅을 수행하는 2D 정석 방식을 따릅니다.

- **Sorting Layers**: 사용자가 정의한 레이어(예: Background, Foreground) 순서대로 객체를 그룹화합니다.
- **Order In Layer**: 동일 레이어 내에서 정수 값으로 순서를 결정합니다.
- **Y-Axis Sorting**: 탑다운 뷰 게임을 위해 Y축 좌표를 기반으로 동적 소팅을 수행하여 캐릭터 간의 앞뒤 관계를 자동으로 처리합니다.

### 3. 2D Real-time Shadow Mapping
그림자 시스템은 단순히 반투명 이미지를 그리는 것이 아니라, 기하학적 아키텍처를 가집니다.

- **Vertex Projection**: 광원 위치로부터 다각형의 정점들을 바깥쪽으로 무한히 투사하여 그림자 볼륨(Shadow Volume)을 생성합니다.
- **Shadow Occluder**: 물리 쉐이프(PolygonShape)를 그림자 차폐 데이터로 직접 사용할 수 있어, 물리적 형태와 그림자 형태가 완벽히 일치하도록 설계되었습니다.

---

## 📚 Graphics API Reference

(기존 그래픽 API 명세 유지...)

### Camera (`Verity.Graphics.Camera`)
| Name | Type | Description |
| :--- | :--- | :--- |
| `OrthographicSize` | `float` | 카메라 시야 수직 크기. |
| `Zoom` | `float` | 추가 배율. |

(이하 생략 - 이전 API 명세와 동일하게 유지)
