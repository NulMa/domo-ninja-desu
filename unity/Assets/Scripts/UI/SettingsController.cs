using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UImage = UnityEngine.UI.Image;

namespace DomoNinja.Unity
{
    /// <summary>설정 팝업. BGM/SFX 볼륨 슬라이더 + 음소거를 <see cref="AudioManager"/>에 그대로 반영한다.</summary>
    public sealed class SettingsController : MonoBehaviour
    {
        private static readonly Color MutedColor = new Color(0.85f, 0.35f, 0.25f);
        private static readonly Color UnmutedColor = new Color(0.22f, 0.23f, 0.25f);

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
            _bgmMuteButton = EnsureButton(bgmRow.Find("MuteButton").gameObject);
            _bgmMuteLabel = _bgmMuteButton.transform.Find("Label").GetComponent<TMP_Text>();

            _sfxSlider = sfxRow.Find("SliderArea/SfxSlider").GetComponent<Slider>();
            _sfxValueLabel = sfxRow.Find("ValueLabel").GetComponent<TMP_Text>();
            _sfxMuteButton = EnsureButton(sfxRow.Find("MuteButton").gameObject);
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

        private static void RefreshMuteButton(Button button, TMP_Text label, bool muted)
        {
            label.text = muted ? "음소거 해제" : "음소거";
            var img = button.GetComponent<UImage>();
            if (img != null) img.color = muted ? MutedColor : UnmutedColor;
        }

        private static Button EnsureButton(GameObject go)
        {
            var btn = go.GetComponent<Button>();
            if (btn == null) btn = go.AddComponent<Button>();
            var img = go.GetComponent<UImage>();
            if (img != null) btn.targetGraphic = img;
            return btn;
        }
    }
}
