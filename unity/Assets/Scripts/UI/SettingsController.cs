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

            NormalizeSlider(_bgmSlider);
            NormalizeSlider(_sfxSlider);

            BuildCollectionNotice();
        }

        private const string NoticeName = "CollectionNotice";

        /// <summary>
        /// 무엇을 기록하는지 <b>한 줄로 밝힌다</b> (`25` §5 개인정보).
        /// </summary>
        /// <remarks>
        /// <para>
        /// ★ <b>실제로 보내고 있을 때만 띄운다.</b> 엔드포인트가 비어 있으면 아무것도 안 나가는데
        /// 그때도 "기록합니다"가 떠 있으면 <b>화면이 거짓말을 한다.</b> 고지는 상태를 따라가야지
        /// 계획을 따라가면 안 된다.
        /// </para>
        /// <para>
        /// ★ <b>씬이 아니라 코드에서 만든다.</b> 씬 파일은 손으로 병합이 안 되고 2인이 같이
        /// 만진다(`19` §5.1) — 슬라이더를 코드에서 고친 것과 같은 이유다.
        /// </para>
        /// <para>
        /// <c>Panel</c> 안이 아니라 <b>팝업 루트 바닥</b>에 붙인다. Panel 은 760×320 고정이고
        /// 이미 네 줄이 차 있어서, 거기 한 줄을 더 넣으면 닫기 버튼과 겹칠 자리를 눈으로 맞춰야 한다.
        /// </para>
        /// </remarks>
        private void BuildCollectionNotice()
        {
            if (transform.Find(NoticeName) != null) return;

            var config = Resources.Load<TelemetryConfig>(TelemetryConfig.ResourceName);
            if (config == null || !config.HasEndpoint) return;

            var go = new GameObject(NoticeName, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            go.transform.SetAsLastSibling();

            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 28f);
            rt.sizeDelta = new Vector2(980f, 48f);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.text =
                "이 게임은 밸런스 확인을 위해 런 결과(고른 캐릭터·스킬·클리어 여부)만 익명으로 기록합니다.\n" +
                "개인정보는 수집하지 않습니다.";
            label.fontSize = 18f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(1f, 1f, 1f, 0.6f);
            label.raycastTarget = false;

            // 한글이 나오려면 이미 쓰이고 있는 폰트를 그대로 물려받아야 한다.
            // TMP 기본 폰트는 라틴 문자만 들고 있어서 새로 만든 라벨은 네모로 나온다.
            if (_bgmValueLabel != null)
            {
                label.font = _bgmValueLabel.font;
                label.fontSharedMaterial = _bgmValueLabel.fontSharedMaterial;
            }
        }

        /// <summary>막대 높이. 배경·채움이 <b>같은 높이</b>여야 한 줄로 읽힌다.</summary>
        private const float BarHeight = 24f;

        /// <summary>손잡이 높이. 막대보다 커야 잡을 곳으로 보인다.</summary>
        private const float HandleHeight = 44f;

        /// <summary>
        /// 슬라이더 세 조각의 <b>비율을 원본 그림에 맞춘다.</b>
        /// </summary>
        /// <remarks>
        /// <para>
        /// ★ 손잡이가 <b>세로로만 늘어나 뭉개져 있었다.</b> 씬에 앵커가
        /// <c>(1,0)~(1,1)</c> 로 잡혀 슬라이더 높이만큼 늘어나고, 거기에 <c>sizeDelta.y=40</c> 이
        /// 더해져 <b>13×11 그림이 24×84 로</b> 그려졌다 — 세로만 6.5배다.
        /// 채움도 배경(24)의 두 배인 48 로 나와 막대가 두 겹으로 보였다.
        /// </para>
        /// <para>
        /// ★ <b>씬이 아니라 코드에서 고친다.</b> 씬 파일은 손으로 병합이 안 되고 2인이 같이
        /// 만진다(`19` §5.1) — 지금 팀원이 같은 씬을 건드리고 있을 수 있다.
        /// 여기서 맞추면 나중에 슬라이더가 늘어도 같은 규칙을 탄다.
        /// </para>
        /// <para>
        /// 손잡이 크기는 <b>원본 비율에서 계산</b>한다. 숫자를 박아두면 그림을 바꿀 때
        /// 다시 어긋나고, 어긋난 걸 눈으로 찾기 어렵다.
        /// </para>
        /// </remarks>
        private static void NormalizeSlider(Slider slider)
        {
            if (slider == null) return;

            var bg = slider.transform.Find("Background") as RectTransform;
            if (bg != null) SetHeightCentered(bg, BarHeight);

            // 채움은 `Fill Area` 가 높이를 정하고, 채움 자체는 그 안에서 늘어난다.
            // 채움의 sizeDelta.y 가 남아 있으면 그만큼 <b>더</b> 커진다 — 0 으로 눌러 붙인다.
            var fill = slider.fillRect;
            if (fill != null)
            {
                if (fill.parent is RectTransform fillArea) SetHeightCentered(fillArea, BarHeight);
                fill.sizeDelta = new Vector2(fill.sizeDelta.x, 0f);
            }

            NormalizeHandle(slider.handleRect);
        }

        /// <summary>
        /// 손잡이 그림을 <b>자식으로 옮겨</b> 비율을 지킨다.
        /// </summary>
        /// <remarks>
        /// ★ <c>Slider</c> 는 <c>UpdateVisuals</c> 에서 손잡이의 <b>세로 앵커를 0~1 로 되돌린다</b>
        /// (값이 바뀔 때마다). 그래서 앵커를 가운데로 모아봐야 다음 프레임에 다시 늘어난다 —
        /// 실제로 그렇게 고쳤다가 손잡이가 여전히 88px 로 나왔다.
        /// <para>
        /// 그래서 <b>손잡이는 늘어나게 두고</b>(잡는 판정 영역으로만 쓰고), 그림은 크기가 고정된
        /// 자식에 옮긴다. 엔진이 부모를 어떻게 늘리든 그림은 원본 비율을 지킨다.
        /// </para>
        /// </remarks>
        private static void NormalizeHandle(RectTransform handle)
        {
            if (handle == null) return;

            var handleImage = handle.GetComponent<UImage>();
            if (handleImage == null) return;

            var sprite = handleImage.sprite;
            float aspect = sprite != null && sprite.rect.height > 0
                ? sprite.rect.width / sprite.rect.height
                : 1f;

            var knobTr = handle.Find("Knob") as RectTransform;
            if (knobTr == null)
            {
                var go = new GameObject("Knob", typeof(RectTransform));
                go.transform.SetParent(handle, false);
                knobTr = (RectTransform)go.transform;
                go.AddComponent<UImage>();
            }

            knobTr.anchorMin = new Vector2(0.5f, 0.5f);
            knobTr.anchorMax = new Vector2(0.5f, 0.5f);
            knobTr.pivot = new Vector2(0.5f, 0.5f);
            knobTr.anchoredPosition = Vector2.zero;
            knobTr.sizeDelta = new Vector2(HandleHeight * aspect, HandleHeight);

            var knob = knobTr.GetComponent<UImage>();
            knob.sprite = sprite;
            knob.preserveAspect = true;
            knob.raycastTarget = false;

            // 부모는 잡는 영역으로만 남긴다 — 그림을 두 번 그리면 늘어난 쪽이 비쳐 보인다.
            handleImage.enabled = false;
        }

        /// <summary>세로 가운데 정렬로 높이만 고정한다. 가로는 부모를 따라 늘어난 채로 둔다.</summary>
        private static void SetHeightCentered(RectTransform rt, float height)
        {
            rt.anchorMin = new Vector2(rt.anchorMin.x, 0.5f);
            rt.anchorMax = new Vector2(rt.anchorMax.x, 0.5f);
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
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
