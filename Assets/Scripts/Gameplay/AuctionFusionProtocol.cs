using System;
using System.Text;
using Fusion;
using Fusion.Sockets;
using Newtonsoft.Json;

namespace AuctionGame
{
    internal static class AuctionFusionProtocol
    {
        private const int MessageKeyMagic = 0x41554354;
        private const int MessageKeyVersion = 3;

        public const string Authenticate = "authenticate";
        public const string Authenticated = "authenticated";
        public const string AuthenticationRejected = "authentication-rejected";
        public const string Action = "action";
        public const string QueryState = "query-state";
        public const string Result = "result";
        public const string State = "state";

        public static ReliableKey CreateMessageKey(int sequence)
        {
            return ReliableKey.FromInts(MessageKeyMagic, MessageKeyVersion, sequence);
        }

        public static bool IsMessageKey(ReliableKey key)
        {
            key.GetInts(out int magic, out int version, out _, out _);
            return magic == MessageKeyMagic && version == MessageKeyVersion;
        }

        public static byte[] Encode(string type, object payload)
        {
            FusionEnvelope envelope = new FusionEnvelope
            {
                Type = type,
                Payload = payload == null ? null : JsonConvert.SerializeObject(payload)
            };
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(envelope));
        }

        public static FusionEnvelope Decode(ArraySegment<byte> bytes)
        {
            string json = Encoding.UTF8.GetString(bytes.Array, bytes.Offset, bytes.Count);
            return JsonConvert.DeserializeObject<FusionEnvelope>(json);
        }

        public static T Payload<T>(FusionEnvelope envelope)
        {
            return JsonConvert.DeserializeObject<T>(envelope.Payload);
        }

        internal sealed class FusionEnvelope
        {
            public string Type;
            public string Payload;
        }

    }
}
