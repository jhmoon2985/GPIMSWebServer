using GPIMSWebServer.Hubs;
using GPIMSWebServer.Services;
using GPIMSWebServer.BackgroundServices;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

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
app.UseAuthorization();

// Map routes
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Map SignalR hub
app.MapHub<DeviceDataHub>("/deviceHub");

app.Run();