using System.Collections.Concurrent;

namespace RemoteLink.Host.Services
{
    public sealed class ChatPeerAddressResolver
    {
        private readonly ConcurrentDictionary<Guid, string> _addresses = new();

        public void Update(Guid deviceId, string address)
        {
            if (deviceId == Guid.Empty || string.IsNullOrWhiteSpace(address))
            {
                return;
            }

            _addresses[deviceId] = address.Trim();
        }

        public string? Resolve(Guid deviceId)
        {
            return _addresses.TryGetValue(deviceId, out string? address) ? address : null;
        }

        public void Remove(Guid deviceId)
        {
            _addresses.TryRemove(deviceId, out _);
        }
    }
}