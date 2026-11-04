using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using RemoteLink.Core.Models;
using RemoteLink.Core.Models.Pairing;

namespace RemoteLink.Host.Services
{
    public sealed class PairingClientService(DeviceIdentityService identity, DeviceSecurityService security, PairingSecurityService pairingSecurity, PairingCryptoSessionService cryptoSessions, PairingDecisionSecurityService decisionSecurity, PairingApprovalService approvals, PairingPeerContextService peerContexts)
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

                await ConfirmInitiatorAsync(proposal.RequestId, remoteDeviceId, remoteAddress, observedThumbprint, verification.RemoteIdentityFingerprint, cancellationToken);

                return verification;
            }
            catch
            {
                cryptoSessions.RemoveSession(proposal.RequestId);

                throw;
            }
        }

        public async Task ConfirmInitiatorAsync(Guid requestId, Guid expectedRemoteDeviceId, string remoteAddress, string expectedTlsThumbprint, string expectedRemoteIdentityFingerprint, CancellationToken cancellationToken)
        {
            if (requestId == Guid.Empty || expectedRemoteDeviceId == Guid.Empty || expectedRemoteDeviceId == identity.DeviceId)
            {
                throw new ArgumentException("Invalid pairing identifiers.");
            }

            if (!IPAddress.TryParse(remoteAddress, out IPAddress? address) || address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast))
            {
                throw new ArgumentException("Invalid remote LAN address.");
            }

            PairingCryptoSessionInfo? session = cryptoSessions.GetSession(requestId);

            if (session is null || session.Role != PairingCryptoRole.Initiator || session.RemoteDeviceId != expectedRemoteDeviceId || session.RemoteEphemeralPublicKey is null)
            {
                throw new InvalidOperationException("No verified initiator session is available.");
            }

            DeviceSecurityIdentity localIdentity = security.GetPublicIdentity();

            byte[] initiatorIdentityKey = Convert.FromBase64String(localIdentity.PublicKey);

            string thumbprint = expectedTlsThumbprint.Replace(" ", "").Replace(":", "").ToUpperInvariant();

            if (thumbprint.Length != 40 || !thumbprint.All(Uri.IsHexDigit))
            {
                throw new ArgumentException("Invalid TLS certificate thumbprint.");
            }

            byte[] expectedCertificateHash = Convert.FromHexString(thumbprint);

            using HttpClientHandler handler = new();

            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate is not null && CryptographicOperations.FixedTimeEquals(certificate.GetCertHash(), expectedCertificateHash);

            using HttpClient client = new(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            Uri identityEndpoint = new($"https://{address}:5019/api/lan/identity");

            DeviceSecurityIdentity? responderIdentity = await client.GetFromJsonAsync<DeviceSecurityIdentity>(identityEndpoint, cancellationToken);

            if (responderIdentity is null || responderIdentity.DeviceId != expectedRemoteDeviceId || !pairingSecurity.IsValidIdentityPublicKey(responderIdentity.PublicKey))
            {
                throw new CryptographicException("The responder identity is invalid.");
            }

            byte[] responderIdentityKey = Convert.FromBase64String(responderIdentity.PublicKey);

            byte[] actualFingerprint = SHA256.HashData(responderIdentityKey);

            byte[] expectedFingerprint;

            try
            {
                expectedFingerprint = Convert.FromHexString(expectedRemoteIdentityFingerprint);
            }
            catch (FormatException exception)
            {
                throw new CryptographicException("The expected responder fingerprint is invalid.", exception);
            }

            if (!CryptographicOperations.FixedTimeEquals(actualFingerprint, expectedFingerprint))
            {
                throw new CryptographicException("The responder identity changed during pairing confirmation.");
            }

            if (!cryptoSessions.TryCreateTranscript(requestId, identity.DeviceId, expectedRemoteDeviceId, initiatorIdentityKey, responderIdentityKey, pairingSecurity, out byte[] transcriptHash))
            {
                throw new InvalidOperationException("Unable to reconstruct the initiator transcript.");
            }

            SignedPairingMessage signedMessage = pairingSecurity.CreateSignedMessage(requestId, identity.DeviceId, initiatorIdentityKey, session.LocalEphemeralPublicKey, transcriptHash, security);

            PairingInitiatorConfirmation confirmation = new()
            {
                SignedMessage = signedMessage
            };

            Uri endpoint = new($"https://{address}:5019/api/lan/pairing/confirm-initiator/{requestId:D}");

            using HttpResponseMessage response = await client.PostAsJsonAsync(endpoint, confirmation, cancellationToken);

            response.EnsureSuccessStatusCode();
            
            PairingCryptoSessionInfo? confirmedSession = cryptoSessions.GetSession(requestId);

            if (confirmedSession is null || confirmedSession.Role != PairingCryptoRole.Initiator || confirmedSession.RemoteDeviceId != expectedRemoteDeviceId || confirmedSession.ExpiresUtc <= DateTime.UtcNow)
            {
                throw new InvalidOperationException("The initiator pairing session expired during confirmation.");
            }

            if (!approvals.TryCreate(requestId, expectedRemoteDeviceId, confirmedSession.ExpiresUtc))
            {
                throw new InvalidOperationException("Unable to initialize initiator pairing approval state.");
            }
            
            
            if (!peerContexts.TryCreate(requestId, expectedRemoteDeviceId, remoteAddress, expectedTlsThumbprint, expectedRemoteIdentityFingerprint, confirmedSession.ExpiresUtc))
            {
                approvals.Remove(requestId);

                throw new InvalidOperationException("Unable to retain the verified remote pairing context.");
            }

        }

        public async Task<PairingApprovalSnapshot> SendDecisionAsync(SendPairingDecisionRequest request, CancellationToken cancellationToken)
        {
            if (request.RequestId == Guid.Empty || request.RemoteDeviceId == Guid.Empty || request.RemoteDeviceId == identity.DeviceId)
            {
                throw new ArgumentException("Invalid pairing identifiers.");
            }

            if (!IPAddress.TryParse(request.RemoteAddress, out IPAddress? address) || address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast))
            {
                throw new ArgumentException("Invalid remote LAN address.");
            }

            PairingCryptoSessionInfo? session = cryptoSessions.GetSession(request.RequestId);

            if (session is null || session.Role != PairingCryptoRole.Initiator || session.RemoteDeviceId != request.RemoteDeviceId || session.RemoteEphemeralPublicKey is null || session.ExpiresUtc <= DateTime.UtcNow)
            {
                throw new InvalidOperationException("The authenticated initiator session is unavailable.");
            }

            string thumbprint = request.ExpectedTlsThumbprint.Replace(" ", "").Replace(":", "").ToUpperInvariant();

            if (thumbprint.Length != 40 || !thumbprint.All(Uri.IsHexDigit))
            {
                throw new ArgumentException("Invalid TLS certificate thumbprint.");
            }

            byte[] expectedCertificateHash = Convert.FromHexString(thumbprint);

            using HttpClientHandler handler = new();

            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate is not null && CryptographicOperations.FixedTimeEquals(certificate.GetCertHash(), expectedCertificateHash);

            using HttpClient client = new(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            Uri identityEndpoint = new($"https://{address}:5019/api/lan/identity");

            DeviceSecurityIdentity? responderIdentity = await client.GetFromJsonAsync<DeviceSecurityIdentity>(identityEndpoint, cancellationToken);

            if (responderIdentity is null || responderIdentity.DeviceId != request.RemoteDeviceId || !pairingSecurity.IsValidIdentityPublicKey(responderIdentity.PublicKey))
            {
                throw new CryptographicException("The remote device identity is invalid.");
            }

            byte[] responderIdentityKey = Convert.FromBase64String(responderIdentity.PublicKey);

            byte[] expectedFingerprint;

            try
            {
                expectedFingerprint = Convert.FromHexString(request.ExpectedRemoteIdentityFingerprint);
            }
            catch (FormatException exception)
            {
                throw new CryptographicException("The expected identity fingerprint is invalid.", exception);
            }

            byte[] actualFingerprint = SHA256.HashData(responderIdentityKey);

            if (!CryptographicOperations.FixedTimeEquals(expectedFingerprint, actualFingerprint))
            {
                throw new CryptographicException("The remote identity changed during pairing.");
            }

            byte[] initiatorIdentityKey = Convert.FromBase64String(security.GetPublicIdentity().PublicKey);

            if (!cryptoSessions.TryCreateTranscript(request.RequestId, identity.DeviceId, request.RemoteDeviceId, initiatorIdentityKey, responderIdentityKey, pairingSecurity, out byte[] transcriptHash))
            {
                throw new InvalidOperationException("Unable to reconstruct the pairing transcript.");
            }

            SignedPairingDecision decision = decisionSecurity.CreateDecision(request.RequestId, identity.DeviceId, request.Approved, transcriptHash, security);

            Uri endpoint = new($"https://{address}:5019/api/lan/pairing/decision/{request.RequestId:D}");

            using HttpResponseMessage response = await client.PostAsJsonAsync(endpoint, decision, cancellationToken);

            response.EnsureSuccessStatusCode();

            PairingApprovalSnapshot? result = await response.Content.ReadFromJsonAsync<PairingApprovalSnapshot>(cancellationToken: cancellationToken);

            return result ?? throw new InvalidOperationException("The remote Host returned an invalid pairing decision response.");
        }

        public async Task<PairingApprovalSnapshot> SendStoredDecisionAsync(LocalPairingDecisionRequest request, CancellationToken cancellationToken)
        {
            PairingPeerContext? peer = peerContexts.Get(request.RequestId);

            if (peer is null)
            {
                throw new InvalidOperationException("No verified remote pairing context exists.");
            }

            PairingApprovalSnapshot? state = approvals.GetState(request.RequestId);

            if (state is null || state.RemoteDeviceId != peer.RemoteDeviceId)
            {
                throw new InvalidOperationException("Pairing approval state is unavailable.");
            }

            PairingDecisionState requestedDecision = request.Approved ? PairingDecisionState.Approved : PairingDecisionState.Rejected;

            if (state.LocalDecision != PairingDecisionState.Pending && state.LocalDecision != requestedDecision)
            {
                throw new InvalidOperationException("The local pairing decision cannot be changed.");
            }

            if (state.Rejected && state.LocalDecision != requestedDecision)
            {
                throw new InvalidOperationException("The pairing request has already been rejected.");
            }

            PairingApprovalSnapshot? recorded = approvals.SetLocalApproval(request.RequestId, request.Approved);

            if (recorded is null)
            {
                throw new InvalidOperationException("Unable to record the local pairing decision.");
            }

            await SendDecisionAsync(new SendPairingDecisionRequest
                {
                    RequestId = request.RequestId,
                    RemoteDeviceId = peer.RemoteDeviceId,
                    RemoteAddress = peer.RemoteAddress,
                    ExpectedTlsThumbprint = peer.TlsThumbprint,
                    ExpectedRemoteIdentityFingerprint = peer.IdentityFingerprint,
                    Approved = request.Approved
                },
                cancellationToken);

            return recorded;
        }
        
        public async Task<PairingApprovalSnapshot> RefreshResponderDecisionAsync(Guid requestId, CancellationToken cancellationToken)
        {
            PairingPeerContext? peer = peerContexts.Get(requestId) ?? throw new InvalidOperationException("No verified pairing context exists.");

            PairingApprovalSnapshot? state = approvals.GetState(requestId);

            if (state is null || state.RemoteDeviceId != peer.RemoteDeviceId)
            {
                throw new InvalidOperationException("Pairing approval state is unavailable.");
            }

            PairingCryptoSessionInfo? session = cryptoSessions.GetSession(requestId);

            if (session is null || session.Role != PairingCryptoRole.Initiator || session.RemoteDeviceId != peer.RemoteDeviceId || session.RemoteEphemeralPublicKey is null || session.ExpiresUtc <= DateTime.UtcNow)
            {
                throw new InvalidOperationException("The initiator pairing session is unavailable.");
            }

            byte[] expectedCertificateHash = Convert.FromHexString(peer.TlsThumbprint);

            using HttpClientHandler handler = new();

            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate is not null && CryptographicOperations.FixedTimeEquals(certificate.GetCertHash(), expectedCertificateHash);

            using HttpClient httpClient = new(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            Uri identityEndpoint = new($"https://{peer.RemoteAddress}:5019/api/lan/identity");

            DeviceSecurityIdentity? responderIdentity = await httpClient.GetFromJsonAsync<DeviceSecurityIdentity>(identityEndpoint, cancellationToken);

            if (responderIdentity is null || responderIdentity.DeviceId != peer.RemoteDeviceId || !pairingSecurity.IsValidIdentityPublicKey(responderIdentity.PublicKey))
            {
                throw new CryptographicException("The responder identity is invalid.");
            }

            byte[] responderIdentityKey = Convert.FromBase64String(responderIdentity.PublicKey);

            byte[] actualFingerprint = SHA256.HashData(responderIdentityKey);

            byte[] expectedFingerprint = Convert.FromHexString(peer.IdentityFingerprint);

            if (!CryptographicOperations.FixedTimeEquals(actualFingerprint, expectedFingerprint))
            {
                throw new CryptographicException("The responder identity changed during pairing.");
            }

            Uri decisionEndpoint = new($"https://{peer.RemoteAddress}:5019/api/lan/pairing/decision/{requestId:D}");

            PairingDecisionResponse? response = await httpClient.GetFromJsonAsync<PairingDecisionResponse>(decisionEndpoint, cancellationToken);

            if (response is null || response.RequestId != requestId)
            {
                throw new CryptographicException("The responder returned an invalid decision response.");
            }

            if (response.AwaitingLocalDecision)
            {
                if (response.SignedDecision is not null)
                {
                    throw new CryptographicException("The responder returned inconsistent decision state.");
                }

                return state;
            }

            if (response.SignedDecision is null)
            {
                throw new CryptographicException("The responder decision is missing its signature.");
            }

            byte[] initiatorIdentityKey = Convert.FromBase64String(security.GetPublicIdentity().PublicKey);

            if (!cryptoSessions.TryCreateTranscript(requestId, identity.DeviceId, peer.RemoteDeviceId, initiatorIdentityKey, responderIdentityKey, pairingSecurity, out byte[] transcriptHash))
            {
                throw new InvalidOperationException("Unable to reconstruct the pairing transcript.");
            }

            bool verified = decisionSecurity.VerifyDecision(response.SignedDecision, requestId, peer.RemoteDeviceId, transcriptHash, responderIdentityKey);

            if (!verified)
            {
                throw new CryptographicException("The responder's signed decision could not be verified.");
            }

            PairingApprovalSnapshot? updated = approvals.SetRemoteApproval(requestId, peer.RemoteDeviceId, response.SignedDecision.Approved);

            if (updated is null)
            {
                throw new InvalidOperationException("The responder decision conflicts with existing pairing state.");
            }

            return updated;
        }
    }
}