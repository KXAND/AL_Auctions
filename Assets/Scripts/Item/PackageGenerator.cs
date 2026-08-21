using System;
using System.Collections.Generic;
using System.Linq;

namespace AuctionGame
{
    internal sealed class PackageGenerator
    {
        private readonly ItemCatalog _itemCatalog;
        private readonly Random _random;

        public PackageGenerator(ItemCatalog itemCatalog, Random random)
        {
            _itemCatalog = itemCatalog ?? throw new ArgumentNullException(nameof(itemCatalog));
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public GeneratedPackage Generate()
        {
            IReadOnlyList<ItemData> itemDataList = _itemCatalog.GetAllItems();
            for (int attempt = 0; attempt < GlobalSettings.PackageGenerationAttemptLimit; attempt++)
            {
                Dictionary<int, ItemData> selected = SelectCandidate(itemDataList);
                if (SatisfiesConstraints(selected))
                {
                    return Build(selected);
                }
            }
            throw new InvalidOperationException("达到包裹生成尝试上限，仍未生成满足约束的包裹。");
        }

        private bool SatisfiesConstraints(Dictionary<int, ItemData> items)
        {
            int totalValue = items.Values.Sum(item => item.BaseValue);
            if (totalValue < GlobalSettings.PackageTotalValueMinimum ||
                totalValue > GlobalSettings.PackageTotalValueMaximum)
            {
                return false;
            }

            int ssrValue = items.Values
                .Where(item => item.Rarity == ItemRarity.SSR)
                .Sum(item => item.BaseValue);
            return ssrValue >= GlobalSettings.PackageSsrValueMinimum &&
                   ssrValue <= GlobalSettings.PackageSsrValueMaximum;
        }

        private Dictionary<int, ItemData> SelectCandidate(IReadOnlyList<ItemData> itemDataList)
        {
            Dictionary<int, ItemData> selected = new Dictionary<int, ItemData>();
            for (int index = 0; index < GlobalSettings.PackageItemCount; index++)
            {
                ItemData[] feasible = itemDataList.Where(itemData =>
                {
                    selected[index] = itemData;
                    return CanComplete(index + 1, selected, itemDataList);
                }).ToArray();
                IReadOnlyList<ItemData> pool = feasible.Length == 0 ? itemDataList : feasible;
                selected[index] = pool[_random.Next(pool.Count)];
            }
            return selected;
        }

        private bool CanComplete(
            int index,
            Dictionary<int, ItemData> selected,
            IReadOnlyList<ItemData> itemDataList)
        {
            if (index == GlobalSettings.PackageItemCount)
            {
                return SatisfiesConstraints(selected);
            }
            foreach (ItemData itemData in itemDataList)
            {
                bool hadValue = selected.TryGetValue(index, out ItemData previous);
                selected[index] = itemData;
                bool result = CanComplete(index + 1, selected, itemDataList);
                if (hadValue)
                {
                    selected[index] = previous;
                }
                else
                {
                    selected.Remove(index);
                }
                if (result)
                {
                    return true;
                }
            }
            return false;
        }

        private GeneratedPackage Build(Dictionary<int, ItemData> items)
        {
            if (items.Values.Any(item => item.Size.x > GlobalSettings.GridWidth))
            {
                throw new InvalidOperationException("藏品宽度超过固定网格宽度。");
            }

            int[] heights = new int[GlobalSettings.GridWidth];
            List<PackageLayoutItem> layout = new List<PackageLayoutItem>();
            foreach (KeyValuePair<int, ItemData> pair in items.OrderByDescending(pair => pair.Value.Size.x).ThenBy(_ => _random.Next(int.MaxValue)))
            {
                List<int> candidates = new List<int>();
                for (int x = 0; x <= GlobalSettings.GridWidth - pair.Value.Size.x; x++)
                {
                    if (Enumerable.Range(x, pair.Value.Size.x).Select(column => heights[column]).Distinct().Count() == 1)
                    {
                        candidates.Add(x);
                    }
                }
                if (candidates.Count == 0)
                {
                    throw new InvalidOperationException("当前包裹布局无解。");
                }

                int column = candidates[_random.Next(candidates.Count)];
                int y = heights[column];
                layout.Add(new PackageLayoutItem(pair.Key, column, y, pair.Value.Size.x, pair.Value.Size.y));
                for (int x = column; x < column + pair.Value.Size.x; x++)
                    heights[x] = y + pair.Value.Size.y;
            }
            return new GeneratedPackage(items, layout);
        }

        internal sealed class GeneratedPackage
        {
            private readonly Dictionary<int, ItemData> _items;
            private readonly Dictionary<int, PackageLayoutItem> _layout;

            public GeneratedPackage(Dictionary<int, ItemData> items, IReadOnlyList<PackageLayoutItem> layout)
            {
                _items = items;
                _layout = layout.ToDictionary(item => item.InstanceId);
                TotalValue = items.Values.Sum(item => item.BaseValue);
                Height = layout.Count == 0 ? 0 : layout.Max(item => item.Y + item.Height);
            }

            public IReadOnlyDictionary<int, ItemData> Items => _items;
            public int TotalValue { get; }
            public int Height { get; }
            public PackageLayoutItem PlacementOf(int instanceId) => _layout[instanceId];
        }

        internal sealed class PackageLayoutItem
        {
            public PackageLayoutItem(int instanceId, int x, int y, int width, int height)
            {
                InstanceId = instanceId;
                X = x;
                Y = y;
                Width = width;
                Height = height;
            }

            public int InstanceId { get; }
            public int X { get; }
            public int Y { get; }
            public int Width { get; }
            public int Height { get; }
        }
    }
}
