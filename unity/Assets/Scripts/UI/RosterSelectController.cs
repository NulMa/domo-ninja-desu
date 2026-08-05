using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UImage = UnityEngine.UI.Image;
using DomoNinja.Unity.View;

namespace DomoNinja.Unity
{
    /// <summary>용병 선택 화면. 최대 3명 토글 선택 → 트레이/정보패널 갱신 → 전투 진입.</summary>
    public sealed class RosterSelectController : MonoBehaviour
    {
        private const int DeployCount = 3;
        private const string PortraitPrefix = "Portrait_";

        private static readonly Color SelectedColor = new Color(0.85f, 0.55f, 0.20f);
        private static readonly Color UnselectedColor = new Color(0.18f, 0.19f, 0.21f);
        private static readonly Color TrayFilledColor = new Color(0.25f, 0.48f, 0.78f);
        private static readonly Color TrayEmptyColor = new Color(0.18f, 0.19f, 0.21f);

        private Transform _portraitGrid;
        private Transform _confirmedTray;
        private Button _enterBattleButton;
        private TMP_Text _infoLabel;
        private SpriteCatalog _catalog;

        private readonly List<string> _selected = new List<string>();

        private void Awake()
        {
            var board = transform.Find("Board");
            _portraitGrid = board.Find("PortraitGrid");
            _confirmedTray = board.Find("ConfirmedTray");
            _enterBattleButton = EnsureButton(board.Find("EnterBattleButton").gameObject);
            _infoLabel = board.Find("InfoPanel/Label").GetComponent<TMP_Text>();

            _catalog = Resources.Load<SpriteCatalog>(SpriteCatalog.ResourceName);

            foreach (Transform cell in _portraitGrid)
            {
                if (!cell.name.StartsWith(PortraitPrefix)) continue;
                string characterId = cell.name.Substring(PortraitPrefix.Length);

                var btn = EnsureButton(cell.gameObject);
                btn.onClick.AddListener(() => OnPortraitClicked(characterId));
            }

            _enterBattleButton.onClick.AddListener(OnEnterBattle);

            RefreshUI();
        }

        private void OnPortraitClicked(string characterId)
        {
            if (_selected.Contains(characterId))
                _selected.Remove(characterId);
            else if (_selected.Count < DeployCount)
                _selected.Add(characterId);

            RefreshUI();
        }

        private void RefreshUI()
        {
            foreach (Transform cell in _portraitGrid)
            {
                if (!cell.name.StartsWith(PortraitPrefix)) continue;
                string characterId = cell.name.Substring(PortraitPrefix.Length);
                var img = cell.GetComponent<UImage>();
                if (img != null) img.color = _selected.Contains(characterId) ? SelectedColor : UnselectedColor;
            }

            for (int i = 0; i < _confirmedTray.childCount; i++)
                RefreshTraySlot(_confirmedTray.GetChild(i), i < _selected.Count ? _selected[i] : null);

            RefreshInfoLabel();

            _enterBattleButton.interactable = _selected.Count == DeployCount;
        }

        private void RefreshTraySlot(Transform slot, string characterId)
        {
            var slotImg = slot.GetComponent<UImage>();
            var face = slot.Find("Face");

            if (characterId == null)
            {
                if (slotImg != null) slotImg.color = TrayEmptyColor;
                if (face != null) face.gameObject.SetActive(false);
                return;
            }

            if (slotImg != null) slotImg.color = TrayFilledColor;

            if (face == null)
            {
                var faceGo = new GameObject("Face", typeof(RectTransform));
                faceGo.transform.SetParent(slot, false);
                var rt = faceGo.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(4, 4);
                rt.offsetMax = new Vector2(-4, -4);
                faceGo.AddComponent<UImage>().preserveAspect = true;
                face = faceGo.transform;
            }

            face.gameObject.SetActive(true);
            face.GetComponent<UImage>().sprite = FindSprite(characterId);
        }

        private void RefreshInfoLabel()
        {
            if (_selected.Count == 0)
            {
                _infoLabel.text = "용병을 선택하면 정보가 표시됩니다";
                return;
            }

            var mgr = RunManager.Instance;
            var def = mgr != null && mgr.Data != null ? mgr.Data.FindCharacter(_selected[_selected.Count - 1]) : null;
            _infoLabel.text = def != null
                ? $"{def.Name}\n공격력 {def.Attack}   체력 {def.Hp}   공격간격 {def.AttackInterval}"
                : "정보를 찾지 못했다";
        }

        private Sprite FindSprite(string characterId)
        {
            var mgr = RunManager.Instance;
            if (mgr == null || mgr.Data == null || _catalog == null) return null;
            var def = mgr.Data.FindCharacter(characterId);
            return def != null ? _catalog.Find(def.Sprite) : null;
        }

        private void OnEnterBattle()
        {
            var mgr = RunManager.Instance;
            if (mgr == null || _selected.Count != DeployCount) return;

            mgr.StartNewRun(mgr.SelectedStageId, _selected);
            UIScreenManager.ShowScreen("GamePlay");
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
