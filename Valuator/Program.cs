using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using NATS.Client;
using Shared;
using Valuator.Services;

namespace Valuator;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddSingleton(new ShardedStorage());
        builder.Services.AddSingleton<UserService>();

        builder.Services.AddSingleton<IConnection>(
            Messaging.Connect(builder.Configuration["Nats:Url"])
        );

        builder.Services.AddDataProtection().SetApplicationName("Valuator");

        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/Login";
                options.Cookie.Name = "valuator.auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
            });
        builder.Services.AddAuthorization();

        builder.Services.AddRazorPages(options =>
        {
            options.Conventions.AuthorizePage("/Index");
            options.Conventions.AuthorizePage("/Summary");
        });

        var app = builder.Build();

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseStaticFiles();

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapRazorPages();

        app.Run();
    }
}
