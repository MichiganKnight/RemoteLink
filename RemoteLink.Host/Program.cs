using System.Net;
using Microsoft.EntityFrameworkCore;
using RemoteLink.Host.Data;
using RemoteLink.Host.Hubs;
using RemoteLink.Host.Services;

namespace RemoteLink.Host
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();

            string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RemoteLink", "Data");
            
            Directory.CreateDirectory(dataDirectory);
            
            string databasePath = Path.Combine(dataDirectory, "remotelink.db");
            
            builder.Services.AddDbContext<RemoteLinkDbContext>(options =>
            {
                options.UseSqlite($"Data Source={databasePath}");
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
            builder.Services.AddScoped<ChatDeliveryService>();

            builder.Services.AddSingleton<ChatPeerAddressResolver>();
            builder.Services.AddHostedService<ChatDeliveryWorker>();
            
            builder.Services.AddSingleton<ChatNotificationService>();
            
            builder.Services.AddScoped<TransferService>();
            builder.Services.AddScoped<TransferOfferService>();

            builder.Services.AddHostedService(provider => provider.GetRequiredService<LanDiscoveryService>());
            
            builder.Services.AddSignalR();
            
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
            
            app.MapHub<LocalChatHub>("/hubs/local-chat");

            using (IServiceScope scope = app.Services.CreateScope())
            {
                RemoteLinkDbContext database = scope.ServiceProvider.GetRequiredService<RemoteLinkDbContext>();
                
                database.Database.Migrate();
            }

            app.Use(async (context, next) =>
            {
                if (context.Connection.LocalPort == 5019)
                {
                    bool allowed = context.Request.Method == HttpMethods.Get && context.Request.Path.Equals("/api/lan/identity", StringComparison.OrdinalIgnoreCase);
                    
                    allowed |= context.Request.Method == HttpMethods.Get && context.Request.Path.StartsWithSegments("/api/lan/pairing/decision", StringComparison.OrdinalIgnoreCase);
                    allowed |= context.Request.Method == HttpMethods.Post && context.Request.Path.Equals("/api/lan/pairing/propose", StringComparison.OrdinalIgnoreCase);
                    allowed |= context.Request.Method == HttpMethods.Post && context.Request.Path.StartsWithSegments("/api/lan/pairing/handshake", StringComparison.OrdinalIgnoreCase);
                    allowed |= context.Request.Method == HttpMethods.Post && context.Request.Path.StartsWithSegments("/api/lan/pairing/confirm-initiator", StringComparison.OrdinalIgnoreCase);
                    allowed |= context.Request.Method == HttpMethods.Post && context.Request.Path.StartsWithSegments("/api/lan/pairing/decision", StringComparison.OrdinalIgnoreCase);
                    allowed |= context.Request.Method == HttpMethods.Post && context.Request.Path.StartsWithSegments("/api/lan/pairing/finalize", StringComparison.OrdinalIgnoreCase);
                    
                    allowed |= context.Request.Method == HttpMethods.Post && context.Request.Path.Equals("/api/lan/auth/challenge", StringComparison.OrdinalIgnoreCase);
                    allowed |= context.Request.Method == HttpMethods.Post && context.Request.Path.Equals("/api/lan/auth/complete", StringComparison.OrdinalIgnoreCase);
                    
                    allowed |= context.Request.Method == HttpMethods.Post && context.Request.Path.Equals("/api/lan/chat/messages", StringComparison.OrdinalIgnoreCase);
                    
                    allowed |= context.Request.Method == HttpMethods.Post && context.Request.Path.Equals("/api/lan/transfers/offers", StringComparison.OrdinalIgnoreCase);

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