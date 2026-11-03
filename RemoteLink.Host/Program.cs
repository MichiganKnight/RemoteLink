using RemoteLink.Host.Services;

namespace RemoteLink.Host
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();
            
            builder.Services.AddSingleton<DeviceIdentityService>();
            builder.Services.AddSingleton<LanDiscoveryService>();

            builder.Services.AddHostedService(provider => provider.GetRequiredService<LanDiscoveryService>());
            
            WebApplication app = builder.Build();

            app.MapControllers();
            
            app.Run();
        }
    }
}