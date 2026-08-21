using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AuctionGame
{
    public sealed class CatalogPanel : MonoBehaviour
    {
        [SerializeField] private ItemCatalog itemCatalog;
        [SerializeField] private RectTransform listContent;
        [SerializeField] private GameObject itemPrefab;
        [SerializeField] private Button[] rarityButtons;
        [SerializeField] private Button[] sizeButtons;
        [SerializeField] private ItemRarity[] rarityValues;
        [SerializeField] private Vector2Int[] sizeValues;
        [SerializeField] private Color selectedColor = new Color(0.68f, 0.85f, 1f, 1f);

        private readonly List<bool> _raritySelected = new List<bool>();
        private readonly List<bool> _sizeSelected = new List<bool>();
        private readonly List<Color> _rarityOriginalColors = new List<Color>();
        private readonly List<Color> _sizeOriginalColors = new List<Color>();
        private readonly List<GameObject> _listItems = new List<GameObject>();

        private void Awake()
        {
            for (int index = 0; index < rarityButtons.Length; index++)
            {
                Image image = rarityButtons[index].GetComponent<Image>();
                _rarityOriginalColors.Add(image != null ? image.color : Color.white);
                int captured = index;
                rarityButtons[index].onClick.AddListener(() => ToggleRarity(captured));
            }
            for (int index = 0; index < sizeButtons.Length; index++)
            {
                Image image = sizeButtons[index].GetComponent<Image>();
                _sizeOriginalColors.Add(image != null ? image.color : Color.white);
                int captured = index;
                sizeButtons[index].onClick.AddListener(() => ToggleSize(captured));
            }
        }

        private void OnEnable()
        {
            OnOpen();
        }

        public void OnOpen()
        {
            _raritySelected.Clear();
            _sizeSelected.Clear();
            for (int index = 0; index < rarityButtons.Length; index++)
            { _raritySelected.Add(false); }
            for (int index = 0; index < sizeButtons.Length; index++)
            { _sizeSelected.Add(false); }
            ApplyButtonVisuals();
            RefreshList();
        }

        public void ToggleRarity(int index)
        {
            _raritySelected[index] = !_raritySelected[index];
            ApplyButtonVisuals();
            RefreshList();
        }

        public void ToggleSize(int index)
        {
            _sizeSelected[index] = !_sizeSelected[index];
            ApplyButtonVisuals();
            RefreshList();
        }

        private void ApplyButtonVisuals()
        {
            for (int index = 0; index < rarityButtons.Length; index++)
            { SetButtonColor(rarityButtons[index], _rarityOriginalColors[index], _raritySelected[index]); }
            for (int index = 0; index < sizeButtons.Length; index++)
            { SetButtonColor(sizeButtons[index], _sizeOriginalColors[index], _sizeSelected[index]); }
        }

        private void SetButtonColor(Button button, Color original, bool selected)
        {
            Image image = button == null ? null : button.GetComponent<Image>();
            if (image != null)
            {
                image.color = selected ? selectedColor : original;
            }
        }

        private void RefreshList()
        {
            foreach (GameObject item in _listItems)
            {
                if (item != null)
                {
                    Destroy(item.gameObject);
                }
            }
            _listItems.Clear();

            if (itemCatalog == null || listContent == null || itemPrefab == null)
            {
                return;
            }

            List<ItemRarity> rarities = new List<ItemRarity>();
            for (int index = 0; index < rarityValues.Length; index++)
            {
                if (index < _raritySelected.Count && _raritySelected[index])
                {
                    rarities.Add(rarityValues[index]);
                }
            }
            List<Vector2Int> sizes = new List<Vector2Int>();
            for (int index = 0; index < sizeValues.Length; index++)
            {
                if (index < _sizeSelected.Count && _sizeSelected[index])
                {
                    sizes.Add(sizeValues[index]);
                }
            }

            IEnumerable<ItemData> filtered = itemCatalog.GetAllItems().Where(item => item != null);
            if (rarities.Count > 0)
            {
                filtered = filtered.Where(item => rarities.Contains(item.Rarity));
            }
            if (sizes.Count > 0)
            {
                filtered = filtered.Where(item => sizes.Contains(item.Size));
            }

            foreach (ItemData itemData in filtered)
            {
                GameObject item = Instantiate(itemPrefab, listContent);
                item.name = "CatalogItem_" + itemData.ItemId;

                if (item.transform.Find("Icon").TryGetComponent<Image>(out Image icon))
                {
                    icon.sprite = itemData.FullSprite;
                    icon.color = RarityColor(itemData.Rarity);
                }

                TMP_Text[] texts = item.GetComponentsInChildren<TMP_Text>(true);
                if (texts.Length >= 2)
                {
                    texts[0].SetText($"{itemData.DisplayName}  {itemData.Size.x}×{itemData.Size.y}");
                    texts[1].SetText(itemData.BaseValue.ToString());
                }

                _listItems.Add(item);
            }
        }

        private static Color RarityColor(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.SSR:
                    return GlobalSettings.BoardRarityColor.SSR;
                case ItemRarity.SR:
                    return GlobalSettings.BoardRarityColor.SR;
                case ItemRarity.R:
                    return GlobalSettings.BoardRarityColor.R;
                default:
                    return GlobalSettings.BoardRarityColor.N;
            }
        }
    }
}

