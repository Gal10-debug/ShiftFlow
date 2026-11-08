using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ShiftManager.Api;

namespace ShiftManager.Tests;

internal static class TestHostIsolation
{
    // Real Identity/antiforgery cryptography, with keys owned only by this host.
    // Tests must never reuse the development/demo data-protection key directory.
    internal static IWebHostBuilder WithIsolatedTestKeys(this IWebHostBuilder builder) => builder.ConfigureServices(services =>
    {
        services.AddDataProtection().UseEphemeralDataProtectionProvider().SetApplicationName("ShiftFlow.Disposable.Tests");
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_TIMING_DIRECTORY")))
            services.Configure<MvcOptions>(options => options.Filters.Add(new FailureTimingFilter()));
    });

    private sealed class FailureTimingFilter : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var result = await next();
            if (result.Exception is { } error && error is not ApiException)
            {
                context.ActionDescriptor.RouteValues.TryGetValue("action", out var action);
                TestTiming.Failure(action ?? "unknown", error);
            }
        }
    }
}
