
using UnityEngine;

namespace AuctionGame
{
    [CreateAssetMenu(
        fileName = "NewItemData",
        menuName = "Game/Item Data")]
    public sealed class ItemData : ScriptableObject
    {
        [SerializeField] private string itemId;
        [SerializeField] private string displayName;
        [SerializeField] private Vector2Int size = Vector2Int.one;
        [SerializeField] private ItemRarity rarity;
        [SerializeField] private int baseValue;
        [SerializeField] private Sprite fullSprite;

        public string ItemId => itemId;
        public string DisplayName => displayName;
        public Vector2Int Size => size;
        public ItemRarity Rarity => rarity;
        public int BaseValue => baseValue;
        public Sprite FullSprite => fullSprite;
    }
}
