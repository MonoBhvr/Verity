# Verity UI System (`Verity.Core.UI`)

Verity의 UI 시스템은 **런타임 UI**와 **에디터 저작 UI**를 명확히 분리합니다.

- 런타임은 `Verity.Core.UI` 네임스페이스의 **retained UI 트리**를 사용합니다.
- 에디터는 ImGui 기반 전용 창(`UIEditorWindow`)을 사용해 자산을 편집합니다.

## 1. High-Level Architecture

런타임 UI는 다음 레이어로 구성됩니다:
1. **Asset Layer**: `.ui`, `.uiprefab`, `.uistyle` 파일.
2. **Document Layer**: `UiDocument`가 월드 엔티티에 부착되어 UI 자산을 소유.
3. **Canvas Layer**: `UiSystem.ShowScreen()`이 생성하는 활성 UI 인스턴스.
4. **Layout/Input Layer**: 레이아웃 계산, 데이터 바인딩, 입력 처리.
5. **Render Layer**: `UiRenderer`가 노드 순회하며 렌더링.

---

## 2. 핵심 데이터 모델

### 2.1. UI 자산 종류
- **UIScreenAsset (.ui)**: 화면 전체 트리.
- **UiPrefabAsset (.uiprefab)**: 재사용 가능한 UI 조각.
- **UiStyleAsset (.uistyle)**: 상태/토큰 기반 스타일 데이터.

### 2.2. UiNode Tree
모든 UI 노드의 공통 부모입니다.
- **Container Family**: `Panel`, `ScrollView`, `ListView`, `Window` 등.
- **Text / Visual Family**: `Label`, `RichText`, `Image`, `Spacer` 등.
- **Control Family**: `Button`, `Toggle`, `InputField`, `Slider` 등.

---

## 3. Data Binding & Events

### 3.1. Binding System
- **Path**: 소스 경로 (예: `Hud:InventoryPresenter.Count`).
- **Mode**: `OneWay` (소스 -> UI), `TwoWay` (양방향).

### 3.2. Event Actions
이벤트(Click, ValueChanged 등) 발생 시 특정 메서드를 호출합니다.
- **Target 문법**: `self`, `binding:Key`, `entity:Name`, `tag:TagName` 등 지원.

---

## 4. Runtime Services

### 4.1. Canvas
하나의 활성 UI 화면 인스턴스입니다. 레이아웃, 바인딩, 입력을 독립적으로 처리합니다.

### 4.2. UiLayoutEngine
`HorizontalStack`, `VerticalStack`, `Grid`, `ScrollContent` 등의 레이아웃 모드를 처리합니다.

### 4.3. UiSystem
전역 UI 런타임 매니저로, 캔버스 생성/제거 및 전역 바인딩 레지스트리를 관리합니다.

---

## 5. ECS Bridge (`UiDocument`)

월드 엔티티에 부착되어 UI를 로드하고 표시합니다.
- **Binding Namespace**: 엔티티와 컴포넌트를 UI에 자동으로 바인딩하기 위한 네임스페이스를 관리합니다.
- **AutoShow**: 런타임 시 자동으로 UI를 띄울지 여부를 결정합니다.

---

## 6. 에디터 저작 (UI Editor)

**Window > UI Editor** 메뉴를 통해 직관적인 UI 제작 환경을 제공합니다.
- **Hierarchy & Inspector**: 노드 구조 편집 및 속성 설정.
- **Screen Overlay**: 월드 렌더링 위에 UI 프리뷰를 오버레이하여 확인 가능.
- **Prefab System**: 복잡한 UI를 조각별로 저장하여 재사용.
