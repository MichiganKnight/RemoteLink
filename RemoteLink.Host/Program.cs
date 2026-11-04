using System.Net;
using Microsoft.EntityFrameworkCore;
using RemoteLink.Host.Data;
using RemoteLink.Host.Services;

namespace RemoteLink.Host
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();

            builder.Services.AddDbContext<RemoteLinkDbContext>(options =>
            {
                string connectionString = builder.Configuration.GetConnectionString("RemoteLink") ?? throw new InvalidOperationException("RemoteLink database connection string is missing.");
                
                options.UseSqlServer(connectionString);
            });
            
            builder.Services.AddSingleton<DeviceIdentityService>();
            builder.Services.AddSingleton<DeviceSecurityService>();
            builder.Services.AddSingleton<TlsCertificateService>();
            builder.Services.AddSingleton<LanDiscoveryService>();
            builder.Services.AddSingleton<PairingSessionService>();
            builder.Services.AddSingleton<PairingSecurityService>();
            builder.Services.AddSingleton<PairingClientService>();
            builder.Services.AddSingleton<PairingCryptoSessionService>();
            builder.Services.AddSingleton<PairingApprovalService>();
            builder.Services.AddSingleton<PairingDecisionSecurityService>();
            builder.Services.AddSingleton<PairingPeerContextService>();
            builder.Services.AddSingleton<TrustedDeviceService>();
            builder.Services.AddSingleton<PeerAuthenticationService>();
            builder.Services.AddSingleton<TrustedPeerClientService>();

            builder.Services.AddScoped<ChatService>();

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
                if (context.Connection.LocalPort == 5019)
                {
                    bool allowed = context.Request.Method == HttpMethods.Get && context.Request.Path.Equals("/api/lan/identity", StringComparison.OrdinalIgnoreCase);
                    
                    allowed |= context.Request.Method == HttpMethods.Post && context.Request.Path.Equals("/api/lan/pairing/propose", StringComparison.OrdinalIgnoreCase);
                    allowed |= context.Request.Method == HttpMethods.Post && context.Request.Path.StartsWithSegments("/api/lan/pairing/handshake", StringComparison.OrdinalIgnoreCase);
                    allowed |= context.Request.Method == HttpMethods.Post && context.Request.Path.StartsWithSegments("/api/lan/pairing/confirm-initiator", StringComparison.OrdinalIgnoreCase);
                    allowed |= context.Request.Method == HttpMethods.Post && context.Request.Path.StartsWithSegments("/api/lan/pairing/decision", StringComparison.OrdinalIgnoreCase);
                    allowed |= context.Request.Method == HttpMethods.Get && context.Request.Path.StartsWithSegments("/api/lan/pairing/decision", StringComparison.OrdinalIgnoreCase);
                    allowed |= context.Request.Method == HttpMethods.Post && context.Request.Path.StartsWithSegments("/api/lan/pairing/finalize", StringComparison.OrdinalIgnoreCase);
                    
                    allowed |= context.Request.Method == HttpMethods.Post && context.Request.Path.Equals("/api/lan/auth/challenge", StringComparison.OrdinalIgnoreCase);
                    allowed |= context.Request.Method == HttpMethods.Post && context.Request.Path.Equals("/api/lan/auth/complete", StringComparison.OrdinalIgnoreCase);

                    if (!allowed)
                    {
                        context.Response.StatusCode = 404;

                        return;    
                    }
                }

                await next();
            });

            app.MapControllers();
            
            app.Run();
        }
    }
}