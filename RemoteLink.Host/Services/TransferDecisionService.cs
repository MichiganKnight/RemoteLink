using RemoteLink.Core.Models.Transfers;
using RemoteLink.Core.Models.Transfers.Requests;
using RemoteLink.Core.Models.Transfers.Responses;

namespace RemoteLink.Host.Services
{
    public sealed class TransferDecisionService(TransferService transfers, DeviceIdentityService identity, ChatPeerAddressResolver addresses, TrustedPeerClientService peerClient)
    {
        public async Task<TransferJobResponse> DecideAndNotifyAsync(Guid transferId, bool accepted, CancellationToken cancellationToken = default)
        {
            TransferJobResponse transfer = await transfers.DecideAsync(transferId, accepted, cancellationToken);

            if (transfer.Direction != TransferDirection.Incoming)
            {
                throw new InvalidOperationException("Only incoming transfers can be approved locally.");
            }

            string address = addresses.Resolve(transfer.RemoteDeviceId) ?? throw new InvalidOperationException("Sender device is not currently discovered on LAN.");
            
            TransferDecisionNotification notification = new()
            {
                TransferId = transfer.Id,
                SenderDeviceId = transfer.RemoteDeviceId,
                RecipientDeviceId = identity.DeviceId,
                Accepted = accepted
            };

            using AuthenticatedPeerConnection connection = await peerClient.ConnectAsync(transfer.RemoteDeviceId, address, cancellationToken);

            return await connection.SendTransferDecisionAsync(notification, cancellationToken);
        }
    }
}