using System;
using UnityEngine;

namespace AuctionGame
{
    public sealed class HumanController : MonoBehaviour, IAuctionController
    {
        [NonSerialized] private AuctionManager _auctionManager;

        public void Bind(AuctionManager auctionManager)
        {
            _auctionManager = auctionManager;
        }

        public void RequestBid(int amount)
        {
            _auctionManager.RequestAction(this, AuctionActionType.Bid, amount);
        }

        public void RequestClue(int clueId)
        {
            _auctionManager.RequestAction(this, AuctionActionType.Clue, clueId);
        }
    }
}