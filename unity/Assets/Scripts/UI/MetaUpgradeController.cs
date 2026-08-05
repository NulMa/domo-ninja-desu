using System.Collections.Generic;
using DomoNinja.Core.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UImage = UnityEngine.UI.Image;

namespace DomoNinja.Unity
{
    /// <summary>메타 강화 팝업. 항목별 레벨·비용을 보여주고 <see cref="MetaProgress.TryUpgrade"/> 를 호출한다.</summary>
    /// <remarks>
    /// `data/meta.json` `persistence._resetNote` — 초기화 버튼은 <b>심사용 데모 장치</b>다.
    /// 심사자가 "강화 없는 상태"와 "강화된 상태"를 둘 다 볼 수 있어야 하므로,
    /// 초기화는 레벨만 0으로 내리는 게 아니라 <b>쓴 재화를 전액 환급</b>한다 — 그래야 반복 시연이 된다.
    /// </remarks>
    public sealed class MetaUpgradeController : MonoBehaviour
    {
        private static readonly string[] RowIds = { "M-ATK", "M-HP", "M-HASTE", "M-MOVE", "M-GOLD" };

        private TMP_Text _currencyLabel;
        private Button _resetButton;

        private readonly Dictionary<string, TMP_Text> _levelLabels = new Dictionary<string, TMP_Text>();
        private readonly Dictionary<string, Button> _upgradeButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, TMP_Text> _upgradeButtonLabels = new Dictionary<string, TMP_Text>();

        private void Awake()
        {
            var board = transform.Find("Board");
            _currencyLabel = board.Find("CurrencyValueLabel").GetComponent<TMP_Text>();

            var closeButton = board.Find("CloseButton").gameObject;
            var close = EnsureButton(closeButton);
            close.onClick.AddListener(() => UIScreenManager.HidePopup("MetaUpgrade"));

            _resetButton = EnsureButton(board.Find("ResetButton").gameObject);
            _resetButton.onClick.AddListener(OnReset);

            foreach (string id in RowIds)
            {
                var row = board.Find("Row_" + id);
                var nameLabel = row.Find("NameLabel").GetComponent<TMP_Text>();
                var def = FindDef(id);
                if (def != null) nameLabel.text = def.Name;

                _levelLabels[id] = row.Find("LevelLabel").GetComponent<TMP_Text>();

                var upgradeButton = EnsureButton(row.Find("UpgradeButton").gameObject);
                _upgradeButtons[id] = upgradeButton;
                _upgradeButtonLabels[id] = row.Find("UpgradeButton/Label").GetComponent<TMP_Text>();

                string rowId = id;
                upgradeButton.onClick.AddListener(() => OnUpgradeClicked(rowId));
            }
        }

        private void OnEnable() => RefreshUI();

        private void RefreshUI()
        {
            var mgr = RunManager.Instance;
            if (mgr == null || mgr.Meta == null || mgr.Data == null) return;

            _currencyLabel.text = mgr.Meta.Currency.ToString();

            foreach (string id in RowIds)
            {
                var def = FindDef(id);
                if (def == null) continue;

                int level = mgr.Meta.LevelOf(id);
                _levelLabels[id].text = $"Lv.{level}/{def.MaxLevel}";

                int? cost = mgr.Meta.NextCost(id);
                var button = _upgradeButtons[id];
                if (cost == null)
                {
                    _upgradeButtonLabels[id].text = "MAX";
                    button.interactable = false;
                }
                else
                {
                    _upgradeButtonLabels[id].text = cost.Value.ToString();
                    button.interactable = mgr.Meta.Currency >= cost.Value;
                }
            }
        }

        private void OnUpgradeClicked(string upgradeId)
        {
            RunManager.Instance?.Meta?.TryUpgrade(upgradeId);
            RefreshUI();
        }

        private void OnReset()
        {
            var mgr = RunManager.Instance;
            if (mgr == null || mgr.Meta == null || mgr.Data == null) return;

            int refund = 0;
            foreach (var def in mgr.Data.Meta.Upgrades)
            {
                int level = mgr.Meta.LevelOf(def.Id);
                for (int i = 0; i < level; i++) refund += def.Costs[i];
                mgr.Meta.SetLevel(def.Id, 0);
            }

            mgr.Meta.Currency += refund;
            RefreshUI();
        }

        private static MetaUpgradeDef FindDef(string id)
        {
            var mgr = RunManager.Instance;
            if (mgr?.Data?.Meta == null) return null;

            foreach (var def in mgr.Data.Meta.Upgrades)
                if (def.Id == id) return def;

            return null;
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
