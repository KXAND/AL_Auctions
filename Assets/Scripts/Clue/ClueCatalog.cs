using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AuctionGame
{
    [CreateAssetMenu(fileName = "ClueCatalog", menuName = "Game/Clue Catalog")]
    public sealed class ClueCatalog : ScriptableObject
    {
        [SerializeField] private Clue[] clues;

        public IReadOnlyList<Clue> All => clues ?? System.Array.Empty<Clue>();

        public Clue Find(int id)
        {
            if (clues == null)
            {
                return null;
            }
            return System.Array.Find(clues, clue => clue != null && clue.Id == id);
        }

        public Clue Pick(System.Random random)
        {
            Clue[] pool = clues;
            if (pool == null || pool.Length == 0)
            {
                return null;
            }
            return pool[random.Next(pool.Length)];
        }

        public IReadOnlyList<Clue> PickDistinct(System.Random random, int count)
        {
            List<Clue> pool = (clues ?? System.Array.Empty<Clue>())
                .Where(clue => clue != null)
                .ToList();
            List<Clue> result = new List<Clue>();
            while (result.Count < count && pool.Count > 0)
            {
                int index = random.Next(pool.Count);
                result.Add(pool[index]);
                pool.RemoveAt(index);
            }
            return result;
        }
    }
}
