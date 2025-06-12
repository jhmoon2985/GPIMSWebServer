using GPIMSWebServer.Hubs;
using GPIMSWebServer.Services;
using GPIMSWebServer.BackgroundServices;
using GPIMSWebServer.Data;
using GPIMSWebServer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add Entity Framework
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add Authentication
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
    });

// Add Authorization Policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => 
        policy.RequireClaim("Role", "Admin"));
    options.AddPolicy("MaintenanceOrAdmin", policy => 
        policy.RequireClaim("Role", "Admin", "Maintenance"));
    options.AddPolicy("AuthenticatedUser", policy => 
        policy.RequireAuthenticatedUser());
});

// Add services to the container
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.WriteIndented = true;
    });

// Add SignalR with JSON configuration
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
})
.AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
    options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.PayloadSerializerOptions.WriteIndented = true;
});

// Add custom services
builder.Services.AddSingleton<IDataService, DataService>();
builder.Services.AddScoped<IUserService, UserService>();

// Add background services
builder.Services.AddHostedService<DataUpdateService>();

// Add CORS for API access
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
});

var app = builder.Build();

// Ensure database is created and seeded with correct admin password
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
    
    try
    {
        // Ensure database is created
        context.Database.EnsureCreated();
        app.Logger.LogInformation("Database ensured created successfully");

        // Check if admin user exists and fix password if necessary
        var adminUser = context.Users.FirstOrDefault(u => u.Username == "admin");
        if (adminUser != null)
        {
            // Test if the current password works
            bool passwordWorks = userService.VerifyPassword("admin123", adminUser.PasswordHash);
            
            if (!passwordWorks)
            {
                app.Logger.LogWarning("Admin password hash is incorrect, fixing...");
                
                // Update with correct hash
                adminUser.PasswordHash = userService.HashPassword("admin123");
                adminUser.UpdatedAt = DateTime.UtcNow;
                context.SaveChanges();
                
                app.Logger.LogInformation("Admin password hash has been corrected");
            }
            else
            {
                app.Logger.LogInformation("Admin password is working correctly");
            }
        }
        else
        {
            // Create admin user if it doesn't exist
            app.Logger.LogInformation("Creating default admin user...");
            
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
            context.SaveChanges();
            
            app.Logger.LogInformation("Default admin user created successfully");
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error during database initialization");
    }
}

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseCors("AllowAll");

// Add Authentication & Authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// Map routes
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .RequireAuthorization("AuthenticatedUser"); // Require authentication for all routes

// Map SignalR hub (also requires authentication)
app.MapHub<DeviceDataHub>("/deviceHub")
    .RequireAuthorization("AuthenticatedUser");

app.Run();