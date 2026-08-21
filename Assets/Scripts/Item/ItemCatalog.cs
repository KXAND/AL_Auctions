using System;
using System.Collections.Generic;
using UnityEngine;

namespace AuctionGame
{
    [CreateAssetMenu(fileName = "ItemCatalog", menuName = "Game/Item Catalog")]
    public sealed class ItemCatalog : ScriptableObject
    {
        [SerializeField] private ItemData[] items;

        public IReadOnlyList<ItemData> GetAllItems()
        {
            return items ?? Array.Empty<ItemData>();
        }

        public ItemData FindById(string itemId)
        {
            if (items == null)
            {
                return null;
            }

            return Array.Find(items, item => item != null && item.ItemId == itemId);
        }

    }
}

