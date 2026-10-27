using System.Security.Claims;
using System.Text.Json;
using FlowTrim.Application.Insights;
using FlowTrim.Application.Insights.Tools;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Api;
using FlowTrim.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FlowTrim.Tests.Insights.Tools;

/// <summary>HU-34: el registro se resuelve con el contenedor real de la API y usa el propietario de la petición.</summary>
public sealed class FinancialToolRegistryHostTests : IClassFixture<FlowTrimApiFactory>
{
    private const string OwnerA = "auth0|registry-a";
    private const string OwnerB = "auth0|registry-b";

    private readonly FlowTrimApiFactory _factory;

    public FinancialToolRegistryHostTests(FlowTrimApiFactory factory)
    {
        _factory = factory;
        _factory.Subscriptions.Items.Clear();
    }

    [Fact]
    public async Task Registry_ResolvedFromApiContainer_UsesRequestOwner()
    {
        var clock = new FixedTimeProvider(DateTimeOffset.UtcNow);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        _factory.Subscriptions.Items.Add(Subscription.Create(OwnerA, "Netflix", null, 38900m, "COP", BillingCycle.Monthly, today.AddDays(40), clock));
        _factory.Subscriptions.Items.Add(Subscription.Create(OwnerB, "Gym", null, 90000m, "COP", BillingCycle.Monthly, today.AddDays(40), clock));

        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", OwnerA)], "Test")),
        };
        var registry = scope.ServiceProvider.GetRequiredService<FinancialToolRegistry>();

        var result = await registry.ExecuteAsync(new ChatToolCall("call-1", "get_spending_summary", "{}"), default);

        Assert.Equal(8, registry.Definitions.Count);
        using var json = JsonDocument.Parse(result.ToJson());
        var cop = Assert.Single(json.RootElement.GetProperty("data").GetProperty("currencies").EnumerateArray());
        Assert.Equal(38900m, cop.GetProperty("monthlyEquivalent").GetDecimal());
        Assert.Equal(1, cop.GetProperty("activeCount").GetInt32());
    }
}
