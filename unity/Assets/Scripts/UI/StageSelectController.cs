using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UImage = UnityEngine.UI.Image;

namespace DomoNinja.Unity
{
    /// <summary>스테이지 선택 화면. 지금은 메타 재화 표시만 관리한다.</summary>
    public sealed class StageSelectController : MonoBehaviour
    {
        private const string MetaCurrencyIconKey = "Meta/M-GOLD_재화";

        private TMP_Text _currencyLabel;

        private void Awake()
        {
            var display = transform.Find("Board/CurrencyDisplay");
            display.Find("Icon").GetComponent<UImage>().sprite = UITheme.Find(MetaCurrencyIconKey);
            _currencyLabel = display.Find("Label").GetComponent<TMP_Text>();
        }

        private void OnEnable() => Refresh();

        private void Refresh()
        {
            var mgr = RunManager.Instance;
            _currencyLabel.text = mgr != null && mgr.Meta != null ? mgr.Meta.Currency.ToString() : "-";
        }
    }
}
