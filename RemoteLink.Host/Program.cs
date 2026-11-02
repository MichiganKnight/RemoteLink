namespace RemoteLink.Host
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            builder.Services.AddControllers();
            
            WebApplication app = builder.Build();

            app.MapControllers();
            
            app.Run();
        }
    }
}