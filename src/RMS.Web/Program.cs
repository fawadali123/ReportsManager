using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RMS.Core.Domain;
using RMS.Infrastructure;
using RMS.Web.Security;

var builder = WebApplication.CreateBuilder(args);

// Machine-specific settings (connection strings, LDAP server) that should stay out of source control.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Oracle repositories, LDAP sign-in, report engine and exporters.
builder.Services.AddReportsManagement(builder.Configuration, builder.Environment);

builder.Services.AddControllersWithViews(options =>
{
    // Every POST must carry an antiforgery token, including the AJAX ones.
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = ".RMS.Antiforgery";
    options.HeaderName = "RequestVerificationToken";
});

var sessionMinutes = builder.Configuration.GetValue("Authentication:SessionMinutes", 60);

builder.Services.AddScoped<RmsCookieEvents>();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".RMS.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(Math.Max(5, sessionMinutes));
        options.SlidingExpiration = true;
        options.EventsType = typeof(RmsCookieEvents);
    });

builder.Services.AddAuthorization(options =>
{
    // Signed-in by default: a new controller is protected unless it opts out with [AllowAnonymous].
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.AddPolicy(Policies.AdminOnly, policy => policy.RequireRole(Roles.Admin));
});

// Slows down password guessing against the login form. Active Directory's own lockout policy still applies.
builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsync("Too many sign-in attempts. Wait a minute, then go back and try again.", ct);
    };

    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Home/Status/{0}");
app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "same-origin";
    // All scripts and styles are served from this site; nothing is loaded from a CDN.
    headers.ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; "
        + "font-src 'self'; frame-ancestors 'none'; form-action 'self'; base-uri 'self'";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute("areas", "{area:exists}/{controller=Users}/{action=Index}/{id?}");
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

app.Run();
