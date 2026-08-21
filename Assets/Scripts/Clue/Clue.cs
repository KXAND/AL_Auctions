using UnityEngine;

namespace AuctionGame
{
    [CreateAssetMenu(fileName = "Clue", menuName = "Game/Clue")]
    public sealed class Clue : ScriptableObject
    {
        [SerializeField] private int id;
        [SerializeField] private string displayName;
        [SerializeField] private Sprite image;

        public int Id => id;
        public string DisplayName => displayName;
        public Sprite Image => image;
    }
}
