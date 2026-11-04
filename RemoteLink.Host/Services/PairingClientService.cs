using System.Net;
using System.Security.Cryptography;
using RemoteLink.Core.Models;
using RemoteLink.Core.Models.Pairing;

namespace RemoteLink.Host.Services
{
    public sealed class PairingClientService(DeviceIdentityService identity, DeviceSecurityService security, PairingSecurityService pairingSecurity, PairingCryptoSessionService cryptoSessions)
    {
        public async Task<PairingProposalResponse> SendProposalAsync(StartPairingRequest request, CancellationToken cancellationToken)
        {
            if (request.DeviceId == Guid.Empty || request.DeviceId == identity.DeviceId)
            {
                throw new ArgumentException("Invalid remote device ID.");
            }

            if (!IPAddress.TryParse(request.Address, out IPAddress? address))
            {
                throw new ArgumentException("A valid LAN IP address is required.");
            }

            if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            {
                throw new ArgumentException("The selected IP address is not supported.");
            }

            string expectedThumbprint = request.ExpectedTlsThumbprint.Replace(" ", "").Replace(":", "").ToUpperInvariant();

            if (expectedThumbprint.Length != 40 || !expectedThumbprint.All(Uri.IsHexDigit))
            {
                throw new ArgumentException("A valid SHA-1 certificate thumbprint is required.");
            }

            PairingKeyExchange exchange = pairingSecurity.CreateKeyExchange();

            Guid requestId = Guid.NewGuid();

            bool sessionCreated;

            byte[] ephemeralPublicKey;

            try
            {
                sessionCreated = cryptoSessions.TryCreateSession(requestId, request.DeviceId, PairingCryptoRole.Initiator, exchange, null, out ephemeralPublicKey);
            }
            catch
            {
                exchange.Dispose();

                throw;
            }

            if (!sessionCreated)
            {
                exchange.Dispose();

                throw new InvalidOperationException("Unable to create an initiator pairing session.");
            }

            try
            {
                DeviceSecurityIdentity publicIdentity = security.GetPublicIdentity();

                PairingProposal proposal = new()
                {
                    RequestId = requestId,
                    DeviceId = identity.DeviceId,
                    DeviceName = identity.DeviceName,
                    IdentityPublicKey = publicIdentity.PublicKey,
                    EphemeralPublicKey = Convert.ToBase64String(ephemeralPublicKey)
                };

                using HttpClientHandler handler = new();

                byte[] expectedCertificateHash = Convert.FromHexString(expectedThumbprint);

                handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                {
                    if (certificate is null)
                    {
                        return false;
                    }

                    byte[] actualCertificateHash = certificate.GetCertHash();

                    return CryptographicOperations.FixedTimeEquals(actualCertificateHash, expectedCertificateHash);
                };

                using HttpClient client = new(handler)
                {
                    Timeout = TimeSpan.FromSeconds(10)
                };

                Uri endpoint = new($"https://{address}:5019/api/lan/pairing/propose");

                using HttpResponseMessage response = await client.PostAsJsonAsync(endpoint, proposal, cancellationToken);

                response.EnsureSuccessStatusCode();

                PairingProposalResponse? result = await response.Content.ReadFromJsonAsync<PairingProposalResponse>(cancellationToken: cancellationToken);

                if (result is null || result.RequestId != requestId || !result.AcceptedForReview)
                {
                    throw new InvalidOperationException("The remote Host returned an invalid pairing response.");
                }

                return result;
            }
            catch
            {
                cryptoSessions.RemoveSession(requestId);

                throw;
            }
        }

