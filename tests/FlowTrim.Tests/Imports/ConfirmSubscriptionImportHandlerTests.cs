using System.Text;
using FlowTrim.Application.Imports;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Imports;

/// <summary>HU-45: confirmación con el mismo archivo, solo filas válidas, idempotente y por propietario.</summary>
public sealed class ConfirmSubscriptionImportHandlerTests
{
    private const string Owner = "auth0|confirm-a";
    private const string Header = "name,amount,currency,billing_cycle,next_billing_date,status";
    private static readonly FixedTimeProvider Clock = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

    private readonly InMemorySubscriptionRepository _repository = new();

    [Fact]
    public async Task Confirm_ValidRows_CreatesOnlyValidRowsForOwner()
    {
        _repository.Items.Add(Existing(Owner, "Netflix", "COP"));
        _repository.Items.Add(Existing("auth0|other", "Spotify", "COP"));

        var result = await ConfirmAsync(
            $"{Header}\nnetflix,38900,COP,Monthly,2026-10-22,Active\nSpotify,16900,cop,Monthly,2026-10-25,\n" +
            "Gym,90000.5,COP,Yearly,2026-11-01,Active\nBroken,abc,COP,Monthly,2026-10-22,Active");

        Assert.Null(result.FileError);
        Assert.Equal(new SubscriptionImportConfirmation(4, 2, 1, 1), result.Confirmation);
        var created = _repository.Items.Where(s => s.OwnerId == Owner && s.Name != "Netflix").OrderBy(s => s.Name).ToList();
        Assert.Equal(
            [("Gym", 90000.5m, "COP", BillingCycle.Yearly, new DateOnly(2026, 11, 1)), ("Spotify", 16900m, "COP", BillingCycle.Monthly, new DateOnly(2026, 10, 25))],
            created.Select(s => (s.Name, s.Amount, s.Currency, s.BillingCycle, s.NextBillingDate)));
        Assert.All(created, s => Assert.Equal(Clock.GetUtcNow(), s.CreatedAt));
        // La suscripción del otro propietario sigue siendo la única suya.
        Assert.Single(_repository.Items, s => s.OwnerId == "auth0|other");
    }

    [Fact]
    public async Task Confirm_InactiveRow_CreatesInactiveSubscription()
    {
        await ConfirmAsync($"{Header}\nGym,1,COP,Monthly,2026-10-22,inactive\nPark,1,COP,Monthly,2026-10-22,");

        Assert.Equal(
            [("Gym", SubscriptionStatus.Inactive), ("Park", SubscriptionStatus.Active)],
            _repository.Items.OrderBy(s => s.Name).Select(s => (s.Name, s.Status)));
    }

    [Fact]
    public async Task Confirm_SameFileTwice_CreatesNothingTheSecondTime()
    {
        const string csv = $"{Header}\nGym,1,COP,Monthly,2026-10-22,Active\nGym,1,COP,Monthly,2026-10-22,Active\nPark,1,COP,Monthly,2026-10-22,Active";

        var first = await ConfirmAsync(csv);
        var second = await ConfirmAsync(csv);

        Assert.Equal(new SubscriptionImportConfirmation(3, 2, 0, 1), first.Confirmation);
        Assert.Equal(new SubscriptionImportConfirmation(3, 0, 0, 3), second.Confirmation);
        Assert.Equal(2, _repository.Items.Count);
    }

    [Fact]
    public async Task Confirm_NoValidRows_ReturnsZeroCreated()
    {
        var result = await ConfirmAsync($"{Header}\nBroken,0,COP,Monthly,2026-10-22,Active");

        Assert.Equal(new SubscriptionImportConfirmation(1, 0, 1, 0), result.Confirmation);
        Assert.Equal(0, _repository.SaveCount);
    }

    [Theory]
    [InlineData("subscriptions.txt", $"{Header}\nGym,1,COP,Monthly,2026-10-22,Active", ImportErrorCodes.UnsupportedFileType)]
    [InlineData("subscriptions.csv", "name,amount\nGym,1", ImportErrorCodes.MissingColumns)]
    public async Task Confirm_FileProblem_CreatesNothing(string fileName, string csv, string code)
    {
        var result = await ConfirmAsync(csv, fileName);

        Assert.Null(result.Confirmation);
        Assert.Equal(code, result.FileError!.Code);
        Assert.Empty(_repository.Items);
    }

    [Fact]
    public async Task Confirm_PersistenceFailure_PropagatesAndCreatesNothing()
    {
        _repository.FailNextImport = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => ConfirmAsync($"{Header}\nGym,1,COP,Monthly,2026-10-22,Active"));

        Assert.Empty(_repository.Items);
    }

    private async Task<ConfirmSubscriptionImportResult> ConfirmAsync(string csv, string fileName = "subscriptions.csv")
    {
        var handler = new ConfirmSubscriptionImportHandler(_repository, new TestCurrentUser(Owner), Clock);
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return await handler.HandleAsync(fileName, content, CancellationToken.None);
    }

    private static Subscription Existing(string owner, string name, string currency) =>
        Subscription.Create(owner, name, null, 100m, currency, BillingCycle.Monthly, new DateOnly(2026, 11, 1), Clock);
}
