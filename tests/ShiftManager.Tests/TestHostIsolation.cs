using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ShiftManager.Tests;

internal static class TestHostIsolation
{
    // Real Identity/antiforgery cryptography, with keys owned only by this host.
    // Tests must never reuse the development/demo data-protection key directory.
    internal static IWebHostBuilder WithIsolatedTestKeys(this IWebHostBuilder builder) => builder.ConfigureServices(services =>
        services.AddDataProtection().UseEphemeralDataProtectionProvider().SetApplicationName("ShiftFlow.Disposable.Tests"));
}
