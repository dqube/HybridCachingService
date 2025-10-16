using HybridCache.DependencyInjection;

namespace HybridCachingService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllers();
            builder.Services.AddAuthorization();

            // Configure HybridCache with Redis, Lua scripts, Cluster support, and Notifications
            var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";

            builder.Services.AddHybridCacheWithRedis(redisConnection, options =>
            {
                options.DefaultExpiration = TimeSpan.FromMinutes(10);
                options.DefaultLocalExpiration = TimeSpan.FromMinutes(2);
                options.KeyPrefix = "hybridcache";
            });

            // Add cache notifications for multi-instance synchronization
            builder.Services.AddCacheNotifications(options =>
            {
                options.EnableNotifications = true;
                options.AutoInvalidateL1OnNotification = true;
                options.NotificationChannel = "hybridcache:notifications";
                options.IgnoreSelfNotifications = true;
                options.IncludeKeyPatterns = new[] { "user:*", "product:*", "session:*" };
            });

            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = "HybridCache API",
                    Version = "v1",
                    Description = "Comprehensive demo of HybridCache features including L1/L2 caching, Lua scripts, notifications, and cluster support"
                });
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}
