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
    }
}