        public async Task<PairingHandshakeVerification> VerifyHandshakeAsync(Guid requestId, Guid expectedRemoteDeviceId, string remoteAddress, string expectedTlsThumbprint, CancellationToken cancellationToken)
        {
            if (requestId == Guid.Empty || expectedRemoteDeviceId == Guid.Empty || expectedRemoteDeviceId == identity.DeviceId)
            {
                throw new ArgumentException("Invalid pairing identifiers.");
            }

            if (!IPAddress.TryParse(remoteAddress, out IPAddress? address) || IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            {
                throw new ArgumentException("Invalid remote IP address.");
            }

            PairingCryptoSessionInfo? session = cryptoSessions.GetSession(requestId);

            if (session is null || session.Role != PairingCryptoRole.Initiator || session.RemoteDeviceId != expectedRemoteDeviceId || session.RemoteEphemeralPublicKey is not null)
            {
                throw new InvalidOperationException("No eligible initiator pairing session exists.");
            }

            string thumbprint = expectedTlsThumbprint.Replace(" ", "").Replace(":", "").ToUpperInvariant();

            if (thumbprint.Length != 40 || !thumbprint.All(Uri.IsHexDigit))
            {
                throw new ArgumentException("Invalid TLS certificate thumbprint.");
            }

            byte[] expectedCertificateHash = Convert.FromHexString(thumbprint);

            using HttpClientHandler handler = new();

            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null)
                {
                    return false;
                }

                return CryptographicOperations.FixedTimeEquals(certificate.GetCertHash(), expectedCertificateHash);
            };

