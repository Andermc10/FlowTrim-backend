using FlowTrim.Application.Imports;
using FlowTrim.Domain.Subscriptions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Imports;

/// <summary>HU-44: duplicados contra suscripciones existentes del propietario y contra filas anteriores del archivo.</summary>
public sealed class SubscriptionImportDuplicatesTests
{
    private const string Header = "name,amount,currency,billing_cycle,next_billing_date";
    private static readonly DateOnly Today = new(2026, 1, 1);

    [Fact]
    public void Classify_ExistingMatch_IsDuplicateOfExisting()
    {
        var existing = Existing("Netflix", "COP");
        existing.Update(existing.Name, existing.Category, existing.Amount, existing.Currency, existing.BillingCycle,
            existing.NextBillingDate, SubscriptionStatus.Inactive, Clock);

        // Una suscripción inactiva también cuenta; el nombre se compara sin mayúsculas y sin espacios alrededor.
        var row = Assert.Single(Classify($"{Header}\n  NETFLIX ,38900,cop,Monthly,2026-10-22", existing).Rows);

        Assert.Equal((ImportRowStatus.Duplicate, ImportDuplicateSource.Existing, (int?)null),
            (row.Status, row.DuplicateOf, row.DuplicateOfRow));
        Assert.Equal("NETFLIX", row.Values!.Name);
        Assert.Empty(row.Errors);
    }

    [Fact]
    public void Classify_RepeatedRow_IsDuplicateOfFirstRow()
    {
        var rows = Classify($"{Header}\nNetflix,1,COP,Monthly,2026-10-22\nGym,1,COP,Monthly,2026-10-22\nNetflix,1,COP,Monthly,2026-10-22\ngym,2,COP,Yearly,2026-11-22").Rows;

        Assert.Equal(
            [(2, ImportRowStatus.Valid, null, null), (3, ImportRowStatus.Valid, null, null),
             (4, ImportRowStatus.Duplicate, ImportDuplicateSource.File, 2), (5, ImportRowStatus.Duplicate, ImportDuplicateSource.File, (int?)3)],
            rows.Select(row => (row.RowNumber, row.Status, row.DuplicateOf, row.DuplicateOfRow)));
    }

    [Fact]
    public void Classify_ExistingMatchTakesPrecedenceOverFileMatch()
    {
        var rows = Classify($"{Header}\nNetflix,1,COP,Monthly,2026-10-22\nNetflix,1,COP,Monthly,2026-10-22", Existing("netflix", "COP")).Rows;

        // Ninguna de las dos se crearía, y las dos lo deben a la suscripción existente.
        Assert.All(rows, row => Assert.Equal((ImportRowStatus.Duplicate, ImportDuplicateSource.Existing), (row.Status, row.DuplicateOf)));
    }

    [Fact]
    public void Classify_OtherCurrency_IsValid()
    {
        var rows = Classify($"{Header}\nNetflix,1,USD,Monthly,2026-10-22\nNetflix,1,EUR,Monthly,2026-10-22", Existing("Netflix", "COP")).Rows;

        Assert.All(rows, row => Assert.Equal(ImportRowStatus.Valid, row.Status));
    }

    [Fact]
    public void Classify_AccentedName_IsNotFolded()
    {
        var row = Assert.Single(Classify($"{Header}\nCafé,1,COP,Monthly,2026-10-22", Existing("Cafe", "COP")).Rows);

        Assert.Equal(ImportRowStatus.Valid, row.Status);
    }

    [Fact]
    public void Classify_InvalidRow_StaysInvalid()
    {
        var row = Assert.Single(Classify($"{Header}\nNetflix,abc,COP,Monthly,2026-10-22", Existing("Netflix", "COP")).Rows);

        Assert.Equal((ImportRowStatus.Invalid, null), (row.Status, row.DuplicateOf));
        Assert.Equal(ImportErrorCodes.InvalidAmount, Assert.Single(row.Errors).Code);
    }

    [Fact]
    public void Classify_EarlierInvalidRow_DoesNotMakeDuplicate()
    {
        var rows = Classify($"{Header}\nGym,0,COP,Monthly,2026-10-22\nGym,1,COP,Monthly,2026-10-22").Rows;

        Assert.Equal([ImportRowStatus.Invalid, ImportRowStatus.Valid], rows.Select(row => row.Status));
    }

    [Fact]
    public void Classify_Summary_AddsUp()
    {
        var preview = Classify(
            $"{Header}\nNetflix,1,COP,Monthly,2026-10-22\nGym,1,COP,Monthly,2026-10-22\nGym,1,COP,Monthly,2026-10-22\nBad,x,COP,Monthly,2026-10-22\nNew,1,COP,Monthly,2026-10-22",
            Existing("Netflix", "COP"));

        Assert.Equal(new ImportSummary(5, 2, 1, 2), preview.Summary);
    }

    [Fact]
    public void Classify_NoExistingAndNoRepeats_LeavesPreviewUnchanged()
    {
        var parsed = SubscriptionCsvImport.Parse($"{Header}\nNetflix,1,COP,Monthly,2026-10-22\nBad,x,COP,Monthly,2026-10-22", Today).Preview!;

        var classified = SubscriptionImportDuplicates.Classify(parsed, []);

        Assert.Equal(parsed.Rows, classified.Rows);
        Assert.Equal(parsed.Summary, classified.Summary);
        Assert.Equal(parsed.IgnoredColumns, classified.IgnoredColumns);
    }

    private static readonly FixedTimeProvider Clock = new(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

    private static Subscription Existing(string name, string currency) =>
        Subscription.Create(TestCurrentUser.DefaultSubject, name, null, 100m, currency, BillingCycle.Monthly, new DateOnly(2026, 11, 1), Clock);

    private static SubscriptionImportPreview Classify(string csv, params Subscription[] existing) =>
        SubscriptionImportDuplicates.Classify(SubscriptionCsvImport.Parse(csv, Today).Preview!, existing);
}
