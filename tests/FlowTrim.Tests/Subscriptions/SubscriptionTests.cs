using FlowTrim.Domain.Common;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Subscriptions;

public sealed class SubscriptionTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 14, 6, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly SampleDate = new(2026, 11, 1);

    private static TimeProvider Clock() => new FixedTimeProvider(FixedNow);

    [Fact]
    public void Create_WithValidData_ReturnsActiveSubscription()
    {
        var subscription = Subscription.Create("Netflix", "Streaming", 38900m, "COP", BillingCycle.Monthly, SampleDate, Clock());

        Assert.NotEqual(Guid.Empty, subscription.Id);
        Assert.Equal("Netflix", subscription.Name);
        Assert.Equal("Streaming", subscription.Category);
        Assert.Equal(38900m, subscription.Amount);
        Assert.Equal("COP", subscription.Currency);
        Assert.Equal(BillingCycle.Monthly, subscription.BillingCycle);
        Assert.Equal(SampleDate, subscription.NextBillingDate);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithEmptyName_Throws(string? name)
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Subscription.Create(name, null, 10m, "COP", BillingCycle.Monthly, SampleDate, Clock()));

        Assert.Equal("name", ex.PropertyName);
    }

    [Fact]
    public void Create_WithNameLongerThanLimit_Throws()
    {
        var longName = new string('x', Subscription.NameMaxLength + 1);

        var ex = Assert.Throws<DomainValidationException>(() =>
            Subscription.Create(longName, null, 10m, "COP", BillingCycle.Monthly, SampleDate, Clock()));

        Assert.Equal("name", ex.PropertyName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(-0.01)]
    public void Create_WithNonPositiveAmount_Throws(decimal amount)
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Subscription.Create("Netflix", null, amount, "COP", BillingCycle.Monthly, SampleDate, Clock()));

        Assert.Equal("amount", ex.PropertyName);
    }

    [Fact]
    public void Create_RoundsAmountToTwoDecimals()
    {
        var subscription = Subscription.Create("Spotify", null, 10.005m, "COP", BillingCycle.Monthly, SampleDate, Clock());

        Assert.Equal(10.01m, subscription.Amount);
    }

    [Fact]
    public void Create_WithLowercaseCurrency_StoresUppercased()
    {
        var subscription = Subscription.Create("Netflix", null, 10m, "usd", BillingCycle.Monthly, SampleDate, Clock());

        Assert.Equal("USD", subscription.Currency);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_WithMissingCurrency_DefaultsToCop(string? currency)
    {
        var subscription = Subscription.Create("Netflix", null, 10m, currency, BillingCycle.Monthly, SampleDate, Clock());

        Assert.Equal("COP", subscription.Currency);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("U1D")]
    public void Create_WithInvalidCurrencyFormat_Throws(string currency)
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Subscription.Create("Netflix", null, 10m, currency, BillingCycle.Monthly, SampleDate, Clock()));

        Assert.Equal("currency", ex.PropertyName);
    }

    [Fact]
    public void Create_WithCategoryLongerThanLimit_Throws()
    {
        var longCategory = new string('x', Subscription.CategoryMaxLength + 1);

        var ex = Assert.Throws<DomainValidationException>(() =>
            Subscription.Create("Netflix", longCategory, 10m, "COP", BillingCycle.Monthly, SampleDate, Clock()));

        Assert.Equal("category", ex.PropertyName);
    }

    [Fact]
    public void Create_WithBlankCategory_StoresNull()
    {
        var subscription = Subscription.Create("Netflix", "   ", 10m, "COP", BillingCycle.Monthly, SampleDate, Clock());

        Assert.Null(subscription.Category);
    }

    [Fact]
    public void Create_WithUnsupportedBillingCycle_Throws()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Subscription.Create("Netflix", null, 10m, "COP", (BillingCycle)99, SampleDate, Clock()));

        Assert.Equal("billingCycle", ex.PropertyName);
    }

    [Fact]
    public void Create_SetsCreatedAndUpdatedFromTimeProvider()
    {
        var subscription = Subscription.Create("Netflix", null, 10m, "COP", BillingCycle.Monthly, SampleDate, Clock());

        Assert.Equal(FixedNow, subscription.CreatedAt);
        Assert.Equal(FixedNow, subscription.UpdatedAt);
    }

    [Fact]
    public void Update_WithValidData_UpdatesFieldsAndRefreshesUpdatedAt()
    {
        var subscription = Subscription.Create("Netflix", "Streaming", 10m, "COP", BillingCycle.Monthly, SampleDate, Clock());
        var later = FixedNow.AddDays(1);
        var newDate = new DateOnly(2026, 12, 1);

        subscription.Update("Spotify", "Music", 19.99m, "usd", BillingCycle.Yearly, newDate, SubscriptionStatus.Inactive, new FixedTimeProvider(later));

        Assert.Equal("Spotify", subscription.Name);
        Assert.Equal("Music", subscription.Category);
        Assert.Equal(19.99m, subscription.Amount);
        Assert.Equal("USD", subscription.Currency);
        Assert.Equal(BillingCycle.Yearly, subscription.BillingCycle);
        Assert.Equal(newDate, subscription.NextBillingDate);
        Assert.Equal(SubscriptionStatus.Inactive, subscription.Status);
        Assert.Equal(later, subscription.UpdatedAt);
    }

    [Fact]
    public void Update_KeepsIdAndCreatedAt()
    {
        var subscription = Subscription.Create("Netflix", null, 10m, "COP", BillingCycle.Monthly, SampleDate, Clock());
        var originalId = subscription.Id;
        var originalCreatedAt = subscription.CreatedAt;

        subscription.Update("Netflix", null, 12m, "COP", BillingCycle.Monthly, SampleDate, SubscriptionStatus.Active, new FixedTimeProvider(FixedNow.AddDays(1)));

        Assert.Equal(originalId, subscription.Id);
        Assert.Equal(originalCreatedAt, subscription.CreatedAt);
    }

    [Fact]
    public void Update_WithNonPositiveAmount_Throws()
    {
        var subscription = Subscription.Create("Netflix", null, 10m, "COP", BillingCycle.Monthly, SampleDate, Clock());

        var ex = Assert.Throws<DomainValidationException>(() =>
            subscription.Update("Netflix", null, 0m, "COP", BillingCycle.Monthly, SampleDate, SubscriptionStatus.Active, Clock()));

        Assert.Equal("amount", ex.PropertyName);
    }

    [Fact]
    public void Update_WithUnsupportedStatus_Throws()
    {
        var subscription = Subscription.Create("Netflix", null, 10m, "COP", BillingCycle.Monthly, SampleDate, Clock());

        var ex = Assert.Throws<DomainValidationException>(() =>
            subscription.Update("Netflix", null, 10m, "COP", BillingCycle.Monthly, SampleDate, (SubscriptionStatus)99, Clock()));

        Assert.Equal("status", ex.PropertyName);
    }
}
