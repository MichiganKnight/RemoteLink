using System.Security.Cryptography;

namespace RemoteLink.Host.Services
{
    public enum PairingCryptoRole
    {
        Initiator,
        Responder
    }

    public sealed class PairingCryptoSessionService : IDisposable
    {
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(2);

        private const int MaxSessions = 32;

        private readonly Dictionary<Guid, Session> _sessions = new();
        private readonly object _sync = new();

        private bool _disposed;

        public bool TryCreateSession(Guid requestId, Guid remoteDeviceId, PairingCryptoRole role, PairingKeyExchange exchange, byte[]? remoteEphemeralPublicKey, out byte[] localEphemeralPublicKey)
        {
            ArgumentNullException.ThrowIfNull(exchange);

            localEphemeralPublicKey = [];

            if (requestId == Guid.Empty || remoteDeviceId == Guid.Empty)
            {
                return false;
            }

            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                CleanupExpiredCore();

                if (_sessions.Count >= MaxSessions || _sessions.ContainsKey(requestId))
                {
                    return false;
                }

                byte[] publicKey = exchange.ExportPublicKey();

                Session session = new()
                {
                    RequestId = requestId,
                    RemoteDeviceId = remoteDeviceId,
                    Role = role,
                    Exchange = exchange,
                    LocalEphemeralPublicKey = publicKey,
                    RemoteEphemeralPublicKey = remoteEphemeralPublicKey is null ? null : (byte[])remoteEphemeralPublicKey.Clone(),
                    ExpiresUtc = DateTime.UtcNow.Add(SessionLifetime)
                };

                _sessions.Add(requestId, session);

                localEphemeralPublicKey = (byte[])publicKey.Clone();

                return true;
            }
        }

        public bool TryCreateTranscript(Guid requestId, Guid initiatorDeviceId, Guid responderDeviceId, byte[] initiatorIdentityKey, byte[] responderIdentityKey, PairingSecurityService security, out byte[] transcriptHash)
        {
            transcriptHash = [];

            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                CleanupExpiredCore();

                if (!_sessions.TryGetValue(requestId, out Session? session) || session.RemoteEphemeralPublicKey is null)
                {
                    return false;
                }
                
                byte[] initiatorEphemeralKey = session.Role == PairingCryptoRole.Initiator ? session.LocalEphemeralPublicKey : session.RemoteEphemeralPublicKey;

                byte[] responderEphemeralKey = session.Role == PairingCryptoRole.Responder ? session.LocalEphemeralPublicKey : session.RemoteEphemeralPublicKey;

                byte[] transcript = security.CreateTranscript(requestId, initiatorDeviceId, responderDeviceId, initiatorIdentityKey, responderIdentityKey, initiatorEphemeralKey, responderEphemeralKey);
                
                transcriptHash = security.CreateTranscriptHash(transcript);
                
                return true;
            }
        }

        public PairingCryptoSessionInfo? GetSession(Guid requestId)
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                CleanupExpiredCore();

                if (!_sessions.TryGetValue(requestId, out Session? session))
                {
                    return null;
                }

                return new PairingCryptoSessionInfo
                {
                    RequestId = session.RequestId,
                    RemoteDeviceId = session.RemoteDeviceId,
                    Role = session.Role,
                    ExpiresUtc = session.ExpiresUtc,
                    LocalEphemeralPublicKey = (byte[])session.LocalEphemeralPublicKey.Clone(),
                    RemoteEphemeralPublicKey = session.RemoteEphemeralPublicKey is null ? null : (byte[])session.RemoteEphemeralPublicKey.Clone()
                };
            }
        }

        public bool TrySetRemoteEphemeralPublicKey(Guid requestId, byte[] publicKey)
        {
            ArgumentNullException.ThrowIfNull(publicKey);
            
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                CleanupExpiredCore();

                if (!_sessions.TryGetValue(requestId, out Session? session) || session.RemoteEphemeralPublicKey is not null)
                {
                    return false;
                }
                
                session.RemoteEphemeralPublicKey = (byte[])publicKey.Clone();
                
                return true;
            }
        }

        public bool TryDeriveSharedSecret(Guid requestId, out byte[]? sharedSecret)
        {
            sharedSecret = [];
            
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                CleanupExpiredCore();
                
                if (!_sessions.TryGetValue(requestId, out Session? session) || session.RemoteEphemeralPublicKey is null)
                {
                    return false;
                }
                
                sharedSecret = session.Exchange.DeriveSharedSecret(session.RemoteEphemeralPublicKey);
                
                return true;
            }
        }

        public bool RemoveSession(Guid requestId)
        {
            lock (_sync)
            {
                if (!_sessions.Remove(requestId, out Session? session))
                {
                    return false;
                }
                
                session.Dispose();
                
                return true;
            }
        }

        public int CleanupExpired()
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                return CleanupExpiredCore();
            }
        }

        private int CleanupExpiredCore()
        {
            DateTime now = DateTime.UtcNow;
            
            Guid[] expiredIds = _sessions.Where(pair => pair.Value.ExpiresUtc <= now)
                .Select(pair => pair.Key)
                .ToArray();
            
            foreach (Guid id in expiredIds)
            {
                Session session = _sessions[id];
                
                _sessions.Remove(id);
                session.Dispose();
            }
            
            return expiredIds.Length;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }
                
                _disposed = true;

                foreach (Session session in _sessions.Values)
                {
                    session.Dispose();
                }
                
                _sessions.Clear();
            }
        }

        private sealed class Session : IDisposable
        {
            public required Guid RequestId { get; init; }
            public required Guid RemoteDeviceId { get; init; }
            public required PairingCryptoRole Role { get; init; }
            public required PairingKeyExchange Exchange { get; init; }
            public required byte[] LocalEphemeralPublicKey { get; init; }

            public byte[]? RemoteEphemeralPublicKey { get; set; }

            public required DateTime ExpiresUtc { get; init; }

            public void Dispose()
            {
                Exchange.Dispose();

                if (RemoteEphemeralPublicKey is not null)
                {
                    CryptographicOperations.ZeroMemory(RemoteEphemeralPublicKey);
                }
            }
        }
    }

    public sealed class PairingCryptoSessionInfo
    {
        public required Guid RequestId { get; init; }
        public required Guid RemoteDeviceId { get; init; }
        public required PairingCryptoRole Role { get; init; }
        public required DateTime ExpiresUtc { get; init; }

        public required byte[] LocalEphemeralPublicKey { get; init; }
        public byte[]? RemoteEphemeralPublicKey { get; init; }
    }
}