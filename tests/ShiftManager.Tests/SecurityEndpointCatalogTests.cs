using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.DependencyInjection;

namespace ShiftManager.Tests;

public sealed partial class SchedulingTests
{
    [PostgresFact]
    public async Task Security_endpoint_catalog_keeps_every_admin_action_in_the_denial_matrix()
    {
        var resources = await SecuritySetup(); using var admin = resources.Admin;
        var calls = await SecurityAdminCalls(resources);
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } p && (p.StartsWith("api/admin/", StringComparison.OrdinalIgnoreCase)
                || p.StartsWith("api/users", StringComparison.OrdinalIgnoreCase))).ToArray();
        Assert.NotEmpty(endpoints);
        foreach (var endpoint in endpoints)
        {
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), a => a.Policy == "ADMIN");
            var matcher = new TemplateMatcher(TemplateParser.Parse(endpoint.RoutePattern.RawText!), new RouteValueDictionary());
            foreach (var method in endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods)
                Assert.True(calls.Any(c => c.Method == method && matcher.TryMatch(new PathString(c.Path.Split('?')[0]), new RouteValueDictionary())),
                    $"Missing real-resource security matrix case: {method} {endpoint.RoutePattern.RawText}");
        }
    }
}
