using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using RemoteLink.Core.Models.Authentication;
using RemoteLink.Core.Models.Pairing;

namespace RemoteLink.Host.Services
{
    public sealed class TrustedPeerClientService(DeviceIdentityService identity, DeviceSecurityService deviceSecurity, TrustedDeviceService trustedDevices, TlsCertificateService tlsCertificates)
    {
        public async Task<AuthenticatedPeerConnection> ConnectAsync(Guid remoteDeviceId, string remoteAddress, CancellationToken cancellationToken = default)
        {
            return await ConnectCoreAsync(remoteDeviceId, remoteAddress, cancellationToken);
        }

        public async Task<PeerAuthenticationResponse> AuthenticateAsync(Guid remoteDeviceId, string remoteAddress, CancellationToken cancellationToken = default)
        {
            using AuthenticatedPeerConnection connection = await ConnectCoreAsync(remoteDeviceId, remoteAddress, cancellationToken);

            return connection.GetSessionInfo();
        }
        
        private async Task<AuthenticatedPeerConnection> ConnectCoreAsync(Guid remoteDeviceId, string remoteAddress, CancellationToken cancellationToken = default)
        {
            if (remoteDeviceId == Guid.Empty || remoteDeviceId == identity.DeviceId)
            {
                throw new ArgumentException("Invalid remote device ID.");
            }
            
            TrustedDevice? trusted = trustedDevices.GetDevice(remoteDeviceId);
            
            if (trusted is not { IsTrusted: true })
            {
                throw new UnauthorizedAccessException("The remote device is not trusted.");
            }

            if (!IPAddress.TryParse(remoteAddress, out IPAddress? address) || IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal)
            {
                throw new ArgumentException("Invalid remote LAN address.");
            }

            byte[] trustedPublicKey = Convert.FromBase64String(trusted.PublicKey);

            using ECDsa verifier = ECDsa.Create();

            verifier.ImportSubjectPublicKeyInfo(trustedPublicKey, out int bytesRead);

            if (bytesRead != trustedPublicKey.Length || verifier.KeySize != 256)
            {
                throw new CryptographicException("Invalid trusted device identity.");
            }

            HttpClientHandler handler = new();

            byte[]? observedCertificateHash = null;

            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null)
                {
                    return false;
                }

                byte[] actualHash = SHA256.HashData(certificate.GetRawCertData());

                if (observedCertificateHash is null)
                {
                    observedCertificateHash = actualHash;

                    return true;
                }

                return CryptographicOperations.FixedTimeEquals(observedCertificateHash, actualHash);
            };

            HttpClient client = new(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            try
            {
                string host = address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();

                string baseUrl = $"https://{host}:5019/api/lan/auth";

                PeerChallengeRequest challengeRequest = new()
                {
                    DeviceId = identity.DeviceId
                };

                using HttpResponseMessage challengeHttpResponse = await client.PostAsJsonAsync($"{baseUrl}/challenge", challengeRequest, cancellationToken);

                challengeHttpResponse.EnsureSuccessStatusCode();

                PeerChallengeResponse challenge = await challengeHttpResponse.Content.ReadFromJsonAsync<PeerChallengeResponse>(cancellationToken: cancellationToken) ??
                                                  throw new CryptographicException("The remote Host returned no authentication challenge.");

                if (challenge.ChallengeId == Guid.Empty || challenge.ServerDeviceId != remoteDeviceId || challenge.ExpiresUtc <= DateTime.UtcNow || challenge.ExpiresUtc > DateTime.UtcNow.AddMinutes(3))
                {
                    throw new CryptographicException("The remote authentication challenge is invalid.");
                }

                if (observedCertificateHash is null)
                {
                    throw new CryptographicException("No TLS certificate was observed during authentication.");
                }

                byte[] claimedCertificateHash;

                try
                {
                    claimedCertificateHash = Convert.FromHexString(challenge.ServerCertificateHash);
                }
                catch (FormatException exception)
                {
                    throw new CryptographicException("The server certificate hash is malformed.", exception);
                }

                if (claimedCertificateHash.Length != 32 || !CryptographicOperations.FixedTimeEquals(claimedCertificateHash, observedCertificateHash))
                {
                    throw new CryptographicException("The signed server certificate identity does not match the HTTPS connection.");
                }

                byte[] nonce;
                byte[] serverSignature;

                try
                {
                    nonce = Convert.FromBase64String(challenge.Nonce);
                    serverSignature = Convert.FromBase64String(challenge.ServerSignature);
                }
                catch (FormatException exception)
                {
                    throw new CryptographicException("The remote authentication proof is malformed.", exception);
                }

                if (nonce.Length != 32)
                {
                    throw new CryptographicException("The remote authentication nonce is invalid.");
                }

                byte[] payload = PeerAuthenticationService.BuildChallengePayload(challenge.ChallengeId, identity.DeviceId, remoteDeviceId, nonce, challenge.ExpiresUtc, observedCertificateHash);

                bool serverVerified = verifier.VerifyData(payload, serverSignature, HashAlgorithmName.SHA256);

                if (!serverVerified)
                {
                    throw new CryptographicException("The remote Host failed trusted identity verification.");
                }

                byte[] clientSignature = deviceSecurity.SignData(payload);

                PeerAuthenticationRequest authenticationRequest = new()
                {
                    ChallengeId = challenge.ChallengeId,
                    DeviceId = identity.DeviceId,
                    Signature = Convert.ToBase64String(clientSignature)
                };

                using HttpResponseMessage authenticationHttpResponse = await client.PostAsJsonAsync($"{baseUrl}/complete", authenticationRequest, cancellationToken);

                authenticationHttpResponse.EnsureSuccessStatusCode();

                PeerAuthenticationResponse session = await authenticationHttpResponse.Content.ReadFromJsonAsync<PeerAuthenticationResponse>(cancellationToken: cancellationToken) ??
                                                     throw new CryptographicException("The remote Host returned no authenticated session.");

                if (session.SessionId == Guid.Empty || session.ServerDeviceId != remoteDeviceId || string.IsNullOrWhiteSpace(session.AccessToken) || session.ExpiresUtc <= DateTime.UtcNow ||
                    session.ExpiresUtc > DateTime.UtcNow.AddMinutes(31))
                {
                    throw new CryptographicException("The remote Host returned an invalid authenticated session.");
                }

                byte[] tokenBytes;

                try
                {
                    tokenBytes = Convert.FromBase64String(session.AccessToken);
                }
                catch (FormatException exception)
                {
                    throw new CryptographicException("The session token is malformed.", exception);
                }

                if (tokenBytes.Length != 32)
                {
                    throw new CryptographicException("The session token has an invalid length.");
                }

                return new AuthenticatedPeerConnection(client, new Uri($"https://{host}:5019"), remoteDeviceId, session);
            }
            catch
            {
                client.Dispose();
                
                throw;
            }
        }
    }
}