namespace AuctionGame
{
    public interface IAuctionController
    {
        void RequestBid(int amount);
        void RequestClue(int clueId);
    }
}