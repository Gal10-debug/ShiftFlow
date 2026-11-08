using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShiftManager.Api;

namespace ShiftManager.Tests;

public sealed partial class AuthTests
{
    private sealed class SecurityAuthClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2030-12-01T10:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static string SecurityCookie(HttpResponseMessage response, string name = "ShiftFlow.Auth") =>
        response.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith(name + "=", StringComparison.Ordinal)).Split(';')[0];

    [PostgresFact]
    public async Task Security_cookie_ticket_has_an_absolute_eight_hour_lifetime_without_sliding_renewal()
    {
        var clock = new SecurityAuthClock();
        await using var timed = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        { s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(clock); }));
        using var client = timed.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        await Login(client, "employee");
        clock.Now += TimeSpan.FromHours(7);
        var valid = await client.GetAsync("/api/auth/me"); Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.False(valid.Headers.TryGetValues("Set-Cookie", out var renewals) && renewals.Any(h => h.StartsWith("ShiftFlow.Auth=")), "Session lifetime must not slide.");
        clock.Now += TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1);
        await Error(await client.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
    }

    [PostgresFact]
    public async Task Security_modified_cookie_is_rejected_without_exposing_the_cookie_or_cryptographic_details()
    {
        using var signedIn = Client();
        var login = await Send(signedIn, "/api/auth/login", new { username = "employee", password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode); var pair = SecurityCookie(login);
        using var replay = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), HandleCookies = false, AllowAutoRedirect = false });
        replay.DefaultRequestHeaders.Add("Cookie", pair);
        Assert.Equal(HttpStatusCode.OK, (await replay.GetAsync("/api/auth/me")).StatusCode);
        replay.DefaultRequestHeaders.Remove("Cookie"); var offset = pair.IndexOf('=') + 20;
        var corrupt = pair[..offset] + (pair[offset] == 'A' ? 'B' : 'A') + pair[(offset + 1)..];
        replay.DefaultRequestHeaders.Add("Cookie", corrupt);
        var response = await replay.GetAsync("/api/auth/me");
        await Error(response, HttpStatusCode.Unauthorized, "UNAUTHENTICATED");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(!body.Contains(pair) && !body.Contains(corrupt), "Cookie material must not be returned to clients.");
        Assert.DoesNotContain("Exception", body); Assert.DoesNotContain("StackTrace", body);
    }

    [PostgresFact]
    public async Task Security_logout_revokes_copied_tickets_and_other_sessions_without_printing_session_material()
    {
        using var client = Client();
        var response = await Send(client, "/api/auth/login", new { username = "employee", password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var cookie = SecurityCookie(response);
        using var copiedTicket = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), HandleCookies = false, AllowAutoRedirect = false });
        copiedTicket.DefaultRequestHeaders.Add("Cookie", cookie);
        using var otherSession = Client(); await Login(otherSession, "employee");
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, "/api/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await copiedTicket.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await otherSession.GetAsync("/api/auth/me")).StatusCode);
        await Login(otherSession, "employee");
        Assert.Equal(HttpStatusCode.OK, (await otherSession.GetAsync("/api/auth/me")).StatusCode);
    }

    [PostgresTheory]
    [InlineData("Development")] [InlineData("Production")]
    public async Task Security_headers_cors_preflight_and_debug_routes_follow_the_environment(string environment)
    {
        await using var host = factory.WithWebHostBuilder(b => b.UseEnvironment(environment));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://shiftflow.test"), AllowAutoRedirect = false });
        var csrf = await client.GetAsync("/api/auth/csrf"); Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        Assert.True(csrf.Headers.CacheControl!.NoStore);
        Assert.Equal("nosniff", csrf.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("same-origin", csrf.Headers.GetValues("Referrer-Policy").Single());
        var csp = csrf.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("frame-ancestors 'none'", csp); Assert.Contains("form-action 'self'", csp);
        Assert.DoesNotContain("unsafe-inline", csp); Assert.DoesNotContain("unsafe-eval", csp);
        Assert.Equal(environment == "Production", csrf.Headers.Contains("Strict-Transport-Security"));
        using var foreignRead = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf"); foreignRead.Headers.Add("Origin", "https://attacker.invalid");
        var read = await client.SendAsync(foreignRead); Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.False(read.Headers.Contains("Access-Control-Allow-Origin")); Assert.False(read.Headers.Contains("Access-Control-Allow-Credentials"));
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/users");
        preflight.Headers.Add("Origin", "https://attacker.invalid"); preflight.Headers.Add("Access-Control-Request-Method", "PUT");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type,x-csrf-token");
        var denied = await client.SendAsync(preflight);
        Assert.True(denied.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
        Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin")); Assert.False(denied.Headers.Contains("Access-Control-Allow-Credentials"));
        var openapi = await client.GetAsync("/openapi/v1.json");
        if (environment == "Development") Assert.Equal(HttpStatusCode.OK, openapi.StatusCode);
        else Assert.DoesNotContain("\"openapi\"", await openapi.Content.ReadAsStringAsync());
        var controls = await client.GetAsync("/__e2e/state");
        if (controls.IsSuccessStatusCode) Assert.Equal("text/html", controls.Content.Headers.ContentType?.MediaType);
        else Assert.Equal(HttpStatusCode.NotFound, controls.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/__e2e/ready")).StatusCode);
    }

    [PostgresFact]
    public async Task Security_initial_password_account_can_logout_without_bypassing_the_password_gate()
    {
        using var client = Client(); await Login(client, "firstlogin");
        await Error(await client.GetAsync("/api/users"), HttpStatusCode.Forbidden, "PASSWORD_CHANGE_REQUIRED");
        Assert.Equal(HttpStatusCode.NoContent, (await Send(client, "/api/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        await Login(client, "firstlogin");
        await Error(await client.GetAsync("/api/users"), HttpStatusCode.Forbidden, "PASSWORD_CHANGE_REQUIRED");
    }

    [PostgresFact]
    public async Task Security_preauthentication_csrf_token_cannot_authorize_a_post_login_mutation()
    {
        using var client = Client();
        var old = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf"); await Login(client, "employee");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Add("X-CSRF-TOKEN", old.GetProperty("requestToken").GetString());
        await Error(await client.SendAsync(request), HttpStatusCode.BadRequest, "CSRF_INVALID");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
}
