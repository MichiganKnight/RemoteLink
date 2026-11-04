using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using RemoteLink.Core.Models.Authentication;
using RemoteLink.Core.Models.Pairing;

namespace RemoteLink.Host.Services
{
    public sealed class PeerAuthenticationService(DeviceIdentityService identity, DeviceSecurityService deviceSecurity, TrustedDeviceService trustedDevices, TlsCertificateService tlsCertificates)
    {
        private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(30);

        private readonly ConcurrentDictionary<Guid, PendingChallenge> _challenges = new();
        private readonly ConcurrentDictionary<string, PeerSession> _sessions = new();

        private readonly object _challengeSync = new();

        private const int MaxPendingChallenges = 1024;

        public PeerChallengeResponse CreateChallenge(Guid remoteDeviceId)
        {
            if (remoteDeviceId == Guid.Empty || remoteDeviceId == identity.DeviceId)
            {
                throw new ArgumentException("Invalid remote device ID.");
            }

            if (trustedDevices.GetDevice(remoteDeviceId) is not { IsTrusted: true })
            {
                throw new UnauthorizedAccessException("The remote device is not trusted.");
            }

            lock (_challengeSync)
            {
                RemoveExpiredChallenges();

                if (_challenges.Count >= MaxPendingChallenges)
                {
                    throw new InvalidOperationException("Too many pending authentication challenges.");
                }
                
                Guid challengeId = Guid.NewGuid();

                byte[] nonce = RandomNumberGenerator.GetBytes(32);

                DateTime expiresUtc = DateTime.UtcNow.Add(ChallengeLifetime);

                PendingChallenge challenge = new()
                {
                    ChallengeId = challengeId,
                    RemoteDeviceId = remoteDeviceId,
                    ServerDeviceId = identity.DeviceId,
                    Nonce = nonce,
                    ExpiresUtc = expiresUtc
                };
                
                if (!_challenges.TryAdd(challengeId, challenge))
                {
                    throw new InvalidOperationException("Unable to create authentication challenge.");
                }

                byte[] certificateHash = SHA256.HashData(tlsCertificates.GetCertificate().RawData);
                
                byte[] payload = BuildChallengePayload(challengeId, remoteDeviceId, identity.DeviceId, nonce, expiresUtc, certificateHash);
                
                byte[] signature = deviceSecurity.SignData(payload);

                return new PeerChallengeResponse
                {
                    ChallengeId = challengeId,
                    ServerDeviceId = identity.DeviceId,
                    Nonce = Convert.ToBase64String(nonce),
                    ExpiresUtc = expiresUtc,
                    ServerSignature = Convert.ToBase64String(signature),
                    ServerCertificateHash = Convert.ToHexString(certificateHash)
                };
            }
        }

        public PeerAuthenticationResponse Authenticate(PeerAuthenticationRequest request)
        {
            PendingChallenge challenge;

            lock (_challengeSync)
            {
                if (!_challenges.TryRemove(request.ChallengeId, out PendingChallenge? pending))
                {
                    throw new UnauthorizedAccessException("Authentication challenge was not found.");
                }

                challenge = pending;
            }

            if (challenge.ExpiresUtc <= DateTime.UtcNow || challenge.RemoteDeviceId != request.DeviceId || challenge.ServerDeviceId != identity.DeviceId)
            {
                throw new UnauthorizedAccessException("Authentication challenge is invalid or expired.");
            }
            
            TrustedDevice? trusted = trustedDevices.GetDevice(request.DeviceId);
            
            if (trusted is not { IsTrusted: true })
            {
                throw new UnauthorizedAccessException("The remote device is not trusted.");
            }

            byte[] signature;
            byte[] publicKey;

            try
            {
                signature = Convert.FromBase64String(request.Signature);
                publicKey = Convert.FromBase64String(trusted.PublicKey);
            }
            catch (FormatException)
            {
                throw new UnauthorizedAccessException("Invalid authentication signature encoding.");
            }
            
            byte[] certificateHash = SHA256.HashData(tlsCertificates.GetCertificate().RawData);

            byte[] payload = BuildChallengePayload(challenge.ChallengeId, challenge.RemoteDeviceId, challenge.ServerDeviceId, challenge.Nonce, challenge.ExpiresUtc, certificateHash);

            using ECDsa verifier = ECDsa.Create();

            try
            {
                verifier.ImportSubjectPublicKeyInfo(publicKey, out int bytesRead);

                if (bytesRead != publicKey.Length || verifier.KeySize != 256 || !verifier.VerifyData(payload, signature, HashAlgorithmName.SHA256))
                {
                    throw new UnauthorizedAccessException("Authentication signature verification failed.");
                }
            }
            catch (CryptographicException)
            {
                throw new UnauthorizedAccessException("Invalid trusted device public key.");
            }

            byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);

