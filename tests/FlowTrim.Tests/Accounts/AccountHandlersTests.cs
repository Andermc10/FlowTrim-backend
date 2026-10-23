using FlowTrim.Application.Accounts;
using FlowTrim.Application.Common;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Transactions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Accounts;

/// <summary>Casos de uso de cuentas (HU-64) con el repositorio en memoria.</summary>
public sealed class AccountHandlersTests
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero));
    private readonly InMemoryAccountRepository _repository = new();
    private readonly TestCurrentUser _user = new();

    [Fact]
    public async Task Create_ValidRequest_StoresAccountWithOwnerAndOpeningBalanceAsBalance()
    {
        var result = await new CreateAccountHandler(_repository, Clock, _user)
            .HandleAsync(new CreateAccountRequest("Bank", "Bank", "cop", 500000m), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(500000m, result.Value!.Balance);
        Assert.False(result.Value.HasTransactions);
        var stored = Assert.Single(_repository.Items);
        Assert.Equal(TestCurrentUser.DefaultSubject, stored.OwnerId);
        Assert.Equal("COP", stored.Currency);
    }

    [Fact]
    public async Task Create_MissingTypeAndOpeningBalance_ReturnsFieldErrors()
    {
        var result = await new CreateAccountHandler(_repository, Clock, _user)
            .HandleAsync(new CreateAccountRequest("Bank", "Checking", "COP", null), default);

        Assert.Equal(HandlerResultKind.Invalid, result.Kind);
        Assert.Equal(["type", "openingBalance"], result.Errors.Keys);
        Assert.Empty(_repository.Items);
    }

    [Fact]
    public async Task Create_NameUsedByArchivedAccountWithOtherCase_ReturnsNameError()
    {
        var archived = Seed("Bank");
        archived.Update("Bank", AccountType.Bank, "COP", 0m, isArchived: true, Clock);

        var result = await new CreateAccountHandler(_repository, Clock, _user)
            .HandleAsync(new CreateAccountRequest("  bank ", "Bank", "COP", 0m), default);

        Assert.Equal(HandlerResultKind.Invalid, result.Kind);
        Assert.Equal([Account.DuplicateNameMessage], result.Errors["name"]);
    }

    [Fact]
    public async Task Create_SameNameAsAnotherOwner_Succeeds()
    {
        Seed("Bank", owner: "someone-else");

        var result = await new CreateAccountHandler(_repository, Clock, _user)
            .HandleAsync(new CreateAccountRequest("Bank", "Bank", "COP", 0m), default);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task List_ExcludesArchivedUnlessRequestedAndComputesBalances()
    {
        var bank = Seed("Bank", opening: 500000m);
        var archived = Seed("Old wallet");
        archived.Update("Old wallet", AccountType.Cash, "COP", 0m, isArchived: true, Clock);
        _repository.Transactions.Add(Expense(bank.Id, 38900m));

        var handler = new ListAccountsHandler(_repository, _user);
        var active = await handler.HandleAsync(includeArchived: false, default);
        var all = await handler.HandleAsync(includeArchived: true, default);

        var single = Assert.Single(active);
        Assert.Equal(461100m, single.Balance);
        Assert.True(single.HasTransactions);
        Assert.Equal(["Bank", "Old wallet"], all.Select(a => a.Name));
    }

    [Fact]
    public async Task Update_CurrencyOfAccountWithTransactions_ReturnsConflict()
    {
        var bank = Seed("Bank");
        _repository.Transactions.Add(Expense(bank.Id, 10m));

        var result = await new UpdateAccountHandler(_repository, Clock, _user)
            .HandleAsync(bank.Id, new UpdateAccountRequest("Bank", "Bank", "USD", 0m, false), default);

        Assert.Equal(HandlerResultKind.Conflict, result.Kind);
        Assert.Equal(UpdateAccountHandler.CurrencyLockedCode, result.ConflictCode);
        Assert.Equal("COP", bank.Currency);
    }

    [Fact]
    public async Task Update_AccountWithTransactions_CanBeArchivedAndRenamed()
    {
        var bank = Seed("Bank", opening: 100m);
        _repository.Transactions.Add(Expense(bank.Id, 10m));

        var result = await new UpdateAccountHandler(_repository, Clock, _user)
            .HandleAsync(bank.Id, new UpdateAccountRequest("Old bank", "Bank", " cop ", 100m, true), default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsArchived);
        Assert.Equal(90m, result.Value.Balance);
    }

    [Fact]
    public async Task Update_CurrencyOfAccountWithoutTransactions_Succeeds()
    {
        var bank = Seed("Bank");

        var result = await new UpdateAccountHandler(_repository, Clock, _user)
            .HandleAsync(bank.Id, new UpdateAccountRequest("Bank", "Bank", "USD", 0m, false), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("USD", bank.Currency);
    }

    [Fact]
    public async Task Update_KeepingOwnName_IsNotADuplicate()
    {
        var bank = Seed("Bank");

        var result = await new UpdateAccountHandler(_repository, Clock, _user)
            .HandleAsync(bank.Id, new UpdateAccountRequest("BANK", "Bank", "COP", 0m, false), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("BANK", bank.Name);
    }

    [Fact]
    public async Task Update_MissingArchivedFlag_ReturnsFieldError()
    {
        var bank = Seed("Bank");

        var result = await new UpdateAccountHandler(_repository, Clock, _user)
            .HandleAsync(bank.Id, new UpdateAccountRequest("Bank", "Bank", "COP", 0m, null), default);

        Assert.Equal(HandlerResultKind.Invalid, result.Kind);
        Assert.True(result.Errors.ContainsKey("isArchived"));
    }

    [Fact]
    public async Task Delete_AccountWithTransactions_ReturnsConflictAndKeepsIt()
    {
        // AC-2 de HU-64.
        var cash = Seed("Cash");
        var bank = Seed("Bank");
        _repository.Transactions.Add(Transfer(bank.Id, cash.Id, 100m));

        var result = await new DeleteAccountHandler(_repository, _user).HandleAsync(cash.Id, default);

        Assert.Equal(HandlerResultKind.Conflict, result.Kind);
        Assert.Equal(DeleteAccountHandler.HasTransactionsCode, result.ConflictCode);
        Assert.Contains(cash, _repository.Items);
    }

    [Fact]
    public async Task Delete_UnusedAccount_RemovesIt()
    {
        var bank = Seed("Bank");

        var result = await new DeleteAccountHandler(_repository, _user).HandleAsync(bank.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Empty(_repository.Items);
    }

    [Fact]
    public async Task GetUpdateDelete_OtherOwnersAccount_ReturnNotFound()
    {
        var foreign = Seed("Bank", owner: "someone-else");

        Assert.Null(await new GetAccountHandler(_repository, _user).HandleAsync(foreign.Id, default));
        Assert.Equal(HandlerResultKind.NotFound, (await new UpdateAccountHandler(_repository, Clock, _user)
            .HandleAsync(foreign.Id, new UpdateAccountRequest("X", "Bank", "COP", 0m, false), default)).Kind);
        Assert.Equal(HandlerResultKind.NotFound, (await new DeleteAccountHandler(_repository, _user)
            .HandleAsync(foreign.Id, default)).Kind);
        Assert.Contains(foreign, _repository.Items);
    }

    private Account Seed(string name, string owner = TestCurrentUser.DefaultSubject, decimal opening = 0m)
    {
        var account = Account.Create(owner, name, AccountType.Bank, "COP", opening, Clock);
        _repository.Items.Add(account);
        return account;
    }

    private static readonly DateOnly Day = new(2026, 10, 20);

    private static Transaction Expense(Guid accountId, decimal amount) => Transaction.Create(
        TestCurrentUser.DefaultSubject, TransactionType.Expense, accountId, null, Guid.NewGuid(), amount, Day, null, null, Clock);

    private static Transaction Transfer(Guid from, Guid to, decimal amount) => Transaction.Create(
        TestCurrentUser.DefaultSubject, TransactionType.Transfer, from, to, null, amount, Day, null, null, Clock);
}
