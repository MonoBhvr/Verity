using System;
using SDL2;

namespace Verity.Core.Audio;

/// <summary>
/// SDL_mixer 오디오 장치의 로우레벨 초기화 및 해제를 담당하는 내부 시스템입니다.
/// </summary>
public static class AudioSystem
{
    private static bool _isInitialized = false;

    public static void Initialize()
    {
        if (_isInitialized) return;

        // 1. SDL 오디오 서브시스템 초기화
        if (SDL.SDL_InitSubSystem(SDL.SDL_INIT_AUDIO) < 0)
        {
            Verity.Core.Debug.LogError($"SDL 오디오 초기화 실패: {SDL.SDL_GetError()}");
            return;
        }

        // 2. 다양한 포맷 지원을 위한 코덱 초기화 (OGG, MP3, FLAC, MOD)
        var flags = SDL_mixer.MIX_InitFlags.MIX_INIT_OGG | 
                    SDL_mixer.MIX_InitFlags.MIX_INIT_MP3 | 
                    SDL_mixer.MIX_InitFlags.MIX_INIT_FLAC | 
                    SDL_mixer.MIX_InitFlags.MIX_INIT_MOD;
        
        int initResult = SDL_mixer.Mix_Init(flags);
        if ((initResult & (int)flags) != (int)flags)
        {
            Verity.Core.Debug.LogWarning($"일부 오디오 코덱 초기화에 실패했습니다. (결과: {initResult}, 기대: {(int)flags})");
        }

        // 3. 오디오 장치 열기 (44100Hz, 16bit, Stereo, 2048 buffer)
        if (SDL_mixer.Mix_OpenAudio(44100, SDL_mixer.MIX_DEFAULT_FORMAT, 2, 2048) < 0)
        {
            Verity.Core.Debug.LogError($"SDL_mixer 오디오 장치 열기 실패: {SDL.SDL_GetError()}");
            return;
        }

        // 4. 최대 채널 할당 (64개)
        SDL_mixer.Mix_AllocateChannels(64);
        _isInitialized = true;
        
        Verity.Core.Debug.Log("오디오 시스템(OGG, MP3, FLAC 지원)이 초기화되었습니다.");
    }

    public static void Shutdown()
    {
        if (!_isInitialized) return;

        SDL_mixer.Mix_CloseAudio();
        SDL_mixer.Mix_Quit(); // 코덱 시스템 종료
        SDL.SDL_QuitSubSystem(SDL.SDL_INIT_AUDIO);
        _isInitialized = false;
        
        Verity.Core.Debug.Log("오디오 시스템이 안전하게 종료되었습니다.");
    }
}
