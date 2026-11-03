using System.Text.Json;

namespace RemoteLink.Host.Services
{
    public sealed class DeviceIdentityService
    {
        public Guid DeviceId { get; }
        
        public string DeviceName => Environment.MachineName;
        
        public DeviceIdentityService()
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteLink", "Data");
            
            Directory.CreateDirectory(directory);

            string filePath = Path.Combine(directory, "device.json");
            
            Guid? existingId = null;

            if (File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);

                    DeviceIdentity? identity = JsonSerializer.Deserialize<DeviceIdentity>(json);

                    if (identity is { Id: var id } && id != Guid.Empty)
                    {
                        existingId = id;
                    }
                }
                catch (JsonException)
                {
                    // Corrupted Identity File
                }
            }
            
            DeviceId = existingId ?? Guid.NewGuid();

            if (existingId is null)
            {
                string json = JsonSerializer.Serialize(new DeviceIdentity
                {
                    Id = DeviceId
                }, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                File.WriteAllText(filePath, json);
            }
        }
        
        private sealed class DeviceIdentity
        {
            public Guid Id { get; set; }
        }
    }
}