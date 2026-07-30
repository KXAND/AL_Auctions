namespace AuctionGame.Fusion
{
    public interface IAuctionPresentationSession
    {
        AuctionWireView CurrentView { get; }
        string Status { get; }
        void SelectPrivateClue(string clueId);
        void SubmitBid(int amount);
    }
}
