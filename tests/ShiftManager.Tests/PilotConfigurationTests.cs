using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ShiftManager.Operations;

namespace ShiftManager.Tests;

public sealed class PilotConfigurationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "shiftflow-pilot-config-" + Guid.NewGuid().ToString("N"));
    private readonly string id = Guid.NewGuid().ToString("N");
    private const string Canary = "PrivateConfigurationCanary!123456789";

    private Dictionary<string, string?> Settings()
    {
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var keys = Path.Combine(directory, "keys"); Directory.CreateDirectory(keys);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(keys, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Disposable pilot configuration test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var pfx = Path.Combine(directory, "certificate.pfx"); File.WriteAllBytes(pfx, cert.Export(X509ContentType.Pfx, Canary));
        var pass = Path.Combine(directory, "password"); File.WriteAllText(pass, Canary);
        var connection = Path.Combine(directory, "connection");
        File.WriteAllText(connection, $"Host=postgres;Database=shiftflow_pilot_{id};Username=sf_{id}_runtime;Password={Canary};Maximum Pool Size=20");
        return new() { ["PILOT_MODE"] = "1", ["PILOT_DEPLOYMENT_ID"] = id, ["PILOT_DATABASE_NAME"] = "shiftflow_pilot_" + id,
            ["PILOT_DATABASE_HOST"] = "postgres", ["PILOT_PUBLIC_ORIGIN"] = "https://pilot.invalid", ["PILOT_TRUSTED_PROXY"] = "172.30.90.2",
            ["PILOT_DATABASE_CONNECTION_FILE"] = connection, ["PILOT_KEYS_DIRECTORY"] = keys,
            ["PILOT_KEY_CERTIFICATE_FILE"] = pfx, ["PILOT_KEY_CERTIFICATE_PASSWORD_FILE"] = pass };
    }

    [Theory]
    [InlineData("PILOT_DEPLOYMENT_ID", "invalid")]
    [InlineData("PILOT_DATABASE_NAME", "shiftmanager")]
    [InlineData("PILOT_PUBLIC_ORIGIN", "http://pilot.invalid")]
    [InlineData("PILOT_PUBLIC_ORIGIN", "https://pilot.invalid/path")]
    [InlineData("PILOT_TRUSTED_PROXY", "0.0.0.0")]
    [InlineData("PILOT_TRUSTED_PROXY", "::")]
    [InlineData("PILOT_DATABASE_CONNECTION_FILE", "/missing/secret")]
    public void Invalid_pilot_configuration_fails_without_echoing_secrets(string key, string value)
    {
        var settings = Settings(); settings[key] = value;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(settings);
        var error = Assert.Throws<InvalidOperationException>(() => PilotConfiguration.Configure(builder, null, false));
        Assert.DoesNotContain(Canary, error.Message);
    }

    [Fact]
    public void Pilot_requires_the_expected_runtime_role_and_private_key_directory()
    {
        var settings = Settings();
        File.WriteAllText(settings["PILOT_DATABASE_CONNECTION_FILE"]!, $"Host=postgres;Database=shiftflow_pilot_{id};Username=pilot_owner;Password={Canary};Maximum Pool Size=20");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(settings);
        Assert.Throws<InvalidOperationException>(() => PilotConfiguration.Configure(builder, null, false));
    }

    [Fact]
    public void Demo_commands_cannot_use_pilot_configuration()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(Settings());
        Assert.Throws<InvalidOperationException>(() => PilotConfiguration.Configure(builder, null, true));
    }

    [Fact]
    public void Valid_pilot_configuration_uses_a_stable_namespace_and_exact_proxy()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(Settings());
        var settings = PilotConfiguration.Configure(builder, null, false)!;
        Assert.Equal(id + "/shiftflow_pilot_" + id, settings.Confirmation);
        Assert.Equal("sf_" + id + "_runtime", settings.Role("runtime"));
        Assert.Equal(IPAddress.Parse("172.30.90.2"), settings.Proxy);
        settings.Certificate.Dispose();
    }

    [Fact]
    public async Task Health_reports_only_status_and_separates_liveness_from_database_readiness()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Production").WithIsolatedTestKeys()
            .UseSetting("ConnectionStrings:Database", "Host=127.0.0.1;Port=1;Database=shiftflow_ci;Username=unused;Password=" + Canary));
        using var https = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var live = await https.GetAsync("/health/live"); Assert.Equal(HttpStatusCode.OK, live.StatusCode); Assert.Equal("Healthy", await live.Content.ReadAsStringAsync());
        var ready = await https.GetAsync("/health/ready"); Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode); Assert.Equal("Unhealthy", await ready.Content.ReadAsStringAsync());
        using var http = factory.CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf"); request.Headers.Add("X-Forwarded-Proto", "https");
        var forged = await http.SendAsync(request); Assert.False(forged.IsSuccessStatusCode); Assert.False(forged.Headers.Contains("Set-Cookie"));
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
