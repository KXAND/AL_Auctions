namespace AuctionGame.Network
{
    public enum AuctionDemoLaunchMode
    {
        None,
        Online,
        Offline
    }

    public static class AuctionDemoLaunch
    {
        private static AuctionDemoLaunchMode _requestedMode;

        public static void RequestOnline()
        {
            _requestedMode = AuctionDemoLaunchMode.Online;
        }

        public static void RequestOffline()
        {
            _requestedMode = AuctionDemoLaunchMode.Offline;
        }

        public static bool TryConsume(out AuctionDemoLaunchMode mode)
        {
            mode = _requestedMode;
            _requestedMode = AuctionDemoLaunchMode.None;
            return mode != AuctionDemoLaunchMode.None;
        }
    }
}
