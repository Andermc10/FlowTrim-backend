using FlowTrim.Application.Accounts;
using FlowTrim.Application.Common;
using FlowTrim.Application.Transactions;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Common;
using FlowTrim.Domain.Transactions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Transactions;

/// <summary>Casos de uso de movimientos (HU-66) con repositorios en memoria que comparten la lista de movimientos.</summary>
public sealed class TransactionHandlersTests
{
    // 2026-10-20 10:00 en Bogotá: hoy = 2026-10-20 en el calendario del producto.
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero));
    private static readonly DateOnly Today = new(2026, 10, 20);

    private readonly InMemoryAccountRepository _accounts = new();
    private readonly InMemoryCategoryRepository _categories;
    private readonly InMemoryTransactionRepository _transactions;
    private readonly TestCurrentUser _user = new();

    private readonly Account _bank;
    private readonly Account _cash;
    private readonly Account _usd;
    private readonly Category _food;
    private readonly Category _salary;

    public TransactionHandlersTests()
    {
        _categories = new InMemoryCategoryRepository { Transactions = _accounts.Transactions };
        _transactions = new InMemoryTransactionRepository(_accounts);
        _bank = SeedAccount("Bank", "COP", 500000m);
        _cash = SeedAccount("Cash", "COP", 0m, AccountType.Cash);
        _usd = SeedAccount("Dollars", "USD", 100m);
        _food = SeedCategory("Food", CategoryKind.Expense);
        _salary = SeedCategory("Salary", CategoryKind.Income);
    }

    [Fact]
    public async Task Create_Expense_StoresItWithAccountCurrencyAndMovesTheBalance()
    {
        var result = await Create(Expense(38900m) with { Description = "Lunch" });

        Assert.True(result.IsSuccess);
        Assert.Equal("COP", result.Value!.Currency);
        Assert.Equal("Bank", result.Value.AccountName);
        Assert.Equal("Food", result.Value.CategoryName);
        Assert.Equal("orange", result.Value.CategoryColor);
        Assert.Equal(461100m, await BalanceOf(_bank));
    }

    [Fact]
    public async Task Create_ExpenseWithIncomeCategory_ReturnsCategoryError()
    {
        // AC-1 de HU-66.
        var result = await Create(Expense(10m) with { CategoryId = _salary.Id });

        Assert.Equal(["An expense needs an expense category."], result.Errors["categoryId"]);
        Assert.Empty(_transactions.Items);
    }

    [Fact]
    public async Task Create_Transfer_MovesBothBalancesAndIsNotIncomeOrExpense()
    {
        // AC-2 de HU-66.
        var result = await Create(Transfer(_bank, _cash, 100000m));

        Assert.True(result.IsSuccess);
        Assert.Equal("Cash", result.Value!.ToAccountName);
        Assert.Null(result.Value.CategoryId);
        Assert.Equal(400000m, await BalanceOf(_bank));
        Assert.Equal(100000m, await BalanceOf(_cash));
        var list = await List(new ListTransactionsQuery(null, null, null, null, null, null, null, null));
        Assert.Empty(list.Value!.Totals);
    }

    [Fact]
    public async Task Create_TransferBetweenCurrencies_ReturnsDestinationError()
    {
        var result = await Create(Transfer(_bank, _usd, 10m));

        Assert.Equal(["Both accounts of a transfer must use the same currency."], result.Errors["toAccountId"]);
    }

    [Fact]
    public async Task Create_TomorrowAndMissingFields_ReturnFieldErrors()
    {
        // AC-3 de HU-66.
        var tomorrow = await Create(Expense(10m) with { Date = Today.AddDays(1) });
        var missing = await Create(new TransactionRequest("Expense", null, null, _food.Id, null, null, null, null));

        Assert.Equal(["Transaction date cannot be in the future."], tomorrow.Errors["date"]);
        Assert.Equal(["accountId", "amount", "date"], missing.Errors.Keys.Order());
    }

    [Fact]
    public async Task Create_CategoryOnTransferOrMissingOnExpense_IsRejectedByDomain()
    {
        var onTransfer = await Assert.ThrowsAsync<DomainValidationException>(() =>
            Create(Transfer(_bank, _cash, 10m) with { CategoryId = _food.Id }));
        var missing = await Assert.ThrowsAsync<DomainValidationException>(() => Create(Expense(10m) with { CategoryId = null }));

        Assert.Equal("categoryId", onTransfer.PropertyName);
        Assert.Equal("categoryId", missing.PropertyName);
    }

    [Fact]
    public async Task Create_ForeignOrArchivedReferences_AreRejectedWithoutRevealingOtherOwners()
    {
        var foreign = Account.Create("someone-else", "Their bank", AccountType.Bank, "COP", 0m, Clock);
        _accounts.Items.Add(foreign);
        _cash.Update("Cash", AccountType.Cash, "COP", 0m, isArchived: true, Clock);
        _food.Update("Food", CategoryKind.Expense, "orange", "food", isArchived: true, Clock);

        var foreignAccount = await Create(Expense(10m) with { AccountId = foreign.Id, CategoryId = SeedCategory("Gifts", CategoryKind.Expense).Id });
        var archivedAccount = await Create(Transfer(_bank, _cash, 10m));
        var archivedCategory = await Create(Expense(10m));

        Assert.Equal(["Account not found."], foreignAccount.Errors["accountId"]);
        Assert.Contains("archived", archivedAccount.Errors["toAccountId"][0]);
        Assert.Contains("archived", archivedCategory.Errors["categoryId"][0]);
    }

    [Fact]
    public async Task Update_KeepingAnArchivedCategory_IsAllowedButChoosingOneIsNot()
    {
        var created = (await Create(Expense(10m))).Value!;
        _food.Update("Food", CategoryKind.Expense, "orange", "food", isArchived: true, Clock);
        var gifts = SeedCategory("Gifts", CategoryKind.Expense);
        var handler = new UpdateTransactionHandler(_transactions, _accounts, _categories, Clock, _user);

        var keep = await handler.HandleAsync(created.Id, Expense(25m), default);
        gifts.Update("Gifts", CategoryKind.Expense, "pink", "gifts", isArchived: true, Clock);
        var choose = await handler.HandleAsync(created.Id, Expense(25m) with { CategoryId = gifts.Id }, default);

        Assert.True(keep.IsSuccess);
        Assert.Equal(25m, keep.Value!.Amount);
        Assert.Equal("Food", keep.Value.CategoryName);
        Assert.Contains("archived", choose.Errors["categoryId"][0]);
    }

    [Fact]
    public async Task Update_ChangingAccountAndType_RecomputesBothBalances()
    {
        var created = (await Create(Expense(1000m))).Value!;

        var result = await new UpdateTransactionHandler(_transactions, _accounts, _categories, Clock, _user)
            .HandleAsync(created.Id, Expense(1000m) with { Type = "Income", AccountId = _cash.Id, CategoryId = _salary.Id }, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(500000m, await BalanceOf(_bank));
        Assert.Equal(1000m, await BalanceOf(_cash));
    }

    [Fact]
    public async Task List_FiltersByMonthAndComputesTotalsForTheWholeFilter()
    {
        // AC-4 de HU-66: octubre solo muestra filas y totales de octubre.
        await Create(Expense(100m) with { Date = new DateOnly(2026, 9, 30) });
        await Create(Expense(200m) with { Date = new DateOnly(2026, 10, 1), Description = "Market" });
        await Create(Expense(300m) with { Date = new DateOnly(2026, 10, 15), Description = "Lunch" });
        await Create(new TransactionRequest("Income", _bank.Id, null, _salary.Id, 5000m, new DateOnly(2026, 10, 5), "Pay", null));
        await Create(Transfer(_bank, _cash, 50m) with { Date = new DateOnly(2026, 10, 6) });

        var october = new ListTransactionsQuery(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), null, null, null, null, 1, 2);
        var result = await List(october);

        Assert.Equal(4, result.Value!.TotalCount);
        Assert.Equal([new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 6)], result.Value.Items.Select(t => t.Date));
        Assert.Equal(new CurrencyTotals("COP", 5000m, 500m, 4500m), Assert.Single(result.Value.Totals));

        var search = await List(october with { Search = "LUN", Page = null, PageSize = null });
        Assert.Equal(["Lunch"], search.Value!.Items.Select(t => t.Description));
        var cashOnly = await List(october with { AccountId = _cash.Id, PageSize = null });
        Assert.Equal(["Transfer"], cashOnly.Value!.Items.Select(t => t.Type));
    }

    [Fact]
    public async Task List_InvalidTypeOrRange_ReturnsFieldErrors()
    {
        var badType = await List(new ListTransactionsQuery(null, null, null, null, "expense", null, null, null));
        var badRange = await List(new ListTransactionsQuery(Today, Today.AddDays(-1), null, null, null, null, null, null));

        Assert.True(badType.Errors.ContainsKey("type"));
        Assert.True(badRange.Errors.ContainsKey("from"));
    }

    [Fact]
    public async Task List_PageSizeIsClampedTo200()
    {
        var result = await List(new ListTransactionsQuery(null, null, null, null, null, null, 0, 5000));

        Assert.Equal(1, result.Value!.Page);
        Assert.Equal(ListTransactionsHandler.MaxPageSize, result.Value.PageSize);
    }

    [Fact]
    public async Task GetUpdateDelete_OtherOwnersTransaction_ReturnNotFound()
    {
        var created = (await Create(Expense(10m))).Value!;
        var stranger = new TestCurrentUser("someone-else");

        Assert.Null(await new GetTransactionHandler(_transactions, _accounts, _categories, stranger).HandleAsync(created.Id, default));
        Assert.Equal(HandlerResultKind.NotFound, (await new UpdateTransactionHandler(_transactions, _accounts, _categories, Clock, stranger)
            .HandleAsync(created.Id, Expense(99m), default)).Kind);
        Assert.False(await new DeleteTransactionHandler(_transactions, stranger).HandleAsync(created.Id, default));
        Assert.True(await new DeleteTransactionHandler(_transactions, _user).HandleAsync(created.Id, default));
        Assert.Empty(_transactions.Items);
    }

    private Task<HandlerResult<TransactionResponse>> Create(TransactionRequest request) =>
        new CreateTransactionHandler(_transactions, _accounts, _categories, Clock, _user).HandleAsync(request, default);

    private Task<HandlerResult<TransactionListResponse>> List(ListTransactionsQuery query) =>
        new ListTransactionsHandler(_transactions, _accounts, _categories, _user).HandleAsync(query, default);

    private async Task<decimal> BalanceOf(Account account) =>
        (await new GetAccountHandler(_accounts, _user).HandleAsync(account.Id, default))!.Balance;

    private TransactionRequest Expense(decimal amount) =>
        new("Expense", _bank.Id, null, _food.Id, amount, Today, null, null);

    private static TransactionRequest Transfer(Account from, Account to, decimal amount) =>
        new("Transfer", from.Id, to.Id, null, amount, Today, null, null);

    private Account SeedAccount(string name, string currency, decimal opening, AccountType type = AccountType.Bank)
    {
        var account = Account.Create(TestCurrentUser.DefaultSubject, name, type, currency, opening, Clock);
        _accounts.Items.Add(account);
        return account;
    }

    private Category SeedCategory(string name, CategoryKind kind)
    {
        var category = Category.Create(TestCurrentUser.DefaultSubject, name, kind, kind == CategoryKind.Income ? "green" : "orange",
            kind == CategoryKind.Income ? "salary" : "food", Clock);
        _categories.Items.Add(category);
        return category;
    }
}
