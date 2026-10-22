using FlowTrim.Application.Common;
using FlowTrim.Application.Usage;
using FlowTrim.Domain.Common;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Domain.Usage;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Usage;

public sealed class UsageTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 14, 6, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly UsageDate = new(2026, 10, 10);

    // "Ahora" del registro de usos: fin de año, para que las fechas de las pruebas no sean futuras (HU-56).
    private static readonly DateTimeOffset RegisterNow = new(2026, 12, 31, 12, 0, 0, TimeSpan.Zero);

    private static Subscription NewSubscription() =>
        Subscription.Create(TestCurrentUser.DefaultSubject, "Netflix", null, 10m, "COP", BillingCycle.Monthly, new DateOnly(2026, 11, 1), new FixedTimeProvider(FixedNow));

    // ----- Domain -----

    [Fact]
    public void Create_WithValidData_Succeeds()
    {
        var record = UsageRecord.Create(Guid.NewGuid(), UsageDate, 3, "watched 3 movies");

        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.Equal(3, record.UsageCount);
        Assert.Equal("watched 3 movies", record.Notes);
    }

    [Fact]
    public void Create_WithZeroCount_Succeeds()
    {
        var record = UsageRecord.Create(Guid.NewGuid(), UsageDate, 0, null);
        Assert.Equal(0, record.UsageCount);
    }

    [Fact]
    public void Create_WithNegativeCount_Throws()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            UsageRecord.Create(Guid.NewGuid(), UsageDate, -1, null));
        Assert.Equal("usageCount", ex.PropertyName);
    }

    [Fact]
    public void Create_WithTooLongNotes_Throws()
    {
        var notes = new string('x', UsageRecord.NotesMaxLength + 1);
        var ex = Assert.Throws<DomainValidationException>(() =>
            UsageRecord.Create(Guid.NewGuid(), UsageDate, 1, notes));
        Assert.Equal("notes", ex.PropertyName);
    }

    [Fact]
    public void Create_WithBlankNotes_StoresNull()
    {
        var record = UsageRecord.Create(Guid.NewGuid(), UsageDate, 1, "   ");
        Assert.Null(record.Notes);
    }

    [Fact]
    public void Create_WithEmptySubscriptionId_Throws()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            UsageRecord.Create(Guid.Empty, UsageDate, 1, null));
        Assert.Equal("subscriptionId", ex.PropertyName);
    }

    // ----- Handlers -----

    private static (RegisterUsageHandler Register, ListUsageHandler List, InMemorySubscriptionRepository Subs, InMemoryUsageRecordRepository Usage) Build()
    {
        var subs = new InMemorySubscriptionRepository();
        var usage = new InMemoryUsageRecordRepository();
        var currentUser = new TestCurrentUser();
        return (
            new RegisterUsageHandler(subs, usage, currentUser, new FixedTimeProvider(RegisterNow)),
            new ListUsageHandler(subs, usage, currentUser),
            subs,
            usage);
    }

    [Fact]
    public async Task Register_UnknownSubscription_ReturnsNotFound()
    {
        var (register, _, _, usage) = Build();

        var result = await register.HandleAsync(Guid.NewGuid(), new RegisterUsageRequest(UsageDate, 2, null), CancellationToken.None);

        Assert.Equal(HandlerResultKind.NotFound, result.Kind);
        Assert.Empty(usage.Items);
    }

    [Fact]
    public async Task Register_Valid_PersistsAndReturnsCreated()
    {
        var (register, _, subs, usage) = Build();
        var subscription = NewSubscription();
        subs.Items.Add(subscription);

        var result = await register.HandleAsync(subscription.Id, new RegisterUsageRequest(UsageDate, 2, "note"), CancellationToken.None);

        Assert.Equal(HandlerResultKind.Success, result.Kind);
        Assert.Equal(subscription.Id, result.Value!.SubscriptionId);
        Assert.Single(usage.Items);
    }

    [Fact]
    public async Task Register_FutureDate_ReturnsInvalid()
    {
        var subs = new InMemorySubscriptionRepository();
        var usage = new InMemoryUsageRecordRepository();
        var subscription = NewSubscription();
        subs.Items.Add(subscription);
        var register = new RegisterUsageHandler(subs, usage, new TestCurrentUser(), new FixedTimeProvider(FixedNow));

        // FixedNow es 2026-10-14 en Bogotá: el 15 todavía no ha ocurrido.
        var future = await register.HandleAsync(subscription.Id, new RegisterUsageRequest(new DateOnly(2026, 10, 15), 1, null), CancellationToken.None);
        var today = await register.HandleAsync(subscription.Id, new RegisterUsageRequest(new DateOnly(2026, 10, 14), 1, null), CancellationToken.None);

        Assert.Equal(HandlerResultKind.Invalid, future.Kind);
        Assert.Equal(["Usage date cannot be in the future."], future.Errors["usageDate"]);
        Assert.Equal(HandlerResultKind.Success, today.Kind);
        Assert.Single(usage.Items);
    }

    [Fact]
    public async Task Register_MissingDate_ReturnsInvalid()
    {
        var (register, _, subs, _) = Build();
        var subscription = NewSubscription();
        subs.Items.Add(subscription);

        var result = await register.HandleAsync(subscription.Id, new RegisterUsageRequest(null, 2, null), CancellationToken.None);

        Assert.Equal(HandlerResultKind.Invalid, result.Kind);
        Assert.True(result.Errors.ContainsKey("usageDate"));
    }

    [Fact]
    public async Task Register_NegativeCount_ThrowsDomainValidation()
    {
        var (register, _, subs, _) = Build();
        var subscription = NewSubscription();
        subs.Items.Add(subscription);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() =>
            register.HandleAsync(subscription.Id, new RegisterUsageRequest(UsageDate, -2, null), CancellationToken.None));
        Assert.Equal("usageCount", ex.PropertyName);
    }

    [Fact]
    public async Task List_UnknownSubscription_ReturnsNull()
    {
        var (_, list, _, _) = Build();

        var result = await list.HandleAsync(Guid.NewGuid(), null, null, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task List_FiltersByPeriodAndOrdersDescending()
    {
        var (register, list, subs, _) = Build();
        var subscription = NewSubscription();
        subs.Items.Add(subscription);
        await register.HandleAsync(subscription.Id, new RegisterUsageRequest(new DateOnly(2026, 10, 1), 1, null), CancellationToken.None);
        await register.HandleAsync(subscription.Id, new RegisterUsageRequest(new DateOnly(2026, 10, 15), 2, null), CancellationToken.None);
        await register.HandleAsync(subscription.Id, new RegisterUsageRequest(new DateOnly(2026, 11, 1), 3, null), CancellationToken.None);

        var result = await list.HandleAsync(subscription.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Equal(new DateOnly(2026, 10, 15), result[0].UsageDate); // descendente
    }
}
