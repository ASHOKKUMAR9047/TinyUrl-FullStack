using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TinyUrl.Api.Data;
using TinyUrl.Api.Services;

try
{
    var builder = WebApplication.CreateBuilder(args);

    // 1. Setup Serilog rolling file logging
    string logDirectory = Path.Combine(builder.Environment.ContentRootPath, "logs");
    if (!Directory.Exists(logDirectory))
    {
        Directory.CreateDirectory(logDirectory);
    }
    string logPath = Path.Combine(logDirectory, "tinyurl_log-.txt");

    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console()
        .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, outputTemplate: 
            "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

    builder.Host.UseSerilog();
    Log.Information("Starting TinyURL MVC Controller Web API Service...");

    // 2. Configure Dynamic Database Providers (SQLite or SQL Server)
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrEmpty(connectionString))
    {
        string dbPath = Path.Combine(builder.Environment.ContentRootPath, "tinyurl.db");
        connectionString = $"Data Source={dbPath}";
    }

    Log.Information("Configuring database context. Connection String type detected: " + 
                    (connectionString.Contains(".db") || connectionString.Contains("Data Source=") || connectionString.Contains("DataSource=") ? "SQLite" : "SQL Server"));

    builder.Services.AddDbContext<AppDbContext>(options =>
    {
        if (connectionString.Contains(".db") || connectionString.Contains("Data Source=") || connectionString.Contains("DataSource="))
        {
            options.UseSqlite(connectionString);
        }
        else
        {
            options.UseSqlServer(connectionString);
        }
    });

    // 3. Configure CORS to allow communication from frontend servers
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
    });

    // 4. Add MVC Controllers support
    builder.Services.AddControllers();

    // 5. Register Database Cleanup Hosted Background Service
    builder.Services.AddHostedService<DatabaseCleanupWorker>();

    // 6. Add Swagger OpenAPI Documentation
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var app = builder.Build();

    // 7. Database Migration / Schema Creation automatically at startup
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        try
        {
            var context = services.GetRequiredService<AppDbContext>();
            context.Database.EnsureCreated();
            Log.Information("Database schema verified and created successfully.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occurred while creating or migrating the database.");
        }
    }

    // Configure the HTTP request pipeline
    if (app.Environment.IsDevelopment() || true) // Enabled globally for frictionless testing
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Tiny URL API v1");
            options.RoutePrefix = "swagger"; // Access Swagger UI at http://localhost:<port>/swagger
        });
    }

    app.UseHttpsRedirection();
    app.UseCors("AllowFrontend");

    // Route Root URL to Swagger UI
    app.MapGet("/", (HttpContext context) =>
    {
        context.Response.Redirect("/swagger");
        return Task.CompletedTask;
    });

    app.UseAuthorization();

    // 8. Map MVC Controllers
    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly!");
}
finally
{
    Log.CloseAndFlush();
}
