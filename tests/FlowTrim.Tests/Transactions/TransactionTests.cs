using FlowTrim.Domain.Common;
using FlowTrim.Domain.Transactions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Transactions;

/// <summary>Forma e invariantes de <see cref="Transaction"/> (EP-12 §7/§8; los casos de uso llegan con HU-66).</summary>
public sealed class TransactionTests
{
    private const string Owner = "owner-a";
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Day = new(2026, 10, 20);
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid OtherAccountId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();

    [Fact]
    public void Create_Expense_KeepsCategoryAndTrimsTexts()
    {
        var transaction = Transaction.Create(
            Owner, TransactionType.Expense, AccountId, null, CategoryId, 38900m, Day, "  Lunch ", "  ", Clock);

        Assert.Equal(CategoryId, transaction.CategoryId);
        Assert.Null(transaction.ToAccountId);
        Assert.Equal("Lunch", transaction.Description);
        Assert.Null(transaction.Note);
    }

    [Fact]
    public void Create_Transfer_HasDestinationAndNoCategory()
    {
        var transaction = Transaction.Create(
            Owner, TransactionType.Transfer, AccountId, OtherAccountId, null, 100000m, Day, null, null, Clock);

        Assert.Equal(OtherAccountId, transaction.ToAccountId);
        Assert.Null(transaction.CategoryId);
        Assert.True(transaction.Touches(AccountId));
        Assert.True(transaction.Touches(OtherAccountId));
    }

    public static TheoryData<TransactionType, bool, bool, bool, string> InvalidShapes() => new()
    {
        // tipo, con destino, destino = origen, con categoría, campo
        { TransactionType.Transfer, false, false, false, "toAccountId" },
        { TransactionType.Transfer, true, true, false, "toAccountId" },
        { TransactionType.Transfer, true, false, true, "categoryId" },
        { TransactionType.Expense, false, false, false, "categoryId" },
        { TransactionType.Income, true, false, true, "toAccountId" },
    };

    [Theory]
    [MemberData(nameof(InvalidShapes))]
    public void Create_InvalidShape_ThrowsForField(
        TransactionType type, bool withDestination, bool sameAccount, bool withCategory, string field)
    {
        Guid? destination = withDestination ? (sameAccount ? AccountId : OtherAccountId) : null;
        Guid? category = withCategory ? CategoryId : null;

        var exception = Assert.Throws<DomainValidationException>(() =>
            Transaction.Create(Owner, type, AccountId, destination, category, 10m, Day, null, null, Clock));

        Assert.Equal(field, exception.PropertyName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.001)]
    public void Create_InvalidAmount_Throws(double amount)
    {
        var exception = Assert.Throws<DomainValidationException>(() =>
            Transaction.Create(Owner, TransactionType.Expense, AccountId, null, CategoryId, (decimal)amount, Day, null, null, Clock));

        Assert.Equal("amount", exception.PropertyName);
    }

    [Fact]
    public void Create_DescriptionTooLong_Throws()
    {
        var exception = Assert.Throws<DomainValidationException>(() => Transaction.Create(
            Owner, TransactionType.Expense, AccountId, null, CategoryId, 10m, Day,
            new string('a', Transaction.DescriptionMaxLength + 1), null, Clock));

        Assert.Equal("description", exception.PropertyName);
    }

    [Fact]
    public void Update_InvalidChange_LeavesTransactionUnchanged()
    {
        var transaction = Transaction.Create(
            Owner, TransactionType.Expense, AccountId, null, CategoryId, 10m, Day, "Lunch", null, Clock);

        Assert.Throws<DomainValidationException>(() => transaction.Update(
            TransactionType.Expense, AccountId, null, CategoryId, -5m, Day, "Changed", null, Clock));

        Assert.Equal(10m, transaction.Amount);
        Assert.Equal("Lunch", transaction.Description);
    }
}