            using HttpClient client = new(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            string host = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();

            Uri endpoint = new($"https://{host}:5019/api/lan/pairing/handshake/{requestId:D}");

            using HttpResponseMessage response = await client.PostAsync(endpoint, content: null, cancellationToken);

            response.EnsureSuccessStatusCode();

            PairingHandshakeResponse? handshake = await response.Content.ReadFromJsonAsync<PairingHandshakeResponse>(cancellationToken: cancellationToken);

            if (handshake is null || handshake.SignedMessage is null || handshake.RequestId != requestId || handshake.ResponderDeviceId != expectedRemoteDeviceId || handshake.SignedMessage.RequestId != requestId ||
                handshake.SignedMessage.DeviceId != expectedRemoteDeviceId)
            {
                throw new CryptographicException("The responder returned inconsistent handshake identifiers.");
            }

            SignedPairingMessage signed = handshake.SignedMessage;

            byte[] responderIdentityKey;
            byte[] responderEphemeralKey;
            byte[] receivedTranscriptHash;

            try
            {
                responderIdentityKey = Convert.FromBase64String(signed.PublicKey);

                responderEphemeralKey = Convert.FromBase64String(signed.EphemeralPublicKey);

                receivedTranscriptHash = Convert.FromBase64String(signed.TranscriptHash);
            }
            catch (FormatException exception)
            {
                throw new CryptographicException("The handshake contains invalid key encoding.", exception);
            }

            if (!pairingSecurity.IsValidIdentityPublicKey(signed.PublicKey) || !pairingSecurity.IsValidEphemeralPublicKey(signed.EphemeralPublicKey))
            {
                throw new CryptographicException("The responder supplied an invalid public key.");
            }

            DeviceSecurityIdentity localIdentity = security.GetPublicIdentity();

            byte[] initiatorIdentityKey = Convert.FromBase64String(localIdentity.PublicKey);

            byte[] transcript = pairingSecurity.CreateTranscript(requestId, identity.DeviceId, expectedRemoteDeviceId, initiatorIdentityKey, responderIdentityKey, session.LocalEphemeralPublicKey, responderEphemeralKey);

            byte[] expectedTranscriptHash = pairingSecurity.CreateTranscriptHash(transcript);

            if (!pairingSecurity.VerifySignedMessage(signed, responderIdentityKey, responderEphemeralKey, expectedTranscriptHash, requestId, expectedRemoteDeviceId))
            {
                throw new CryptographicException("The responder's signed handshake could not be verified.");
            }

            if (!CryptographicOperations.FixedTimeEquals(receivedTranscriptHash, expectedTranscriptHash))
            {
                throw new CryptographicException("Pairing transcript verification failed.");
            }

            if (!cryptoSessions.TrySetRemoteEphemeralPublicKey(requestId, responderEphemeralKey))
            {
                throw new InvalidOperationException("The pairing session was already updated or expired.");
            }

            if (!cryptoSessions.TryDeriveSharedSecret(requestId, out byte[] sharedSecret))
            {
                throw new CryptographicException("Unable to derive the pairing shared secret.");
            }

            string verificationCode;

            try
            {
                byte[] verificationKey = pairingSecurity.DeriveVerificationKey(sharedSecret, expectedTranscriptHash);

                try
                {
                    verificationCode = pairingSecurity.CreateVerificationCode(verificationKey, expectedTranscriptHash);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(verificationKey);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(sharedSecret);
            }

            return new PairingHandshakeVerification
            {
                RequestId = requestId,
                RemoteDeviceId = expectedRemoteDeviceId,
                RemoteDeviceName = handshake.ResponderDeviceName,
                RemoteIdentityFingerprint = Convert.ToHexString(SHA256.HashData(responderIdentityKey)),
                VerificationCode = verificationCode,
                SignatureVerified = true
            };
        }

        public async Task<PairingHandshakeVerification> StartDiscoveredPairingAsync(Guid remoteDeviceId, string remoteAddress, CancellationToken cancellationToken)
        {
            if (remoteDeviceId == Guid.Empty || remoteDeviceId == identity.DeviceId)
            {
                throw new ArgumentException("Invalid remote device ID.");
            }

            if (!IPAddress.TryParse(remoteAddress, out IPAddress? address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
                address.Equals(IPAddress.Broadcast) || address.IsIPv6LinkLocal || address.IsIPv6Multicast)
            {
                throw new ArgumentException("Invalid LAN address.");
            }

            byte[]? observedCertificateHash = null;

            using HttpClientHandler handler = new();

            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null)
                {
                    return false;
                }

                byte[] currentHash = certificate.GetCertHash();

                if (observedCertificateHash is null)
                {
                    observedCertificateHash = currentHash;

                    return true;
                }

                return CryptographicOperations.FixedTimeEquals(observedCertificateHash, currentHash);
            };

            using HttpClient httpClient = new(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            Uri identityEndpoint = new($"https://{address}:5019/api/lan/identity");

            using HttpResponseMessage identityResponse = await httpClient.GetAsync(identityEndpoint, cancellationToken);

            identityResponse.EnsureSuccessStatusCode();

            DeviceSecurityIdentity? remoteIdentity = await identityResponse.Content.ReadFromJsonAsync<DeviceSecurityIdentity>(cancellationToken: cancellationToken);

            if (remoteIdentity is null || remoteIdentity.DeviceId != remoteDeviceId || !pairingSecurity.IsValidIdentityPublicKey(remoteIdentity.PublicKey))
            {
                throw new CryptographicException("The remote identity does not match the discovered device.");
            }

            if (observedCertificateHash is null)
            {
                throw new CryptographicException("The remote TLS certificate could not be observed.");
            }

            string observedThumbprint = Convert.ToHexString(observedCertificateHash);

            PairingProposalResponse proposal = await SendProposalAsync(new StartPairingRequest
                {
                    DeviceId = remoteDeviceId,
                    Address = remoteAddress,
                    ExpectedTlsThumbprint = observedThumbprint
                },
                cancellationToken);

            try
            {
                PairingHandshakeVerification verification = await VerifyHandshakeAsync(proposal.RequestId, remoteDeviceId, remoteAddress, observedThumbprint, cancellationToken);

                byte[] expectedFingerprint = SHA256.HashData(Convert.FromBase64String(remoteIdentity.PublicKey));

                byte[] verifiedFingerprint = Convert.FromHexString(verification.RemoteIdentityFingerprint);

                if (!CryptographicOperations.FixedTimeEquals(expectedFingerprint, verifiedFingerprint))
                {
                    throw new CryptographicException("The responder identity changed during pairing.");
                }

                return verification;
            }
            catch
            {
                cryptoSessions.RemoveSession(proposal.RequestId);
                
                throw;
            }
        }
    }
}