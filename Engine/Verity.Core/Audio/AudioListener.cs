using Verity.Core.ECS;

namespace Verity.Core.Audio;

/// <summary>
/// 오디오 리스너는 소리를 듣는 역할을 수행합니다. (보통 메인 카메라 엔티티에 부착)
/// </summary>
public class AudioListener : Component
{
    // 리스너가 엔티티에 부착되어 있으므로 Transform.Position을 통해 리스너의 위치를 가져옵니다.
}
