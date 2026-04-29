# Multi-Camera / Camera Texture / Multi-Window Plan

## Goal

Verity의 기본 카메라 사용 경험은 초보자에게 단순해야 한다. 여러 카메라, 렌더 텍스쳐, 다중 창은 고급 선택 기능으로 분리한다.

핵심 원칙:

- `Camera`는 "씬을 보는 컴포넌트"로 유지한다.
- 고급 출력 설정은 `CameraOutput`이 담당한다.
- 렌더 텍스쳐는 파일 에셋이면서 스크립트 타입인 `TextureAsset` 계열로 다룬다.
- 월드 업데이트와 물리/스크립트 계산은 한 번만 수행하고, 여러 출력은 렌더링만 추가 수행한다.

## Camera

`Camera`는 계속 간결하게 유지한다.

책임:

- 위치, 회전, 투영, 배경색, 종횡비, 후처리 같은 뷰 설정
- 씬을 어떤 방식으로 볼지 결정

책임이 아닌 것:

- 렌더 텍스쳐 파일 관리
- 다중 창 생성과 배치
- 출력 대상 라우팅

기본 화면에 사용할 카메라는 단순 규칙으로 선택한다.

- `CameraOutput.Target == MainWindow`인 카메라 우선
- 없으면 `MainCamera` 태그 우선
- 없으면 첫 번째 활성 카메라 사용
- `RenderTexture` 전용 카메라는 기본 메인 화면 후보에서 제외

## Texture Assets

렌더 텍스쳐는 독립된 출력 이름만으로 관리하지 않고 에셋으로 관리한다.

타입 구조:

- `TextureAsset`: 이미지나 렌더 텍스쳐를 모두 가리킬 수 있는 기본 텍스쳐 에셋 참조
- `CameraTextureAsset`: `TextureAsset`을 상속한 카메라 출력 전용 텍스쳐 에셋
- `CameraTextureAssetData`: `.rendertexture` 파일에 저장되는 폭, 높이, 필터 설정

사용 방식:

- `.png`, `.jpg`, `.jpeg`는 일반 `TextureAsset`으로 사용할 수 있다.
- `.rendertexture`는 `CameraTextureAsset` 또는 일반 `TextureAsset`으로 사용할 수 있다.
- 스프라이트 렌더러와 UI 이미지는 `TextureAsset`을 통해 카메라 출력 결과를 표시할 수 있다.
- 스크립트에서는 `CameraTextureAsset.Resize(...)`, `LoadSettings(...)`, `SaveSettings(...)`로 설정을 변경할 수 있다.

## CameraOutput

`CameraOutput`은 카메라가 어디로 렌더링될지 정하는 고급 컴포넌트다.

주요 필드:

- `Target`: `MainWindow`, `RenderTexture`, `Window`
- `Primary`: 기본 후보 여부
- `Order`: 출력 렌더 순서
- `OutputName`: 수동 출력 이름
- `TargetTexture`: `.rendertexture` 에셋 참조

`TargetTexture`가 있으면 렌더 결과는 해당 에셋 경로를 키로 저장된다. 그래서 스프라이트나 UI는 같은 `.rendertexture` 에셋을 `TextureAsset`으로 지정해 결과를 표시할 수 있다.

## Render Flow

프레임 흐름:

1. 월드 로직, 물리, 애니메이션, 스크립트를 한 번 업데이트한다.
2. `CameraOutput.Target == RenderTexture`인 카메라들을 순서대로 오프스크린 렌더링한다.
3. 메인 화면 카메라를 선택해 화면에 렌더링한다.
4. 스프라이트/UI가 `TextureAsset`으로 `.rendertexture`를 참조하면, 해당 카메라 출력 결과를 사용한다.

이 구조는 같은 씬을 여러 카메라가 보더라도 게임 계산을 중복하지 않는다.

## Multi-Window

다중 창은 선택 기능으로 둔다.

방향:

- Windows에서는 실제 OS 창을 여러 개 만들 수 있게 한다.
- 각 창은 특정 카메라 또는 `CameraOutput`을 할당받는다.
- 창 크기, 비율, 위치는 창 설정에서 관리한다.
- 웹에서는 실제 iframe보다는 동일 런타임 안의 여러 view/canvas 또는 iframe처럼 보이는 sub-view가 적합하다.

다중 창은 고급 기능이므로 설정 UI가 복잡해도 된다. 단, 기본 프로젝트에서는 노출되지 않아야 한다.

## Implementation Phases

1. 여러 카메라 허용과 기본 카메라 선택 규칙 정리
2. `CameraOutput` 추가와 렌더 텍스쳐 출력
3. `TextureAsset` / `CameraTextureAsset` 에셋화
4. 스프라이트와 UI에서 `TextureAsset` 사용
5. 선택 기능으로 다중 창 추가

## Current Scope

현재 구현 범위는 1-5단계의 엔진/에디터 기반까지다.

- 여러 카메라를 둘 수 있다.
- `CameraOutput`으로 메인 화면과 렌더 텍스쳐 출력을 구분한다.
- `.rendertexture` 파일을 만들고 인스펙터에서 크기/필터를 편집한다.
- `TextureAsset`으로 일반 이미지와 렌더 텍스쳐를 스프라이트/UI에 사용할 수 있다.
- `CameraOutput.Target == Window` 출력은 별도 렌더 표면으로 생성되고, 에디터의 Camera Outputs 창에서 여러 출력 뷰로 확인할 수 있다.
- Windows 네이티브 OS 창 생성은 아직 백엔드 후속 단계다. 현재 구현은 웹 sub-view와 에디터 다중 출력 창에 바로 대응되는 구조다.
