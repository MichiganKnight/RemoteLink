using System.Net;
using System.Net.Sockets;

namespace RemoteLink.Host.Services
{
    public sealed class PairingPeerContextService
    {
        private const int MaxContexts = 32;

        private readonly Dictionary<Guid, PeerContext> _contexts = new();
        private readonly object _sync = new();

        public bool TryCreate(Guid requestId, Guid remoteDeviceId, string remoteAddress, string tlsThumbprint, string identityFingerprint, DateTime expiresUtc)
        {
            if (requestId == Guid.Empty || remoteDeviceId == Guid.Empty || expiresUtc <= DateTime.UtcNow)
            {
                return false;
            }

            if (!IPAddress.TryParse(remoteAddress, out IPAddress? address) || address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast))
            {
                return false;
            }

            string normalizedThumbprint = NormalizeHex(tlsThumbprint);

            string normalizedFingerprint = NormalizeHex(identityFingerprint);

            if (!IsValidHex(normalizedThumbprint, 40) || !IsValidHex(normalizedFingerprint, 64))
            {
                return false;
            }

            lock (_sync)
            {
                CleanupExpiredCore();

                if (_contexts.ContainsKey(requestId) || _contexts.Count >= MaxContexts)
                {
                    return false;
                }

                _contexts.Add(requestId, new PeerContext
                {
                    RequestId = requestId,
                    RemoteDeviceId = remoteDeviceId,
                    RemoteAddress = address.ToString(),
                    TlsThumbprint = normalizedThumbprint,
                    IdentityFingerprint = normalizedFingerprint,
                    ExpiresUtc = expiresUtc
                });

                return true;
            }
        }

        public PairingPeerContext? Get(Guid requestId)
        {
            lock (_sync)
            {
                CleanupExpiredCore();

                if (!_contexts.TryGetValue(requestId, out PeerContext? context))
                {
                    return null;
                }

                return new PairingPeerContext
                {
                    RequestId = context.RequestId,
                    RemoteDeviceId = context.RemoteDeviceId,
                    RemoteAddress = context.RemoteAddress,
                    TlsThumbprint = context.TlsThumbprint,
                    IdentityFingerprint = context.IdentityFingerprint,
                    ExpiresUtc = context.ExpiresUtc
                };
            }
        }

        public bool Remove(Guid requestId)
        {
            lock (_sync)
            {
                return _contexts.Remove(requestId);
            }
        }

        public int CleanupExpired()
        {
            lock (_sync)
            {
                return CleanupExpiredCore();
            }
        }

        private int CleanupExpiredCore()
        {
            DateTime now = DateTime.UtcNow;

            Guid[] expired = _contexts.Where(entry => entry.Value.ExpiresUtc <= now)
                .Select(entry => entry.Key)
                .ToArray();

            foreach (Guid requestId in expired)
            {
                _contexts.Remove(requestId);
            }

            return expired.Length;
        }

        private static string NormalizeHex(string? value)
        {
            return (value ?? string.Empty).Replace(" ", "").Replace(":", "").ToUpperInvariant();
        }

        private static bool IsValidHex(string value, int expectedLength)
        {
            return value.Length == expectedLength && value.All(Uri.IsHexDigit);
        }

        private sealed class PeerContext
        {
            public required Guid RequestId { get; init; }
            public required Guid RemoteDeviceId { get; init; }
            public required string RemoteAddress { get; init; }
            public required string TlsThumbprint { get; init; }
            public required string IdentityFingerprint { get; init; }
            public required DateTime ExpiresUtc { get; init; }
        }
    }

    public sealed class PairingPeerContext
    {
        public required Guid RequestId { get; init; }
        public required Guid RemoteDeviceId { get; init; }
        public required string RemoteAddress { get; init; }
        public required string TlsThumbprint { get; init; }
        public required string IdentityFingerprint { get; init; }
        public required DateTime ExpiresUtc { get; init; }
    }
}