using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using svc.products.Data;
using svc.products.Validation;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Database
        builder.Services.AddDbContext<ProductsDb>(opt =>
            opt.UseNpgsql(builder.Configuration.GetConnectionString("ProductsDb")));

        // AuthN/Z
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.Authority = builder.Configuration["Jwt:Authority"];
                o.Audience = builder.Configuration["Jwt:Audience"];
                o.RequireHttpsMetadata = true;
            });

        builder.Services.AddAuthorization(o =>
        {
            o.AddPolicy("products:read", p => p.RequireClaim("scope", "products.read"));
            o.AddPolicy("products:write", p => p.RequireClaim("scope", "products.write"));
        });

        // CORS
        builder.Services.AddCors(o =>
        {
            o.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>())
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            });
        });

        // Validation
        builder.Services.AddControllers().AddNewtonsoftJson();
        builder.Services.AddValidatorsFromAssemblyContaining<ProductCreateValidator>();

        // Swagger (dev only)
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.MapControllers();

        using (var scope = app.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            var logger = services.GetRequiredService<ILogger<Program>>();

            try
            {
                var db = services.GetRequiredService<ProductsDb>();
                // Optional: simple retry if DB not up yet (dev convenience)
                const int maxAttempts = 10;
                var delay = TimeSpan.FromSeconds(3);

                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        db.Database.MigrateAsync();
                        logger.LogInformation("Database migrated successfully.");
                        break;
                    }
                    catch (Npgsql.NpgsqlException ex) when (attempt < maxAttempts)
                    {
                        logger.LogWarning(ex, "DB not ready yet. Attempt {Attempt}/{MaxAttempts}. Retrying in {Delay}s...", attempt, maxAttempts, delay.TotalSeconds);
                        Task.Delay(delay);
                    }
                }

                // Optional seed (dev only)
                // if (!await db.Products.AnyAsync()) { db.Products.Add(new Product { ... }); await db.SaveChangesAsync(); }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error applying migrations");
                throw; // Let container crash rather than run with a broken DB
            }
        }

        app.Run();
    }
}