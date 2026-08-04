// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System;
using UnityEngine;

namespace DomoNinja.Unity
{
    /// <summary>
    /// BGM/SFX 볼륨·음소거 상태를 들고 있다. <b>실제 재생은 아직 없다</b> —
    /// 오디오 클립을 붙이는 건 별도 작업이고, 여기는 그때 읽을 값과 저장을 미리 만들어 둔 것이다.
    /// </summary>
    /// <remarks>
    /// <see cref="RunManager"/>와 같은 패턴(씬 전역 싱글톤, <c>DontDestroyOnLoad</c>).
    /// 저장은 <c>PlayerPrefs</c> — 메타 진행도(`meta.json`, 서버/로컬스토리지)와는 다른 층이다.
    /// 볼륨 설정은 게임 상태가 아니라 브라우저(기기) 단위 환경설정이라 굳이 같은 저장소에 둘 이유가 없다.
    /// </remarks>
    public sealed class AudioManager : MonoBehaviour
    {
        private const string BgmVolumeKey = "audio.bgmVolume";
        private const string SfxVolumeKey = "audio.sfxVolume";
        private const string BgmMutedKey = "audio.bgmMuted";
        private const string SfxMutedKey = "audio.sfxMuted";

        public static AudioManager? Instance { get; private set; }

        public float BgmVolume { get; private set; } = 1f;
        public float SfxVolume { get; private set; } = 1f;
        public bool BgmMuted { get; private set; }
        public bool SfxMuted { get; private set; }

        /// <summary>값이 바뀔 때마다 알린다. 설정 UI가 이걸 구독해 슬라이더/버튼 표시를 맞춘다.</summary>
        public event Action? Changed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            BgmVolume = PlayerPrefs.GetFloat(BgmVolumeKey, 1f);
            SfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, 1f);
            BgmMuted = PlayerPrefs.GetInt(BgmMutedKey, 0) == 1;
            SfxMuted = PlayerPrefs.GetInt(SfxMutedKey, 0) == 1;
        }

        public void SetBgmVolume(float value)
        {
            BgmVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(BgmVolumeKey, BgmVolume);
            Changed?.Invoke();
        }

        public void SetSfxVolume(float value)
        {
            SfxVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(SfxVolumeKey, SfxVolume);
            Changed?.Invoke();
        }

        public void ToggleBgmMute()
        {
            BgmMuted = !BgmMuted;
            PlayerPrefs.SetInt(BgmMutedKey, BgmMuted ? 1 : 0);
            Changed?.Invoke();
        }

        public void ToggleSfxMute()
        {
            SfxMuted = !SfxMuted;
            PlayerPrefs.SetInt(SfxMutedKey, SfxMuted ? 1 : 0);
            Changed?.Invoke();
        }
    }
}
