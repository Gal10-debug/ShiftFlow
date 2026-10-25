using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Security;
using ShiftManager.Services;

var bootstrap = args.Contains("--bootstrap-admin");
var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--bootstrap-admin").ToArray());
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Database") ?? "Host=localhost;Database=shiftmanager;Username=shiftmanager"));
builder.Services.AddDataProtection();
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.Password.RequiredLength = 12;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.AllowedForNewUsers = true;
    options.User.AllowedUserNameCharacters = string.Empty;
}).AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<ApplicationDbContext>()
  .AddSignInManager().AddDefaultTokenProviders().AddClaimsPrincipalFactory<AppClaimsFactory>();
builder.Services.AddScoped<AppCookieEvents>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AvailabilityService>();
builder.Services.AddScoped<IHardBlockQuery>(services => services.GetRequiredService<AvailabilityService>());
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddCookie(IdentityConstants.ApplicationScheme, options =>
{
    options.Cookie.Name = builder.Environment.IsDevelopment() ? "ShiftFlow.Auth" : "__Host-ShiftFlow.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.EventsType = typeof(AppCookieEvents);
});
builder.Services.AddAuthorization(options =>
{
    var normal = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim("password_change_required", "false").Build();
    options.DefaultPolicy = normal;
    options.FallbackPolicy = normal;
    options.AddPolicy("SignedIn", p => p.RequireAuthenticatedUser());
    foreach (var role in AppRoles.All)
        options.AddPolicy(role, p => p.RequireAuthenticatedUser().RequireClaim("password_change_required", "false").RequireRole(role));
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = builder.Environment.IsDevelopment() ? "ShiftFlow.Csrf" : "__Host-ShiftFlow.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(ApiErrors.Error("RATE_LIMITED"), token);
    };
});
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = _ => new BadRequestObjectResult(ApiErrors.Error("VALIDATION_ERROR")));
builder.Services.AddOpenApi();
builder.Services.Configure<RequestLocalizationOptions>(options => options
    .SetDefaultCulture("he-IL").AddSupportedCultures("he-IL").AddSupportedUICultures("he-IL"));
var app = builder.Build();
if (bootstrap)
{
    try { await AdminBootstrap.Run(app.Services, app.Configuration); }
    catch (Exception error) when (error is InvalidOperationException or ApiException)
    {
        Console.Error.WriteLine(error is ApiException api ? $"Bootstrap failed: {api.Code}" : error.Message);
        Environment.ExitCode = 1;
    }
    return;
}
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (ApiException error)
    {
        context.Response.StatusCode = error.Status;
        await context.Response.WriteAsJsonAsync(ApiErrors.Error(error));
    }
    catch (Exception error)
    {
        // Never log request bodies, credentials, SQL parameter values or exception payloads.
        app.Logger.LogError("Request failed ({ExceptionType}), trace {TraceId}", error.GetType().Name, context.TraceIdentifier);
        var conflict = error is DbUpdateConcurrencyException || error is DbUpdateException { InnerException: PostgresException { SqlState: "23505" } };
        context.Response.StatusCode = conflict ? 409 : 500;
        await context.Response.WriteAsJsonAsync(ApiErrors.Error(conflict ? "CONFLICT" : "INTERNAL_ERROR"));
    }
});
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
    // Fail closed if no HTTPS redirect port has been configured.
    app.Use(async (context, next) =>
    {
        if (!context.Request.IsHttps)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(ApiErrors.Error("HTTPS_REQUIRED"));
            return;
        }
        await next(context);
    });
}
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; style-src 'self'; script-src 'self'; connect-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    await next(context);
});
app.UseStatusCodePages(async status =>
{
    var context = status.HttpContext;
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var code = context.Response.StatusCode switch
        {
            400 => "VALIDATION_ERROR",
            401 => "UNAUTHENTICATED",
            403 => "FORBIDDEN",
            404 => "NOT_FOUND",
            405 => "METHOD_NOT_ALLOWED",
            _ => "INTERNAL_ERROR"
        };
        await context.Response.WriteAsJsonAsync(ApiErrors.Error(code));
    }
});
app.UseRequestLocalization();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method)
        && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(ApiErrors.Error("CSRF_INVALID"));
            return;
        }
    }
    await next(context);
});
app.MapControllers();
foreach (var role in AppRoles.All)
    app.MapGet($"/api/access/{role.ToLowerInvariant()}", () => Results.NoContent()).RequireAuthorization(role);
if (app.Environment.IsDevelopment()) app.MapOpenApi().AllowAnonymous();
app.MapFallback(async context =>
{
    if (!context.Request.Path.StartsWithSegments("/api") && HttpMethods.IsGet(context.Request.Method)
        && File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html")))
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html"));
        return;
    }
    context.Response.StatusCode = 404;
    await context.Response.WriteAsJsonAsync(ApiErrors.Error("NOT_FOUND"));
}).AllowAnonymous();
await app.RunAsync();
public partial class Program;
