using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UImage = UnityEngine.UI.Image;

namespace DomoNinja.Unity
{
    /// <summary>설정 팝업. BGM/SFX 볼륨 슬라이더 + 음소거를 <see cref="AudioManager"/>에 그대로 반영한다.</summary>
    public sealed class SettingsController : MonoBehaviour
    {
        // 스프라이트 위에 곱해지는 틴트다. 흰색이 원본 그대로다.
        private static readonly Color MutedColor = new Color(1.00f, 0.45f, 0.35f);
        private static readonly Color UnmutedColor = Color.white;

        private Slider _bgmSlider;
        private TMP_Text _bgmValueLabel;
        private Button _bgmMuteButton;
        private TMP_Text _bgmMuteLabel;

        private Slider _sfxSlider;
        private TMP_Text _sfxValueLabel;
        private Button _sfxMuteButton;
        private TMP_Text _sfxMuteLabel;

        private void Awake()
        {
            var panel = transform.Find("Panel");
            var bgmRow = panel.Find("BgmRow");
            var sfxRow = panel.Find("SfxRow");

            _bgmSlider = bgmRow.Find("SliderArea/BgmSlider").GetComponent<Slider>();
            _bgmValueLabel = bgmRow.Find("ValueLabel").GetComponent<TMP_Text>();
            _bgmMuteButton = UITheme.EnsureButton(bgmRow.Find("MuteButton").gameObject);
            _bgmMuteLabel = _bgmMuteButton.transform.Find("Label").GetComponent<TMP_Text>();

            _sfxSlider = sfxRow.Find("SliderArea/SfxSlider").GetComponent<Slider>();
            _sfxValueLabel = sfxRow.Find("ValueLabel").GetComponent<TMP_Text>();
            _sfxMuteButton = UITheme.EnsureButton(sfxRow.Find("MuteButton").gameObject);
            _sfxMuteLabel = _sfxMuteButton.transform.Find("Label").GetComponent<TMP_Text>();

            _bgmSlider.onValueChanged.AddListener(v => AudioManager.Instance?.SetBgmVolume(v));
            _sfxSlider.onValueChanged.AddListener(v => AudioManager.Instance?.SetSfxVolume(v));
            _bgmMuteButton.onClick.AddListener(() => AudioManager.Instance?.ToggleBgmMute());
            _sfxMuteButton.onClick.AddListener(() => AudioManager.Instance?.ToggleSfxMute());
        }

        private void OnEnable()
        {
            var mgr = AudioManager.Instance;
            if (mgr != null) mgr.Changed += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            var mgr = AudioManager.Instance;
            if (mgr != null) mgr.Changed -= Refresh;
        }

        private void Refresh()
        {
            var mgr = AudioManager.Instance;
            if (mgr == null) return;

            // 슬라이더 이벤트가 다시 SetBgmVolume 을 부르지 않도록 값만 바꾼다.
            _bgmSlider.SetValueWithoutNotify(mgr.BgmVolume);
            _sfxSlider.SetValueWithoutNotify(mgr.SfxVolume);

            _bgmValueLabel.text = $"{Mathf.RoundToInt(mgr.BgmVolume * 100)}%";
            _sfxValueLabel.text = $"{Mathf.RoundToInt(mgr.SfxVolume * 100)}%";

            RefreshMuteButton(_bgmMuteButton, _bgmMuteLabel, mgr.BgmMuted);
            RefreshMuteButton(_sfxMuteButton, _sfxMuteLabel, mgr.SfxMuted);
        }

        /// <summary>
        /// 음소거 상태를 <b>체크 표시</b>로 보여준다.
        /// </summary>
        /// <remarks>
        /// 전에는 버튼 글자("음소거" ↔ "음소거 해제")와 붉은 틴트뿐이었다.
        /// <b>글자를 읽어야만 지금 상태를 알 수 있고</b>, 게다가 그 글자는 <b>상태가 아니라 다음 동작</b>이라
        /// "음소거"가 켜졌다는 뜻인지 누르면 꺼진다는 뜻인지가 매번 헷갈린다.
        /// 팩의 <c>checked</c>/<c>unchecked</c> 를 붙여 <b>상태는 그림이, 동작은 글자가</b> 맡게 나눴다.
        /// </remarks>
        private static void RefreshMuteButton(Button button, TMP_Text label, bool muted)
        {
            label.text = muted ? "음소거 해제" : "음소거";

            var img = button.GetComponent<UImage>();
            if (img != null) img.color = muted ? MutedColor : UnmutedColor;

            var icon = button.transform.Find("CheckIcon");
            if (icon == null) return;

            var iconImage = icon.GetComponent<UImage>();
            if (iconImage == null) return;

            var sprite = UITheme.Find(muted ? "UI/Theme/checked" : "UI/Theme/unchecked");
            if (sprite != null) iconImage.sprite = sprite;
        }
    }
}
