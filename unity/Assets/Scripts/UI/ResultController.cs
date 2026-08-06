using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UImage = UnityEngine.UI.Image;

namespace DomoNinja.Unity
{
    /// <summary>결과 팝업. <see cref="RunManager.EndRun"/> 이 남긴 스냅샷을 읽어 보여준다.</summary>
    public sealed class ResultController : MonoBehaviour
    {
        private TMP_Text _titleLabel;
        private TMP_Text _statsLabel;
        private TMP_Text _rewardLabel;
        private Button _confirmButton;

        private void Awake()
        {
            var board = transform.Find("Board");
            _titleLabel = board.Find("TitleLabel").GetComponent<TMP_Text>();
            _statsLabel = board.Find("StatsLabel").GetComponent<TMP_Text>();
            _rewardLabel = board.Find("RewardLabel").GetComponent<TMP_Text>();
            _confirmButton = UITheme.EnsureButton(board.Find("ConfirmButton").gameObject);

            _confirmButton.onClick.AddListener(OnConfirm);
        }

        private void OnEnable()
        {
            var mgr = RunManager.Instance;
            if (mgr == null) return;

            _titleLabel.text = mgr.LastRunCleared ? "클리어!" : "실패";
            _statsLabel.text = $"도달 라운드 {mgr.LastRunRoundsWon}";
            _rewardLabel.text = $"+{mgr.LastRunCurrencyEarned}";
        }

        private void OnConfirm()
        {
            UIScreenManager.HidePopup("Result");
            UIScreenManager.ShowScreen("StageSelect");
        }    }
}