            string token = Convert.ToBase64String(tokenBytes);

            string tokenHash = Convert.ToHexString(SHA256.HashData(tokenBytes));

            DateTime sessionExpiresUtc = DateTime.UtcNow.Add(SessionLifetime);

            PeerSession session = new()
            {
                SessionId = Guid.NewGuid(),
                DeviceId = request.DeviceId,
                ExpiresUtc = sessionExpiresUtc
            };

            _sessions[tokenHash] = session;

            return new PeerAuthenticationResponse
            {
                SessionId = session.SessionId,
                ServerDeviceId = identity.DeviceId,
                AccessToken = token,
                ExpiresUtc = sessionExpiresUtc
            };
        }

        public bool TryValidateSession(string? accessToken, out Guid remoteDeviceId)
        {
            remoteDeviceId = Guid.Empty;

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return false;
            }

            byte[] tokenBytes;

            try
            {
                tokenBytes = Convert.FromBase64String(accessToken);
            }
            catch (FormatException)
            {
                return false;
            }

            if (tokenBytes.Length != 32)
            {
                return false;
            }

            string tokenHash = Convert.ToHexString(SHA256.HashData(tokenBytes));

            if (!_sessions.TryGetValue(tokenHash, out PeerSession? session))
            {
                return false;
            }

            if (session.ExpiresUtc <= DateTime.UtcNow)
            {
                _sessions.TryRemove(tokenHash, out _);

                return false;
            }

            if (trustedDevices.GetDevice(session.DeviceId) is not { IsTrusted: true })
            {
                _sessions.TryRemove(tokenHash, out _);

                return false;
            }

            remoteDeviceId = session.DeviceId;

            return true;
        }

        public static byte[] BuildChallengePayload(Guid challengeId, Guid remoteDeviceId, Guid serverDeviceId, byte[] nonce, DateTime expiresUtc, byte[] certificateHash) {
            string payload = string.Join("\n", "RemoteLink-Peer-Authentication-v2", challengeId.ToString("D"), remoteDeviceId.ToString("D"), serverDeviceId.ToString("D"), Convert.ToBase64String(nonce), expiresUtc.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), Convert.ToHexString(certificateHash));

            return Encoding.UTF8.GetBytes(payload);
        }
        
        private void RemoveExpiredChallenges()
        {
            DateTime now = DateTime.UtcNow;

            foreach (KeyValuePair<Guid, PendingChallenge> entry in _challenges)
            {
                if (entry.Value.ExpiresUtc <= now)
                {
                    _challenges.TryRemove(entry.Key, out _);
                }
            }
        }
        
        private sealed class PendingChallenge
        {
            public Guid ChallengeId { get; init; }

            public Guid RemoteDeviceId { get; init; }

            public Guid ServerDeviceId { get; init; }

            public byte[] Nonce { get; init; } = [];

            public DateTime ExpiresUtc { get; init; }
        }

        private sealed class PeerSession
        {
            public Guid SessionId { get; init; }

            public Guid DeviceId { get; init; }

            public DateTime ExpiresUtc { get; init; }
        }
    }
}