using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using RemoteLink.Core.Models;

namespace RemoteLink.Host.Services
{
    public sealed class LanDiscoveryService(DeviceIdentityService identity, ILogger<LanDiscoveryService> logger) : BackgroundService
    {
        private const int DiscoveryPort = 5018;

        private static readonly TimeSpan AnnouncementInterval = TimeSpan.FromSeconds(3);
        
        private static readonly TimeSpan DeviceTimeout = TimeSpan.FromSeconds(45);
        
        private readonly ConcurrentDictionary<Guid, DiscoveredDevice> _devices = new();

        public IReadOnlyList<DiscoveredDevice> GetDevices()
        {
            RemoveExpiredDevices();
            
            return _devices.Values.OrderBy(device => device.Name).Select(device => new DiscoveredDevice
            {
                Id = device.Id,
                Name = device.Name,
                IpAddress = device.IpAddress,
                DiscoveryPort = device.DiscoveryPort,
                LastSeenUtc = device.LastSeenUtc,
                IsOnline = device.IsOnline
            }).ToList();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using UdpClient receiver = new(AddressFamily.InterNetwork);
            
            receiver.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            
            receiver.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            
            using UdpClient sender = new(AddressFamily.InterNetwork);
            
            sender.EnableBroadcast = true;
            
            logger.LogInformation("LAN discovery listening on port {Port}", DiscoveryPort);

            Task receiveTask = ReceiveLoopAsync(receiver, stoppingToken);
            Task broadcastTask = BroadcastLoopAsync(sender, stoppingToken);
            
            await Task.WhenAny(receiveTask, broadcastTask);
        }

        private async Task ReceiveLoopAsync(UdpClient receiver, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    UdpReceiveResult result = await receiver.ReceiveAsync(cancellationToken);

                    if (result.Buffer.Length > 4096)
                    {
                        continue;
                    }

                    string json = Encoding.UTF8.GetString(result.Buffer);

                    DiscoveryAnnouncement? announcement = JsonSerializer.Deserialize<DiscoveryAnnouncement>(json);

                    if (announcement is null || announcement.Protocol != "RemoteLink.Discovery" || announcement.ProtocolVersion != 1 || announcement.DeviceId == Guid.Empty || announcement.DeviceId == identity.DeviceId ||
                        announcement.DiscoveryPort != DiscoveryPort || string.IsNullOrWhiteSpace(announcement.DeviceName) || announcement.DeviceName.Length > 128)
                    {
                        continue;
                    }

                    DateTime now = DateTime.UtcNow;

                    _devices.AddOrUpdate(announcement.DeviceId, _ => new DiscoveredDevice
                    {
                        Id = announcement.DeviceId,
                        Name = announcement.DeviceName,
                        IpAddress = result.RemoteEndPoint.Address.ToString(),
                        DiscoveryPort = announcement.DiscoveryPort,
                        LastSeenUtc = now,
                        IsOnline = true
                    }, (_, existing) => new DiscoveredDevice
                    {
                        Id = announcement.DeviceId,
                        Name = announcement.DeviceName,
                        IpAddress = result.RemoteEndPoint.Address.ToString(),
                        DiscoveryPort = announcement.DiscoveryPort,
                        LastSeenUtc = now,
                        IsOnline = true
                    });
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (JsonException)
                {
                    // Malformed JSON
                }
                catch (SocketException exception)
                {
                    logger.LogWarning(exception, "LAN discovery receive error");
                }
            }
        }

        private async Task BroadcastLoopAsync(UdpClient sender, CancellationToken cancellationToken)
        {
            DiscoveryAnnouncement announcement = new()
            {
                DeviceId = identity.DeviceId,
                DeviceName = identity.DeviceName,
                DiscoveryPort = DiscoveryPort
            };
            
            byte[] payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(announcement));
            
            IPEndPoint destination = new(IPAddress.Broadcast, DiscoveryPort);
            
            using PeriodicTimer timer = new(AnnouncementInterval);

            do
            {
                try
                {
                    await sender.SendAsync(payload, destination, cancellationToken);

                    RemoveExpiredDevices();
                }
                catch (SocketException exception)
                {
                    logger.LogWarning(exception, "LAN discovery broadcast failed");
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }

        private void RemoveExpiredDevices()
        {
            DateTime cutoff = DateTime.UtcNow - DeviceTimeout;
            
            foreach (KeyValuePair<Guid, DiscoveredDevice> entry in _devices)
            {
                if (entry.Value.LastSeenUtc < cutoff)
                {
                    _devices.TryRemove(entry.Key, out _);
                }
            }
        }
    }
}