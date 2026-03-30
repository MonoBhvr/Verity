# Verity Audio API Reference

## 1. AudioSource (`Verity.Core.Audio.AudioSource`)

### Properties
| Name | Type | Description |
| :--- | :--- | :--- |
| `Clip` | `AudioClip` | 재생할 오디오 클립 에셋입니다. |
| `GroupName` | `string` | 오디오 그룹 (SFX, BGM 등) 이름입니다. |
| `Volume / Pitch` | `float` | 볼륨(0~1) 및 재생 속도 배율입니다. |
| `Loop / PlayOnStart` | `bool` | 반복 여부 및 활성화 시 자동 재생 여부입니다. |
| `IsSpatial / Mute` | `bool` | 3D 공간 음향 적용 여부 및 음소거 여부입니다. |
| `Min / Max Pitch` | `float` | 재생 시마다 적용될 무작위 피치 범위입니다. |
| `Min / Max Volume` | `float` | 재생 시마다 적용될 무작위 볼륨 범위입니다. |
| `Min / Max Distance` | `float` | 공간 음향의 볼륨 감쇄가 시작/종료되는 거리입니다. |

### Methods
- `Play()`: 소리 재생 시작.
- `Stop()`: 소리 재생 중단.
- `Pause / Resume()`: 일시 정지 및 재개.
- `PlayOneShot(clip, scale)`: 지정된 클립을 현재 소스의 위치에서 중첩 재생.

---

## 2. AudioManager (Static)
전역 오디오 엔진 제어 및 그룹 관리를 수행합니다.

### Static Methods
- `PlayOneShot(clip, group, scale, pos, min, max)`: 특정 위치나 그룹 설정으로 클립을 즉시 재생.
- `SetGroupVolume / Pitch(name, val)`: 그룹 내 모든 소리의 볼륨/피치를 일괄 조절.
- `StopGroup(name)`: 특정 그룹의 모든 소리를 즉시 정지.
- `MuteGroup(name, mute)`: 특정 그룹을 음소거하거나 해제.
