# Verity Engine Architecture & API Reference

Verity Engine은 C# 기반의 **Entity-Component-System (ECS)** 아키텍처를 따르는 고성능 2D 게임 엔진입니다. 이 문서는 엔진의 설계 철학과 각 시스템의 작동 원리, 그리고 상세 API를 다룹니다.

---

## 🏗️ Engine Philosophy: "Simpler, Faster, Unified"

Verity는 다음과 같은 핵심 설계 원칙을 유지합니다:
1.  **Unity-Like Workflow**: 유니티 개발자에게 익숙한 컴포넌트 기반 워크플로우를 제공하면서도, 내부 구조는 더 가볍고 직렬화에 최적화되어 있습니다.
2.  **Deterministic Update**: 논리(Logic)와 물리(Physics) 주기를 프레임(Render)과 분리하여, 하드웨어 사양에 관계없이 일관된 게임 플레이 경험을 보장합니다.
3.  **Data-Driven Design**: 필터(Filter), 블루프린트(Blueprint), UI 스타일 등 대부분의 게임 데이터를 JSON 형식으로 관리하여 확장이 용이합니다.

---

## 🗺️ Architectural Map

### 1. [Core & ECS Architecture](Docs/Core.md)
엔진의 심장부입니다. **Dual-Accumulator** 기반의 실행 루프와 엔티티 계층 구조, 그리고 리플렉션 기반의 직렬화 시스템을 다룹니다.
- **Key Concept**: Logic Tick vs Physics Tick, Recursive Serialization, Entity Hierarchy.

### 2. [Scripting & Coroutine](Docs/Scripting.md)
사용자 로직이 엔진과 통신하는 방식입니다. 초기화 시 메서드를 캐싱하여 성능을 높이는 **Delegate Binding**과 비동기 처리를 위한 **Coroutine 스케줄링**을 설명합니다.
- **Key Concept**: Method Caching, Coroutine State Machine, Lifecycle Hooks.

### 3. [Physics & Spatial Optimization](Docs/Physics.md)
정밀한 충돌 판정을 위한 **SAT(Separating Axis Theorem)** 알고리즘과 대규모 객체 처리를 위한 **Spatial Hash Grid** 아키텍처를 다룹니다.
- **Key Concept**: Impulse Resolution, Sub-stepping, Grid-based Culling.

### 4. [Graphics & Rendering Pipeline](Docs/Graphics.md)
화면이 그려지는 다단계 과정입니다. **CPU 기반 소팅**, **2D 실시간 조명 및 그림자 투사**, 그리고 **Post-Processing 체인** 아키텍처를 설명합니다.
- **Key Concept**: Render Passes, 2D Shadow Mapping, Sorting Layers.

### 5. [Input & Filter System](Docs/Input.md) / [Filter API](Docs/Filter.md)
입력과 분류를 위한 **64-bit Bitmask** 아키텍처입니다. 하나의 정수 연산으로 수천 개의 그룹을 필터링하는 효율적인 마스킹 시스템을 다룹니다.
- **Key Concept**: Bitmask Mapping, Unified Input.

### 6. [Audio & Animation](Docs/Audio.md) / [Docs/Animation.md]
**SDL_mixer** 통합 아키텍처와 성능 최적화를 위한 **Animation Binding Cache** 시스템을 다룹니다.
- **Key Concept**: Voice Management, Spatial Panning, Property Interpolation.

### 7. [UI System](Docs/UI.md)
**Retained UI** 구조를 통해 에디터와 런타임 UI를 브릿지하는 아키텍처를 설명합니다.
- **Key Concept**: UiDocument Bridge, Data Binding, Layout Engine.

---

## 🚀 Execution Flow
1.  **Init**: 엔진 초기화 및 어셈블리 로드.
2.  **Load**: 월드(Scene) 파일 파싱 및 엔티티 역직렬화.
3.  **Loop**: 
    - `Accumulate Time` -> `Execute Physics Ticks` (고정 주가)
    - `Execute Logic Ticks` (고정 주기, 스크립트 업데이트)
    - `Render Frame` (가변 주기, 보간 렌더링)
4.  **Shutdown**: 리소스 해제 및 시스템 종료.
