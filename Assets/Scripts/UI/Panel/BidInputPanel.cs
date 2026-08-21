using System;
using TMPro;
using UnityEngine;

namespace AuctionGame
{
    public sealed class BidInputPanel : MonoBehaviour
    {
        [SerializeField] private TMP_InputField _input;
        [SerializeField] private TMP_Text _status;
        [SerializeField] private TMP_Text multiplierLabel;

        private string _inputText = string.Empty;
        private int _ownAssets;
        private decimal _multiplier;
        private int _lastFilledRound;
        private bool _canEdit;
        private int _bidValue;

        private int BidValue
        {
            get => _bidValue;
            set => _bidValue = Math.Clamp(value, 0, _ownAssets);
        }

        private void Awake()
        {
            _input.onValueChanged.AddListener(OnInputEdited);
        }

        private void OnInputEdited(string text)
        {
            int parsed = int.TryParse(text, out int value) ? value : 0;
            BidValue = parsed;
            _inputText = BidValue.ToString();
            _input.SetTextWithoutNotify(_inputText);
        }

        public void SetInputState(int ownAssets, int ownLastBid, int round, bool canRequest)
        {
            _ownAssets = ownAssets;
            _multiplier = round >= 1 && round <= GlobalSettings.WinningMultipliers.Count
                ? GlobalSettings.WinningMultipliers[round - 1]
                : GlobalSettings.FinalWinningMultiplier;

            if (multiplierLabel != null)
            {
                Utils.SetText(multiplierLabel, $"×{_multiplier:0.#}");
            }

            if (round != _lastFilledRound)
            {
                _lastFilledRound = round;
                _canEdit = canRequest;
                _inputText = ownLastBid.ToString();
                int parsed = int.TryParse(_inputText, out int value) ? value : 0;
                BidValue = parsed;
                if (BidValue != parsed)
                {
                    _inputText = BidValue.ToString();
                }
                _input.SetTextWithoutNotify(_inputText);
                Utils.SetText(_status, string.Empty);
            }
            else
            {
                _canEdit = canRequest;
            }

            _input.interactable = _canEdit;
        }

        public void SetRequestState(PendingRequestState state, string reason)
        {
            if (state == PendingRequestState.Rejected)
            {
                _canEdit = true;
                Utils.SetText(_status, reason);
            }
            else if (state == PendingRequestState.Accepted)
            {
                _canEdit = false;
                Utils.SetText(_status, "已提交");
            }

            _input.interactable = _canEdit;
        }

        public void Submit()
        {
            if (!_canEdit)
            {
                return;
            }

            HumanController controller = GameManager.Instance.CurrentHumanController;
            if (controller != null)
            {
                controller.RequestBid(BidValue);
            }
            _canEdit = false;
            Utils.SetText(_status, "已提交");
            _input.interactable = _canEdit;
        }

        public void AppendInput(string value)
        {
            if (!_canEdit)
            {
                return;
            }

            _inputText += value;
            int parsed = int.TryParse(_inputText, out int parsedValue) ? parsedValue : 0;

            BidValue = parsed;
            _inputText = BidValue.ToString();
            _input.SetTextWithoutNotify(_inputText);
        }

        public void ClearInput()
        {
            if (!_canEdit)
            {
                return;
            }

            _inputText = "0";
            BidValue = 0;
            _input.SetTextWithoutNotify(_inputText);
        }

        public void BackspaceInput()
        {
            if (!_canEdit)
            {
                return;
            }

            _inputText = _inputText.Length <= 1
                ? string.Empty
                : _inputText.Substring(0, _inputText.Length - 1);
            int parsed = int.TryParse(_inputText, out int parsedValue) ? parsedValue : 0;

            BidValue = parsed;
            _inputText = BidValue.ToString();
            _input.SetTextWithoutNotify(_inputText);
        }

        public void MultiplyInput()
        {
            if (!_canEdit)
            {
                return;
            }

            if (_multiplier <= 1m)
            {
                return;
            }

            BidValue = (int)(BidValue * _multiplier);
            _inputText = BidValue.ToString();
            _input.SetTextWithoutNotify(_inputText);
        }
    }
}




