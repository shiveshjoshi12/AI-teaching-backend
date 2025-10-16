using Microsoft.EntityFrameworkCore;
using AI_driven_teaching_platform.Models;
using AI_driven_teaching_platform.Services;
using AI_driven_teaching_platform.Data;

var builder = WebApplication.CreateBuilder(args);

// Your existing services
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure settings (your existing code)
builder.Services.Configure<OpenRouterSettings>(
    builder.Configuration.GetSection("OpenRouter"));

builder.Services.Configure<HeyGenSettings>(
    builder.Configuration.GetSection("HeyGen"));

// Add PostgreSQL Database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register services
builder.Services.AddScoped<HeyGenService>();
builder.Services.AddScoped<LanguageService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IDatabaseDocumentService, DatabaseDocumentService>();
builder.Services.AddHttpClient<DocumentService>();

// ===== ADD CORS CONFIGURATION =====
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins(
                "http://localhost:3000",      // React Create React App
                "http://localhost:5173",      // Vite React App
                "https://localhost:3000",
                "https://localhost:5173"
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

var app = builder.Build();

// CREATE DATABASE TABLES ON STARTUP
using (var scope = app.Services.CreateScope())
{
    try
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync();
        Console.WriteLine("✅ Database and tables created successfully!");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Database creation failed: {ex.Message}");
    }
}

// ===== USE CORS BEFORE OTHER MIDDLEWARE =====
app.UseCors("AllowReactApp");

// Your existing middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

Console.WriteLine("🚀 AI Teaching Platform Started!");
Console.WriteLine("📚 Swagger: http://localhost:5152/swagger");
Console.WriteLine("💾 PostgreSQL Database Connected");
Console.WriteLine("🔍 Qdrant: localhost:6333");
Console.WriteLine("🌐 CORS enabled for React apps");

app.Run();
