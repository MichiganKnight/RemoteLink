using System.Net;
using System.Security.Cryptography;
using RemoteLink.Core.Models;
using RemoteLink.Core.Models.Pairing;

namespace RemoteLink.Host.Services
{
    public sealed class PairingClientService(DeviceIdentityService identity, DeviceSecurityService security, PairingSecurityService pairingSecurity)
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

            using PairingKeyExchange exchange = pairingSecurity.CreateKeyExchange();

            DeviceSecurityIdentity publicIdentity = security.GetPublicIdentity();

            PairingProposal proposal = new()
            {
                RequestId = Guid.NewGuid(),
                DeviceId = identity.DeviceId,
                DeviceName = identity.DeviceName,
                IdentityPublicKey = publicIdentity.PublicKey,
                EphemeralPublicKey = Convert.ToBase64String(exchange.ExportPublicKey())
            };

            using HttpClientHandler handler = new();

            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null)
                {
                    return false;
                }

                byte[] actual = certificate.GetCertHash();

                byte[] expected = Convert.FromHexString(expectedThumbprint);

                return CryptographicOperations.FixedTimeEquals(actual, expected);
            };

            using HttpClient client = new(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            
            Uri endpoint = new($"https://{address}:5019/api/lan/pairing/propose");
            
            using HttpResponseMessage response = await client.PostAsJsonAsync(endpoint, proposal, cancellationToken);
            
            response.EnsureSuccessStatusCode();
            
            PairingProposalResponse? result = await response.Content.ReadFromJsonAsync<PairingProposalResponse>(cancellationToken);

            if (result is null || result.RequestId != proposal.RequestId || !result.AcceptedForReview)
            {
                throw new InvalidOperationException("The remote Host returned an invalid response.");
            }
            
            return result;
        }
    }
}