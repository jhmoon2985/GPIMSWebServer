// Program.cs - 메모리 및 성능 최적화 버전
using GPIMSWebServer.Hubs;
using GPIMSWebServer.Services;
using GPIMSWebServer.BackgroundServices;
using GPIMSWebServer.Data;
using GPIMSWebServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Text.Json.Serialization;
using System.Runtime;

var builder = WebApplication.CreateBuilder(args);

// 메모리 최적화를 위한 GC 설정
GCSettings.LatencyMode = GCLatencyMode.Batch; // 처리량 우선
GC.TryStartNoGCRegion(50 * 1024 * 1024); // 50MB 정도는 GC 없이 할당

// 로깅 최적화
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.FormatterName = "simple";
});

// 개발 환경에서만 디버그 로깅
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddDebug();
    builder.Logging.SetMinimumLevel(LogLevel.Debug);
}
else
{
    builder.Logging.SetMinimumLevel(LogLevel.Information);
}

// Entity Framework - SQLite 최적화 설정
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"), sqliteOptions =>
    {
        sqliteOptions.CommandTimeout(30); // 30초 타임아웃
    });
    
    // 성능 최적화 설정
    options.EnableSensitiveDataLogging(builder.Environment.IsDevelopment());
    options.EnableDetailedErrors(builder.Environment.IsDevelopment());
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking); // 기본적으로 추적 비활성화
}, ServiceLifetime.Scoped); // Scoped로 명시적 설정

// Authentication 최적화
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name = "GPIMSAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax; // CSRF 보호
        options.Cookie.MaxAge = TimeSpan.FromHours(8);
    });

// Authorization Policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => 
        policy.RequireClaim("Role", "Admin"));
    options.AddPolicy("MaintenanceOrAdmin", policy => 
        policy.RequireClaim("Role", "Admin", "Maintenance"));
    options.AddPolicy("AuthenticatedUser", policy => 
        policy.RequireAuthenticatedUser());
});

// MVC with optimized JSON settings
builder.Services.AddControllersWithViews(options =>
{
    // 압축 설정
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.ResponseCacheAttribute
    {
        Duration = 0,
        Location = Microsoft.AspNetCore.Mvc.ResponseCacheLocation.None,
        NoStore = true
    });
})
.AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.JsonSerializerOptions.WriteIndented = false; // 프로덕션에서는 압축
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull; // null 값 무시
});

// SignalR 최적화 설정
builder.Services.AddSignalR(hubOptions =>
{
    hubOptions.EnableDetailedErrors = builder.Environment.IsDevelopment();
    hubOptions.KeepAliveInterval = TimeSpan.FromSeconds(30); // 15초에서 30초로 증가
    hubOptions.ClientTimeoutInterval = TimeSpan.FromSeconds(60); // 30초에서 60초로 증가
    hubOptions.HandshakeTimeout = TimeSpan.FromSeconds(15);
    hubOptions.MaximumReceiveMessageSize = 32 * 1024; // 32KB 제한
    hubOptions.StreamBufferCapacity = 10; // 스트림 버퍼 크기 제한
    hubOptions.MaximumParallelInvocationsPerClient = 1; // 클라이언트당 동시 호출 제한
})
.AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
    options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.PayloadSerializerOptions.WriteIndented = false; // 압축
    options.PayloadSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// Custom services - Singleton으로 성능 최적화
builder.Services.AddSingleton<IDataService, DataService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserActivityService, UserActivityService>();
builder.Services.AddScoped<IDeviceUpdateService, DeviceUpdateService>();

// Background services
builder.Services.AddHostedService<DataUpdateService>();

// 메모리 캐싱 추가
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 100; // 100개 항목 제한
    options.CompactionPercentage = 0.25; // 25% 압축
});

// Response compression
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});

// CORS 최적화
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("Content-Length"); // 필요한 헤더만 노출
    });
});

// Health checks 추가
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>()
    .AddCheck("memory", () =>
    {
        var memoryUsed = GC.GetTotalMemory(false);
        var memoryLimit = 500 * 1024 * 1024; // 500MB 제한
        
        return memoryUsed < memoryLimit 
            ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy($"Memory usage: {memoryUsed / 1024 / 1024} MB")
            : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy($"High memory usage: {memoryUsed / 1024 / 1024} MB");
    });

