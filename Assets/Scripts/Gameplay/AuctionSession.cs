using System;
using System.Collections.Generic;
using System.Linq;

namespace AuctionGame.Gameplay
{
    public sealed class AuctionConnection
    {
        internal AuctionConnection(string identity, int availableAssets)
        {
            if (string.IsNullOrWhiteSpace(identity)) throw new ArgumentException("连接身份不能为空。", nameof(identity));
            Identity = identity;
            AvailableAssets = availableAssets;
            IsConnected = true;
        }

        public string Identity { get; }
        public int AvailableAssets { get; private set; }
        public bool IsConnected { get; private set; }

        internal void SetAvailableAssets(int value) => AvailableAssets = value;
        internal void Disconnect() => IsConnected = false;
    }

    public sealed class AuctionSession
    {
        private readonly AuctionRules _rules;
        private readonly IRandomSource _random;
        private readonly List<AuctionConnection> _waiting = new List<AuctionConnection>();
        private readonly Dictionary<AuctionConnection, int> _seatByConnection = new Dictionary<AuctionConnection, int>();
        private long _connectionSequence;

        public AuctionSession(AuctionRules rules, IRandomSource random)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _random = random ?? new SystemRandomSource();
        }

        public AuctionMatch CurrentMatch { get; private set; }

        public AuctionConnection OpenConnection(string connectionLabel)
        {
            var connection = new AuctionConnection($"{connectionLabel}#{++_connectionSequence}", _rules.InitialAssets);
            _waiting.Add(connection);
            return connection;
        }

        public AuctionMatch StartNextMatch()
        {
            if (CurrentMatch != null && CurrentMatch.Phase != AuctionPhase.Settlement) throw new InvalidOperationException("当前对局尚未结束。");
            ReturnConnectedPlayersToWaitingQueue();
            CurrentMatch = AuctionMatch.Create(_rules, _random);
            foreach (var connection in _waiting.Where(item => item.IsConnected).Take(_rules.PlayerCount).ToArray())
            {
                _seatByConnection[connection] = CurrentMatch.ConnectHuman(connection);
                _waiting.Remove(connection);
            }
            CurrentMatch.Start();
            return CurrentMatch;
        }

        public void Disconnect(AuctionConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (_seatByConnection.TryGetValue(connection, out var seatIndex))
            {
                CurrentMatch.DisconnectHuman(seatIndex);
                _seatByConnection.Remove(connection);
            }
            else
            {
                connection.Disconnect();
            }
            _waiting.Remove(connection);
        }

        public int SeatOf(AuctionConnection connection)
        {
            if (connection == null || !_seatByConnection.TryGetValue(connection, out var seatIndex)) throw new InvalidOperationException("该连接当前没有对局席位。");
            return seatIndex;
        }

        public bool TryGetSeat(AuctionConnection connection, out int seatIndex)
        {
            if (connection == null)
            {
                seatIndex = -1;
                return false;
            }

            return _seatByConnection.TryGetValue(connection, out seatIndex);
        }

        public bool IsWaiting(AuctionConnection connection)
        {
            return connection != null && _waiting.Contains(connection);
        }

        private void ReturnConnectedPlayersToWaitingQueue()
        {
            foreach (var connection in _seatByConnection.Keys.ToArray())
            {
                if (connection.IsConnected && !_waiting.Contains(connection)) _waiting.Add(connection);
            }
            _seatByConnection.Clear();
        }
    }
}
