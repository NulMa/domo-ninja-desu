// Unity 는 Directory.Build.props 를 읽지 않는다 — 파일마다 nullable 문맥을 명시한다.
#nullable enable

using System;
using UnityEngine;

namespace DomoNinja.Unity
{
    /// <summary>
    /// BGM/SFX 를 재생하고 볼륨·음소거 상태를 들고 있다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="RunManager"/>와 같은 패턴(씬 전역 싱글톤, <c>DontDestroyOnLoad</c>).
    /// 저장은 <c>PlayerPrefs</c> — 메타 진행도(`meta.json`)와는 다른 층이다.
    /// 볼륨 설정은 게임 상태가 아니라 브라우저(기기) 단위 환경설정이라 같은 저장소에 둘 이유가 없다.
    /// </para>
    /// <para>
    /// ★ <b>브라우저는 사용자가 한 번 건드리기 전까지 소리를 막는다.</b> 자동재생 정책이라
    /// 코드로 우회할 수 없다. 다만 <b>우리가 재생을 미룰 필요는 없다</b> —
    /// Unity WebGL 런타임이 첫 입력에서 오디오 컨텍스트를 스스로 깨우고, 그때 이미 돌고 있던 소리가 들린다.
    /// </para>
    /// <para>
    /// 처음엔 첫 입력까지 <b>재생을 붙잡아 뒀는데, 그러면 타이틀 음악을 아무도 못 듣는다</b> —
    /// 타이틀에서 누를 수 있는 건 화면 자체뿐이고, 그걸 누르면 타이틀을 <b>떠나기</b> 때문이다.
    /// 그래서 요청이 오면 바로 튼다. 브라우저가 막는 동안은 조용하고 첫 탭에 소리가 붙는다.
    /// </para>
    /// <para>
    /// 클립은 <see cref="AudioCatalog"/> 에서 이름으로 꺼낸다. 표에 없으면 <b>조용히 건너뛴다</b> —
    /// 소리가 없다고 게임이 멈추면 안 되고, 대신 없는 이름은 한 번만 로그로 남긴다.
    /// </para>
    /// </remarks>
    public sealed class AudioManager : MonoBehaviour
    {
        private const string BgmVolumeKey = "audio.bgmVolume";
        private const string SfxVolumeKey = "audio.sfxVolume";
        private const string BgmMutedKey = "audio.bgmMuted";
        private const string SfxMutedKey = "audio.sfxMuted";

        /// <summary>
        /// 처음 켰을 때의 볼륨. <b>가득이 아니다.</b>
        /// </summary>
        /// <remarks>
        /// 웹은 심사자가 <b>다른 탭을 열어둔 채</b> 게임을 연다. 1.0 으로 시작하면 첫 소리가 놀랄 만큼 크고,
        /// 그때 사람이 먼저 하는 일은 볼륨을 줄이는 게 아니라 <b>탭을 닫는 것</b>이다.
        /// 설정에서 올릴 수는 있어도, 한 번 닫힌 창은 돌아오지 않는다.
        /// </remarks>
        private const float DefaultVolume = 0.3f;

        /// <summary>같은 효과음이 한 프레임에 여러 번 겹치면 소리가 찢어진다. 이 간격 안에는 한 번만 낸다.</summary>
        private const float SfxRepeatGuardSeconds = 0.04f;

        public static AudioManager? Instance { get; private set; }

        private AudioCatalog? _catalog;
        private AudioSource? _bgmSource;
        private AudioSource? _sfxSource;

        private string? _currentBgmKey;
        private bool _unlocked;

        private readonly System.Collections.Generic.HashSet<string> _missingLogged = new();
        private readonly System.Collections.Generic.Dictionary<string, float> _lastPlayed = new();

        public float BgmVolume { get; private set; } = DefaultVolume;
        public float SfxVolume { get; private set; } = DefaultVolume;
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

            BgmVolume = PlayerPrefs.GetFloat(BgmVolumeKey, DefaultVolume);
            SfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, DefaultVolume);
            BgmMuted = PlayerPrefs.GetInt(BgmMutedKey, 0) == 1;
            SfxMuted = PlayerPrefs.GetInt(SfxMutedKey, 0) == 1;

            _catalog = Resources.Load<AudioCatalog>(AudioCatalog.ResourceName);

            _bgmSource = gameObject.AddComponent<AudioSource>();
            _bgmSource.loop = true;
            _bgmSource.playOnAwake = false;

            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;

            ApplyVolumes();
        }

        // ────────────────────────────── 재생

        /// <summary>
        /// 배경음을 바꾼다. 같은 곡이면 <b>다시 틀지 않는다</b> — 화면을 오갈 때마다 처음부터 시작하면
        /// 음악이 끊긴 것처럼 들린다.
        /// </summary>
        public void PlayBgm(string key)
        {
            if (_currentBgmKey == key && _bgmSource != null && _bgmSource.isPlaying) return;

            _currentBgmKey = key;
            StartBgm(key);
        }

        public void StopBgm()
        {
            _currentBgmKey = null;
            if (_bgmSource != null) _bgmSource.Stop();
        }

        /// <summary>효과음 한 발. 없는 이름이면 아무 일도 하지 않는다.</summary>
        public void PlaySfx(string key)
        {
            if (_sfxSource == null || SfxMuted || SfxVolume <= 0f) return;

            if (_lastPlayed.TryGetValue(key, out float last) && Time.unscaledTime - last < SfxRepeatGuardSeconds) return;
            _lastPlayed[key] = Time.unscaledTime;

            var clip = Resolve(key);
            if (clip == null) return;

            _sfxSource.PlayOneShot(clip, SfxVolume);
        }

        /// <summary>
        /// 첫 입력이 들어왔다.
        /// </summary>
        /// <remarks>
        /// 재생을 미루지 않으므로 여기서 할 일은 <b>끊긴 배경음을 다시 잇는 것</b>뿐이다 —
        /// 컨텍스트가 잠긴 동안 시작된 재생이 브라우저에 따라 죽어 있을 수 있다.
        /// 이미 소리가 나고 있으면 아무 것도 하지 않는다(다시 틀면 곡이 처음으로 튄다).
        /// </remarks>
        public void UnlockAudio()
        {
            if (_unlocked) return;
            _unlocked = true;

            if (_currentBgmKey != null && (_bgmSource == null || !_bgmSource.isPlaying))
                StartBgm(_currentBgmKey);
        }

        private void StartBgm(string key)
        {
            var clip = Resolve(key);
            if (clip == null || _bgmSource == null) return;

            _bgmSource.clip = clip;
            _bgmSource.volume = BgmMuted ? 0f : BgmVolume;
            _bgmSource.Play();
        }

        private AudioClip? Resolve(string key)
        {
            var clip = _catalog != null ? _catalog.Find(key) : null;
            if (clip == null && _missingLogged.Add(key))
                Debug.LogWarning($"[AudioManager] 소리를 찾지 못했다 — '{key}'. 표를 다시 구웠는지 확인할 것(DomoNinja/오디오 카탈로그 생성).");
            return clip;
        }

        private void ApplyVolumes()
        {
            if (_bgmSource != null) _bgmSource.volume = BgmMuted ? 0f : BgmVolume;
        }

        public void SetBgmVolume(float value)
        {
            BgmVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(BgmVolumeKey, BgmVolume);
            ApplyVolumes();
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
            ApplyVolumes();
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
