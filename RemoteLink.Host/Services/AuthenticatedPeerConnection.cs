using System.Net.Http.Headers;
using System.Security.Cryptography;
using RemoteLink.Core.Models.Authentication;
using RemoteLink.Core.Models.Chat.Requests;
using RemoteLink.Core.Models.Chat.Responses;
using RemoteLink.Core.Models.Transfers.Requests;
using RemoteLink.Core.Models.Transfers.Responses;

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

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);

                throw new HttpRequestException($"Remote chat delivery failed. HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). Response: {errorBody}");
            }

            ChatDeliveryAcknowledgment acknowledgment = await response.Content.ReadFromJsonAsync<ChatDeliveryAcknowledgment>(cancellationToken: cancellationToken) ?? throw new CryptographicException("Missing message acknowledgment.");

            if (acknowledgment.MessageId != message.MessageId || acknowledgment.ReceivingDeviceId != _remoteDeviceId || acknowledgment.DeliveredUtc == default)
            {
                throw new CryptographicException("Invalid message acknowledgment.");
            }

            return acknowledgment;
        }
        
        public async Task<TransferJobResponse> SendTransferOfferAsync(IncomingTransferRequest transfer, CancellationToken cancellationToken = default)
        {
            if (_session.ExpiresUtc <= DateTime.UtcNow)
            {
                throw new InvalidOperationException("Peer session has expired.");
            }

            Uri endpoint = new(_baseUri, "/api/lan/transfers/offers");

            using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(transfer)
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);

            using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync(cancellationToken);

                throw new HttpRequestException($"Transfer offer failed ({(int)response.StatusCode}): {error}");
            }

            TransferJobResponse? result = await response.Content.ReadFromJsonAsync<TransferJobResponse>(cancellationToken: cancellationToken);

            return result ?? throw new InvalidOperationException("Remote device returned an empty transfer response.");
        }
        
        public async Task<TransferJobResponse> SendTransferDecisionAsync(TransferDecisionNotification notification, CancellationToken cancellationToken = default)
        {
            if (_session.ExpiresUtc <= DateTime.UtcNow)
            {
                throw new InvalidOperationException("Peer session has expired.");
            }

            Uri endpoint = new(_baseUri, $"/api/lan/transfers/{notification.TransferId}/decision");

            using HttpRequestMessage request = new(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(notification)
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);

            using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync(cancellationToken);

                throw new HttpRequestException($"Transfer decision notification failed ({(int)response.StatusCode}): {error}");
            }

            TransferJobResponse? result = await response.Content.ReadFromJsonAsync<TransferJobResponse>(cancellationToken: cancellationToken);

            return result ?? throw new InvalidOperationException("Remote device returned an empty decision response.");
        }
        
        public async Task<TransferFileAcknowledgment> SendTransferFileAsync(Guid transferId, Guid itemId, string sourcePath, string expectedSha256, Action<long>? onProgress = null, CancellationToken cancellationToken = default)
        {
            if (_session.ExpiresUtc <= DateTime.UtcNow)
            {
                throw new InvalidOperationException("Peer session has expired.");
            }

            await using FileStream file = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1024 * 1024, useAsync: true);

            long expectedLength = file.Length;

            using TransferProgressStream progressStream = new(file, bytes => onProgress?.Invoke(bytes));

            Uri endpoint = new(_baseUri, $"/api/lan/transfers/{transferId}/items/{itemId}/content");

            using HttpRequestMessage request = new(HttpMethod.Put, endpoint)
            {
                Content = new StreamContent(progressStream)
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.AccessToken);

            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            request.Content.Headers.ContentLength = expectedLength;
            
            if (expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit))
            {
                throw new ArgumentException("Expected SHA-256 must contain 64 hexadecimal characters.", nameof(expectedSha256));
            }

            request.Headers.Add("X-File-Sha256", expectedSha256);

            using HttpResponseMessage response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync(cancellationToken);

                throw new HttpRequestException($"File transfer failed ({(int)response.StatusCode}): {error}");
            }

            TransferFileAcknowledgment acknowledgment = await response.Content.ReadFromJsonAsync<TransferFileAcknowledgment>(cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Receiver returned an empty transfer acknowledgment.");

            if (acknowledgment.TransferId != transferId || acknowledgment.ItemId != itemId || acknowledgment.ReceivedBytes != expectedLength || string.IsNullOrWhiteSpace(acknowledgment.Sha256))
            {
                throw new InvalidOperationException("Receiver returned an invalid transfer acknowledgment.");
            }

            return acknowledgment;
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }
}