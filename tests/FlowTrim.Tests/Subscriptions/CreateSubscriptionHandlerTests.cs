using FlowTrim.Application.Subscriptions;
using FlowTrim.Domain.Common;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Subscriptions;

public sealed class CreateSubscriptionHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 14, 6, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly SampleDate = new(2026, 11, 1);

    private static (CreateSubscriptionHandler Handler, InMemorySubscriptionRepository Repo) BuildHandler()
    {
        var repo = new InMemorySubscriptionRepository();
        var handler = new CreateSubscriptionHandler(repo, new FixedTimeProvider(FixedNow), new TestCurrentUser(), new InMemoryAccountRepository(), new InMemoryCategoryRepository());
        return (handler, repo);
    }

    private static CreateSubscriptionRequest ValidRequest(
        string? name = "Netflix",
        decimal? amount = 38900m,
        string? currency = "COP",
        string? billingCycle = "Monthly",
        string? category = "Streaming") =>
        new(name, category, amount, currency, billingCycle, SampleDate);

    // ----- HU-56: la próxima fecha de cobro es de hoy en adelante (calendario de Bogotá) -----

    [Fact]
    public async Task HandleAsync_NextBillingDateBeforeToday_ReturnsInvalidAndPersistsNothing()
    {
        var (handler, repo) = BuildHandler();

        var result = await handler.HandleAsync(ValidRequest() with { NextBillingDate = new DateOnly(2026, 10, 13) }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(["Next billing date cannot be in the past."], result.Errors["nextBillingDate"]);
        Assert.Empty(repo.Items);
    }

    [Fact]
    public async Task HandleAsync_NextBillingDateToday_Succeeds()
    {
        var (handler, _) = BuildHandler();

        var result = await handler.HandleAsync(ValidRequest() with { NextBillingDate = new DateOnly(2026, 10, 14) }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_LateEveningInBogota_UsesTheBogotaDayNotTheUtcDay()
    {
        // 2026-10-15 03:00 UTC es 2026-10-14 22:00 en Bogotá: "hoy" sigue siendo el 14.
        var handler = new CreateSubscriptionHandler(
            new InMemorySubscriptionRepository(),
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 15, 3, 0, 0, TimeSpan.Zero)),
            new TestCurrentUser(),
            new InMemoryAccountRepository(),
            new InMemoryCategoryRepository());

        var result = await handler.HandleAsync(ValidRequest() with { NextBillingDate = new DateOnly(2026, 10, 14) }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_WithValidRequest_PersistsAndReturnsResponse()
    {
        var (handler, repo) = BuildHandler();

        var result = await handler.HandleAsync(ValidRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Netflix", result.Value!.Name);
        Assert.Equal("Active", result.Value.Status);
        Assert.Equal("Monthly", result.Value.BillingCycle);
        Assert.Equal(FixedNow, result.Value.CreatedAt);
        Assert.Equal(1, repo.SaveCount);
        Assert.Single(repo.Items);
    }

    [Fact]
    public async Task HandleAsync_WithMissingAmount_ReturnsAmountErrorAndDoesNotPersist()
    {
        var (handler, repo) = BuildHandler();

        var result = await handler.HandleAsync(ValidRequest(amount: null), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Errors.ContainsKey("amount"));
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_WithInvalidBillingCycle_ReturnsBillingCycleError()
    {
        var (handler, _) = BuildHandler();

        var result = await handler.HandleAsync(ValidRequest(billingCycle: "Daily"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.Errors.ContainsKey("billingCycle"));
    }

    [Fact]
    public async Task HandleAsync_WithEmptyName_ThrowsDomainValidation()
    {
        var (handler, repo) = BuildHandler();

        // Las reglas de valor las impone el dominio: lanza y el manejador global las traduce a 400.
        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            handler.HandleAsync(ValidRequest(name: "   "), CancellationToken.None));

        Assert.Equal("name", ex.PropertyName);
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public async Task HandleAsync_WithNonPositiveAmount_ThrowsDomainValidation()
    {
        var (handler, _) = BuildHandler();

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            handler.HandleAsync(ValidRequest(amount: 0m), CancellationToken.None));

        Assert.Equal("amount", ex.PropertyName);
    }

    [Fact]
    public async Task HandleAsync_WithMissingCurrency_DefaultsToCop()
    {
        var (handler, _) = BuildHandler();

        var result = await handler.HandleAsync(ValidRequest(currency: null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("COP", result.Value!.Currency);
    }
}
