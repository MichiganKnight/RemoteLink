using System.Net.Http.Headers;
using System.Security.Cryptography;
using RemoteLink.Core.Models.Authentication;
using RemoteLink.Core.Models.Chat.Requests;
using RemoteLink.Core.Models.Chat.Responses;

namespace RemoteLink.Host.Services
{
    public sealed class AuthenticatedPeerConnection : IDisposable
    {
        internal PeerAuthenticationResponse GetSessionInfo()
        {
            return _session;
        }
        
        private readonly HttpClient _client;
        private readonly Uri _baseUri;
        private readonly Guid _remoteDeviceId;
        private readonly PeerAuthenticationResponse _session;

        internal AuthenticatedPeerConnection(HttpClient client, Uri baseUri, Guid remoteDeviceId, PeerAuthenticationResponse session)
        {
            _client = client;
            _baseUri = baseUri;
            _remoteDeviceId = remoteDeviceId;
            _session = session;
        }

        public async Task<ChatDeliveryAcknowledgment> SendMessageAsync(ReceiveChatMessageRequest message, CancellationToken cancellationToken = default)
        {
            if (_session.ExpiresUtc <= DateTime.UtcNow)
            {
                throw new InvalidOperationException("Peer session has expired.");
            }

            if (message.RecipientDeviceId != _remoteDeviceId)
            {
                throw new ArgumentException("Message recipient does not match connected peer.");
            }

            Uri endpoint = new(_baseUri, "/api/lan/chat/messages");

            using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(message)
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);

            using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken);

            response.EnsureSuccessStatusCode();

            ChatDeliveryAcknowledgment acknowledgment = await response.Content.ReadFromJsonAsync<ChatDeliveryAcknowledgment>(cancellationToken: cancellationToken) ?? throw new CryptographicException("Missing message acknowledgment.");

            if (acknowledgment.MessageId != message.MessageId || acknowledgment.ReceivingDeviceId != _remoteDeviceId || acknowledgment.DeliveredUtc == default)
            {
                throw new CryptographicException("Invalid message acknowledgment.");
            }

            return acknowledgment;
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}