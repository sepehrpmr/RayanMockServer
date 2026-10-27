using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using RayanMockServer.Data;
using RayanMockServer.Middleware;
using RayanMockServer.Services;
using RayanMockServer.Swagger;

var builder = WebApplication.CreateBuilder(args);

// 1. Add Database Context (SQL Server + EF Core)
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// 2. Add In-Memory Caching and Custom Mock Cache Service
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IMockCacheService, MockCacheService>();

// 3. Add MVC / API Controllers with JSON formatting options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null; // Preserve exact C# property casing for Persian UI consistency
        options.JsonSerializerOptions.WriteIndented = true;
    });

// 4. Configure Swagger with Dynamic SQL Document Filter
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Rayan Dynamic Mock API & Swagger",
        Version = "v1",
        Description = "مستندات وب‌سرویس‌های صندوق سرمایه‌گذاری رایان هم‌افزا با قابلیت تولید داینامیک از SQL Server و شبیه‌سازی سناریوهای خطا",
        Contact = new OpenApiContact
        {
            Name = "تیم توسعه صندوق سرمایه‌گذاری",
            Url = new Uri("http://localhost:5000/admin")
        }
    });

    // Add our dynamic DB DocumentFilter
    c.DocumentFilter<SqlSwaggerDocumentFilter>();
});

// 5. Add standard CORS policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader()
              .WithExposedHeaders("x-mock-response-code", "x-mock-response-name");
    });
});

var app = builder.Build();

// 6. Ensure Database Created and Seeded with 116 Rayan Services
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        logger.LogInformation("Checking database and applying migrations / EnsureCreated...");
        await db.Database.EnsureCreatedAsync();

        logger.LogInformation("Seeding initial Rayan HamAfza mock services and variables...");
        await DataSeeder.SeedAsync(db, env);
        logger.LogInformation("Database initialized successfully!");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while creating or seeding the database.");
    }
}

// 7. Configure HTTP Request Pipeline
app.UseCors("AllowAll");

// Serve Web Management UI from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

// Always enable Swagger UI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Rayan Fund API v1 (Dynamic)");
    c.RoutePrefix = "swagger";
    c.DocumentTitle = "مستندات وب‌سرویس‌های صندوق رایان هم‌افزا";
    c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
});

// 8. Map API Controllers for Admin Management (/api/admin-mgmt/...)
app.MapControllers();

// 9. Execute Dynamic Mock Execution Engine for all client /api/... calls
app.UseMiddleware<DynamicMockMiddleware>();

// 10. Fallback to Web UI for SPA routing (/admin, etc.)
app.MapFallbackToFile("index.html");

app.Run();
