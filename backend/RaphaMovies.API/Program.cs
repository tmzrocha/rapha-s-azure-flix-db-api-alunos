using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using RaphaMovies.API.Data;
using RaphaMovies.API.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Entity Framework
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// AutoMapper
builder.Services.AddAutoMapper(typeof(Program));

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Secret"] ?? builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key/Secret not configured");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "RaphaMovies";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "RaphaMoviesApp";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("admin"));
});

// Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IMovieService, MovieService>();
builder.Services.AddScoped<IRentalService, RentalService>();
builder.Services.AddScoped<IAdminService, AdminService>();

// CORS - Configurável via:
// 1. appsettings.json: Cors:AllowedOrigins (array)
// 2. Variável de ambiente: CORS_ORIGINS (separado por vírgula)
// 3. Azure App Settings: Cors__AllowedOrigins__0, Cors__AllowedOrigins__1, etc.
//
// Para facilitar ambientes de aula/preview (origens dinâmicas), é possível habilitar:
// - Cors:AllowAnyOrigin=true (ou env CORS_ALLOW_ANY=true)
var corsAllowAnyOrigin =
    builder.Configuration.GetValue<bool>("Cors:AllowAnyOrigin") ||
    string.Equals(Environment.GetEnvironmentVariable("CORS_ALLOW_ANY"), "true", StringComparison.OrdinalIgnoreCase);

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

// Fallback: verificar variável de ambiente CORS_ORIGINS (separada por vírgula)
if (corsOrigins == null || corsOrigins.Length == 0)
{
    var envCorsOrigins = Environment.GetEnvironmentVariable("CORS_ORIGINS");
    if (!string.IsNullOrWhiteSpace(envCorsOrigins))
    {
        corsOrigins = envCorsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

// Default para desenvolvimento local
corsOrigins ??= new[] { "http://localhost:5173", "http://localhost:3000", "http://localhost:8080" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        if (corsAllowAnyOrigin)
        {
            // Importante: não usar AllowCredentials com origem liberada
            policy.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
        else
        {
            policy.WithOrigins(corsOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
    });
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "POS TFTEC Movies API", 
        Version = "v1",
        Description = "API para o sistema de aluguel de filmes POS TFTEC Movies"
    });
    
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
// Enable Swagger in all environments for easier debugging
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "POS TFTEC Movies API v1");
    c.RoutePrefix = "swagger";
});

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Health check endpoint for diagnostics
app.MapGet("/", () => Results.Ok(new { 
    status = "healthy", 
    service = "RaphaMovies API",
    timestamp = DateTime.UtcNow 
}));

app.MapGet("/health", () => Results.Ok(new { 
    status = "healthy",
    timestamp = DateTime.UtcNow 
}));

// Apply migrations on startup
// In Production, failing migrations/DB connectivity can crash the app (HTTP 500.30).
// Default behavior: migrate only in Development. To enable in Production, set:
// Database__ApplyMigrationsOnStartup=true
var applyMigrationsOnStartup =
    app.Environment.IsDevelopment() ||
    app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup");

if (applyMigrationsOnStartup)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.Migrate();
        app.Logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to apply database migrations on startup.");
        if (app.Environment.IsDevelopment())
            throw;
    }
}

app.Run();
