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

            // Configure HybridCache with all capabilities in one unified call
            var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";

            builder.Services.AddHybridCacheWithCapabilities(
                redisConnection,
                cacheOptions =>
                {
                    cacheOptions.KeyPrefix = "hybridcache";
                    cacheOptions.DefaultExpiration = TimeSpan.FromMinutes(10);
                    cacheOptions.DefaultLocalExpiration = TimeSpan.FromMinutes(2);
                },
                capabilities =>
                {
                    // Enable cache notifications for multi-instance synchronization
                    capabilities.EnableNotifications = true;
                    
                    // Optionally enable cache warming (currently disabled)
                    capabilities.EnableCacheWarming = false;
                    
                    // Optionally enable clustering (currently disabled)
                    capabilities.EnableClustering = false;

                    // Configure notifications
                    capabilities.NotificationOptions = options =>
                    {
                        options.EnableNotifications = true;
                        options.AutoInvalidateL1OnNotification = true;
                        options.NotificationChannel = "hybridcache:notifications";
                        options.IgnoreSelfNotifications = true;
                        options.IncludeKeyPatterns = new[] { "user:*", "product:*", "session:*" };
                    };

                    // Uncomment to enable cache warming
                    /*
                    capabilities.CacheWarmingOptions = options =>
                    {
                        options.EnableAutoWarming = true;
                        options.WarmingInterval = TimeSpan.FromMinutes(5);
                        options.IncludePatterns = new[] { "user:*", "product:*" };
                        options.MaxKeysPerWarming = 1000;
                    };
                    */

                    // Uncomment to enable clustering
                    /*
                    capabilities.ClusterOptions = options =>
                    {
                        options.IsClusterMode = true;
                        options.UseHashTags = true;
                        options.ValidateHashSlots = true;
                    };
                    */
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