var app = builder.Build();

// Response compression 활성화
app.UseResponseCompression();

// Database initialization with optimizations
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
    var activityService = scope.ServiceProvider.GetRequiredService<IUserActivityService>();
    
    try
    {
        // Database migration
        await context.Database.MigrateAsync();
        app.Logger.LogInformation("Database migration completed successfully");

        // Admin user setup (코드 간소화)
        await SetupAdminUserAsync(context, userService, activityService, app.Logger);

        // Background cleanup task (메모리 최적화)
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromHours(6)); // 6시간마다 실행
                    
                    using var cleanupScope = app.Services.CreateScope();
                    var cleanupActivityService = cleanupScope.ServiceProvider.GetRequiredService<IUserActivityService>();
                    await cleanupActivityService.CleanupOldActivitiesAsync(30); // 30일 이상 된 기록 삭제
                    
                    // SignalR 연결 정리
                    DeviceDataHub.CleanupInactiveConnections(TimeSpan.FromHours(2));
                    
                    // 메모리 정리
                    if (GC.GetTotalMemory(false) > 300 * 1024 * 1024) // 300MB 이상
                    {
                        GC.Collect(2, GCCollectionMode.Optimized);
                        GC.WaitForPendingFinalizers();
                        app.Logger.LogInformation("Performed scheduled memory cleanup");
                    }
                }
                catch (Exception ex)
                {
                    app.Logger.LogError(ex, "Error during scheduled cleanup");
                }
            }
        });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error during database initialization");
        
        if (app.Environment.IsDevelopment())
        {
            throw;
        }
    }
}

// Configure pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // 정적 파일 캐싱 최적화
        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=31536000"); // 1년
    }
});

app.UseRouting();
app.UseCors("AllowAll");

// Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// Health checks endpoint
app.MapHealthChecks("/health");

// Routes
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .RequireAuthorization("AuthenticatedUser");

// SignalR Hub
app.MapHub<DeviceDataHub>("/deviceHub")
    .RequireAuthorization("AuthenticatedUser");

// 메모리 모니터링 엔드포인트 (개발 환경에서만)
if (app.Environment.IsDevelopment())
{
    app.MapGet("/debug/memory", () =>
    {
        var memoryInfo = new
        {
            TotalMemory = GC.GetTotalMemory(false) / 1024 / 1024, // MB
            Generation0Collections = GC.CollectionCount(0),
            Generation1Collections = GC.CollectionCount(1),
            Generation2Collections = GC.CollectionCount(2),
            SignalRStats = DeviceDataHub.GetConnectionStats()
        };
        return Results.Json(memoryInfo);
    });
}

app.Run();

// Helper method for admin user setup
static async Task SetupAdminUserAsync(ApplicationDbContext context, IUserService userService, 
    IUserActivityService activityService, ILogger logger)
{
    var adminUser = await context.Users.FirstOrDefaultAsync(u => u.Username == "admin");
    
    if (adminUser != null)
    {
        // Password verification optimization
        bool passwordWorks = userService.VerifyPassword("admin123", adminUser.PasswordHash);
        
        if (!passwordWorks)
        {
            logger.LogWarning("Admin password hash is incorrect, fixing...");
            adminUser.PasswordHash = userService.HashPassword("admin123");
            adminUser.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
            logger.LogInformation("Admin password hash has been corrected");
        }
    }
    else
    {
        logger.LogInformation("Creating default admin user...");
        
        var newAdmin = new User
        {
            Username = "admin",
            PasswordHash = userService.HashPassword("admin123"),
            Name = "System Administrator",
            Department = "IT",
            Role = UserRole.Admin,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        context.Users.Add(newAdmin);
        await context.SaveChangesAsync();
        
        await activityService.LogActivityAsync(
            newAdmin.Id, 
            newAdmin.Username, 
            ActivityType.CreateUser, 
            "System administrator account created", 
            "127.0.0.1", 
            "System"
        );
        
        logger.LogInformation("Default admin user created successfully");
    }
}