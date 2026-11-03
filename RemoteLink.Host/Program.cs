using System.Net;
using System.Security.Cryptography.X509Certificates;
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
            builder.Services.AddSingleton<DeviceSecurityService>();
            builder.Services.AddSingleton<TlsCertificateService>();
            builder.Services.AddSingleton<LanDiscoveryService>();

            builder.Services.AddHostedService(provider => provider.GetRequiredService<LanDiscoveryService>());
            
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, 5017);

                options.ListenAnyIP(5019, listenOptions =>
                {
                    TlsCertificateService tls = listenOptions.ApplicationServices.GetRequiredService<TlsCertificateService>();

                    listenOptions.UseHttps(tls.GetCertificate());
                });
            });
            
            WebApplication app = builder.Build();

            app.Use(async (context, next) =>
            {
                if (context.Connection.LocalPort == 5019 && !context.Request.Path.Equals("/api/lan/identity", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 404;

                    return;
                }

                await next();
            });

            app.MapControllers();
            
            app.Run();
        }
    }
}