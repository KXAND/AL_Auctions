using System;
using System.Collections.Generic;
using System.Linq;

namespace AuctionGame
{
    public enum CollectibleRarity { N, R, SR, SSR }

    public readonly struct IntRange
    {
        public IntRange(int minimum, int maximum) { Minimum = minimum; Maximum = maximum; if (minimum > maximum) throw new ArgumentException("数值区间无效。"); }
        public int Minimum { get; }
        public int Maximum { get; }
        public bool Contains(int value) => value >= Minimum && value <= Maximum;
    }

    public sealed class CollectibleDefinition
    {
        public CollectibleDefinition(string id, string name, string appearanceKey, int width, int height, CollectibleRarity rarity, int value)
        {
            if (string.IsNullOrWhiteSpace(id) || width < 1 || height < 1 || value < 0) throw new ArgumentOutOfRangeException();
            Id = id; Name = name; AppearanceKey = appearanceKey; Width = width; Height = height; Rarity = rarity; Value = value;
        }
        public string Id { get; } public string Name { get; } public string AppearanceKey { get; }
        public int Width { get; } public int Height { get; } public CollectibleRarity Rarity { get; } public int Value { get; }
    }

    public sealed class PackageConstraints
    {
        public PackageConstraints(IntRange totalValue, IReadOnlyDictionary<CollectibleRarity, IntRange> rarityValues)
        { TotalValue = totalValue; RarityValues = rarityValues ?? new Dictionary<CollectibleRarity, IntRange>(); }
        public IntRange TotalValue { get; } public IReadOnlyDictionary<CollectibleRarity, IntRange> RarityValues { get; }
        public bool IsSatisfiedBy(IReadOnlyList<CollectibleInstance> items)
        {
            if (!TotalValue.Contains(items.Sum(item => item.Definition.Value))) return false;
            return RarityValues.All(pair => pair.Value.Contains(items.Where(item => item.Definition.Rarity == pair.Key).Sum(item => item.Definition.Value)));
        }
    }

    public interface IRandomSource { int Next(int exclusiveMaximum); }
    public sealed class SystemRandomSource : IRandomSource { private readonly Random _random = new Random(); public int Next(int exclusiveMaximum) => _random.Next(exclusiveMaximum); }
    public sealed class SequenceRandomSource : IRandomSource
    {
        private readonly int[] _values; private int _index;
        public SequenceRandomSource(params int[] values) { _values = values == null || values.Length == 0 ? new[] { 0 } : values; }
        public int Next(int exclusiveMaximum) { if (exclusiveMaximum < 1) throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum)); return Math.Abs(_values[_index++ % _values.Length]) % exclusiveMaximum; }
    }

    public sealed class CollectibleInstance { public CollectibleInstance(int id, CollectibleDefinition definition) { Id = id; Definition = definition; } public int Id { get; } public CollectibleDefinition Definition { get; } }
    public sealed class PackageLayoutItem
    {
        public PackageLayoutItem(int instanceId, int x, int y, int width, int height) { InstanceId = instanceId; X = x; Y = y; Width = width; Height = height; }
        public int InstanceId { get; } public int X { get; } public int Y { get; } public int Width { get; } public int Height { get; }
    }
    public sealed class PackageLayout
    {
        public PackageLayout(int width, IReadOnlyList<PackageLayoutItem> items) { Width = width; Items = items; Height = items.Count == 0 ? 0 : items.Max(item => item.Y + item.Height); }
        public int Width { get; } public int Height { get; } public IReadOnlyList<PackageLayoutItem> Items { get; }
        public PackageLayoutItem For(int instanceId) => Items.Single(item => item.InstanceId == instanceId);
    }
    public sealed class GeneratedPackage
    {
        public GeneratedPackage(IReadOnlyList<CollectibleInstance> items, PackageLayout layout) { Items = items; Layout = layout; TotalValue = items.Sum(item => item.Definition.Value); }
        public IReadOnlyList<CollectibleInstance> Items { get; } public PackageLayout Layout { get; } public int TotalValue { get; }
    }
    public sealed class PackageGenerator
    {
        private readonly AuctionRules _rules; private readonly IRandomSource _random;
        public PackageGenerator(AuctionRules rules, IRandomSource random) { _rules = rules; _random = random; }
        public GeneratedPackage Generate()
        {
            for (var attempt = 0; attempt < 128; attempt++)
            {
                var items = Enumerable.Range(0, _rules.PackageItemCount).Select(index => new CollectibleInstance(index, _rules.Catalogue[_random.Next(_rules.Catalogue.Count)])).ToArray();
                if (_rules.PackageConstraints.IsSatisfiedBy(items)) return new GeneratedPackage(items, Layout(items));
            }
            throw new InvalidOperationException("当前包裹生成约束无解。");
        }
        private PackageLayout Layout(IReadOnlyList<CollectibleInstance> items)
        {
            var heights = new int[_rules.GridWidth]; var layout = new List<PackageLayoutItem>();
            foreach (var item in items)
            {
                if (item.Definition.Width > _rules.GridWidth) throw new InvalidOperationException("藏品宽度超过网格宽度。");
                var candidates = new List<int>();
                for (var x = 0; x <= _rules.GridWidth - item.Definition.Width; x++) if (Enumerable.Range(x, item.Definition.Width).Select(column => heights[column]).Distinct().Count() == 1) candidates.Add(x);
                if (candidates.Count == 0) throw new InvalidOperationException("当前包裹布局无解。");
                var column = candidates[_random.Next(candidates.Count)]; var y = heights[column];
                layout.Add(new PackageLayoutItem(item.Id, column, y, item.Definition.Width, item.Definition.Height));
                for (var index = column; index < column + item.Definition.Width; index++) heights[index] = y + item.Definition.Height;
            }
            return new PackageLayout(_rules.GridWidth, layout);
        }
    }
}
