using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers()
    .AddNewtonsoftJson(); // For JSON serialization compatibility

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddLogging(config =>
{
    config.AddConsole();
    config.AddDebug();
});

// Add HTTP client factory
builder.Services.AddHttpClient();

// Register custom services
builder.Services.AddScoped<ScreenConnect.WebApp.Services.ScreenConnectService>();
builder.Services.AddScoped<ScreenConnect.WebApp.Services.SelfExtractorService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseRouting();
app.UseCors();

app.MapControllers();

app.MapGet("/", () => "ScreenConnect Web App - Linux Version");

app.Run(); 