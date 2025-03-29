using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace PresentationsSoftware;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        
        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });
            
        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseMySql(builder.Configuration.GetConnectionString("RemoteConnection"),
                new MySqlServerVersion(new Version(8, 0, 23))));
        // builder.Services.AddDbContext<ApplicationDbContext>(options =>
        //     options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

        builder.Services.AddSignalR()
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("CorsPolicy", policyBuilder =>
                policyBuilder
                    .WithOrigins("https://presentations-software-frontend.vercel.app")
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials());
        });

        var app = builder.Build();

        app.UseCors("CorsPolicy");
        app.UseHttpsRedirection();
        app.UseAuthorization();

        app.MapControllers();
        app.MapHub<PresentationHub>("/presentationHub");

        app.Run();
    }
}