using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AuctionGame
{
    public sealed class ClueSelectionPanel : MonoBehaviour
    {
        [SerializeField] private Transform clueRoot;
        [SerializeField] private Button clueButtonTemplate;
        [SerializeField] private Button confirmButton;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private ClueCatalog clueCatalog;
        [SerializeField] private Color selectedColor = new Color(0.68f, 0.85f, 1f, 1f);

        private readonly List<Button> _choiceButtons = new List<Button>();
        private readonly List<int> _choiceIds = new List<int>();
        private readonly List<Color> _buttonOriginalColors = new List<Color>();
        private int _selectedClueId = -1;
        private string _choiceSignature;
        private bool _canEdit;

        public void SetChoices(IReadOnlyList<int> clueIds)
        {
            string signature = string.Join(",", clueIds);
            if (_choiceSignature == signature)
            {
                UpdateSelection();
                return;
            }

            _choiceSignature = signature;
            _selectedClueId = -1;
            _canEdit = true;
            foreach (Button button in _choiceButtons)
            {
                if (button != null)
                {
                    Destroy(button.gameObject);
                }
            }
            _choiceButtons.Clear();
            _choiceIds.Clear();
            _buttonOriginalColors.Clear();

            if (clueButtonTemplate == null || clueRoot == null)
            {
                return;
            }

            clueButtonTemplate.gameObject.SetActive(false);
            foreach (int clueId in clueIds)
            {
                Clue clue = clueCatalog == null ? null : clueCatalog.Find(clueId);
                if (clue == null)
                {
                    continue;
                }

                Button button = Instantiate(clueButtonTemplate, clueRoot);
                button.name = $"ClueChoice_{clueId}";
                button.gameObject.SetActive(true);

                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.text = clue.DisplayName;
                }

                Image image = button.GetComponent<Image>();
                _buttonOriginalColors.Add(image != null ? image.color : Color.white);

                int capturedId = clueId;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => SelectClue(capturedId));

                _choiceButtons.Add(button);
                _choiceIds.Add(clueId);
            }

            UpdateSelection();
            UpdateInteractable();
        }

        public void SetRequestState(PendingRequestState state, string reason)
        {
            if (state == PendingRequestState.Rejected)
            {
                _canEdit = true;
                Utils.SetText(statusText, reason);
            }
            else if (state == PendingRequestState.Accepted)
            {
                _canEdit = false;
                Utils.SetText(statusText, "已提交");
            }

            UpdateInteractable();
        }

        public void SelectClue(int clueId)
        {
            if (!_canEdit)
            {
                return;
            }

            _selectedClueId = clueId;
            UpdateSelection();
            UpdateInteractable();
        }

        public void SubmitSelected()
        {
            if (!_canEdit || _selectedClueId < 0)
            {
                return;
            }

            HumanController controller = GameManager.Instance.CurrentHumanController;
            if (controller != null)
            {
                controller.RequestClue(_selectedClueId);
            }
            _canEdit = false;
            Utils.SetText(statusText, "已提交");
            UpdateInteractable();
        }

        private void UpdateSelection()
        {
            for (int index = 0; index < _choiceButtons.Count; index++)
            {
                Image image = _choiceButtons[index].GetComponent<Image>();
                if (image != null)
                {
                    image.color = _selectedClueId == _choiceIds[index]
                    ? selectedColor
                    : _buttonOriginalColors[index];
                }
            }

            Utils.SetText(statusText, _selectedClueId < 0 ? "请选择事件" : "已选择事件");
        }

        private void UpdateInteractable()
        {
            foreach (Button button in _choiceButtons)
            {
                button.interactable = _canEdit;
            }

            if (confirmButton != null)
            {
                confirmButton.interactable = _canEdit && _selectedClueId >= 0;
            }
        }
    }
}

