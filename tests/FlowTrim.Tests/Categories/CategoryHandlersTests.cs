using FlowTrim.Application.Categories;
using FlowTrim.Application.Common;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Transactions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Categories;

/// <summary>Casos de uso de categorías (HU-65) y set por defecto con el repositorio en memoria.</summary>
public sealed class CategoryHandlersTests
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero));
    private readonly InMemoryCategoryRepository _repository = new();
    private readonly TestCurrentUser _user = new();

    [Fact]
    public void Defaults_HaveElevenExpenseAndThreeIncomeCategoriesWithAllowListedStyles()
    {
        var defaults = DefaultCategories.Create("owner", CategoryLanguage.English, Clock);

        Assert.Equal(11, defaults.Count(c => c.Kind == CategoryKind.Expense));
        Assert.Equal(["Salary", "Freelance", "Other income"], defaults.Where(c => c.Kind == CategoryKind.Income).Select(c => c.Name));
        Assert.All(defaults, c => Assert.Contains(c.Color, Category.Colors));
        Assert.Equal(defaults.Count, defaults.Select(c => (c.Kind, c.Name)).Distinct().Count());
    }

    [Fact]
    public void Defaults_InSpanish_UseSpanishNames()
    {
        var names = DefaultCategories.Create("owner", CategoryLanguage.Spanish, Clock).Select(c => c.Name).ToList();

        Assert.Contains("Comida", names);
        Assert.Contains("Educación", names);
        Assert.Contains("Otros ingresos", names);
        Assert.DoesNotContain("Food", names);
    }

    [Fact]
    public async Task List_NewUser_CreatesDefaultsOnceAndReloadingDoesNotDuplicate()
    {
        // AC-1 de HU-65.
        var handler = new ListCategoriesHandler(_repository, Clock, _user);

        var first = await handler.HandleAsync(null, false, CategoryLanguage.English, default);
        var second = await handler.HandleAsync(null, false, CategoryLanguage.English, default);

        Assert.Equal(14, first.Value!.Count);
        Assert.Equal(14, second.Value!.Count);
        Assert.Equal(1, _repository.DefaultsRequests);
        Assert.All(_repository.Items, c => Assert.Equal(TestCurrentUser.DefaultSubject, c.OwnerId));
    }

    [Fact]
    public async Task List_UserWithOnlyArchivedCategories_DoesNotRecreateDefaults()
    {
        var archived = Seed("Old", CategoryKind.Expense);
        archived.Update("Old", CategoryKind.Expense, "slate", "other", isArchived: true, Clock);

        var result = await new ListCategoriesHandler(_repository, Clock, _user)
            .HandleAsync(null, false, CategoryLanguage.English, default);

        Assert.Empty(result.Value!);
        Assert.Equal(0, _repository.DefaultsRequests);
    }

    [Fact]
    public async Task List_FilterByKind_ReturnsOnlyThatKind()
    {
        var result = await new ListCategoriesHandler(_repository, Clock, _user)
            .HandleAsync("Income", false, CategoryLanguage.English, default);

        Assert.Equal(3, result.Value!.Count);
        Assert.All(result.Value, c => Assert.Equal("Income", c.Kind));
    }

    [Fact]
    public async Task List_UnknownKind_ReturnsFieldError()
    {
        var result = await new ListCategoriesHandler(_repository, Clock, _user)
            .HandleAsync("income", false, CategoryLanguage.English, default);

        Assert.Equal(HandlerResultKind.Invalid, result.Kind);
        Assert.True(result.Errors.ContainsKey("kind"));
    }

    [Fact]
    public async Task Create_SameNameInOtherKind_IsAllowedButNotInSameKind()
    {
        Seed("Other", CategoryKind.Expense);
        var handler = new CreateCategoryHandler(_repository, Clock, _user);

        var income = await handler.HandleAsync(new CreateCategoryRequest("Other", "Income", "slate", "other"), default);
        var duplicate = await handler.HandleAsync(new CreateCategoryRequest(" OTHER ", "Expense", "slate", "other"), default);

        Assert.True(income.IsSuccess);
        Assert.Equal([Category.DuplicateNameMessage], duplicate.Errors["name"]);
    }

    [Fact]
    public async Task Update_KindOfUsedCategory_ReturnsConflict()
    {
        var food = Seed("Food", CategoryKind.Expense);
        _repository.Transactions.Add(ExpenseWith(food.Id));

        var result = await new UpdateCategoryHandler(_repository, Clock, _user)
            .HandleAsync(food.Id, new UpdateCategoryRequest("Food", "Income", "orange", "food", false), default);

        Assert.Equal(HandlerResultKind.Conflict, result.Kind);
        Assert.Equal(UpdateCategoryHandler.KindLockedCode, result.ConflictCode);
        Assert.Equal(CategoryKind.Expense, food.Kind);
    }

    [Fact]
    public async Task Update_UsedCategory_CanBeRenamedRestyledAndArchived()
    {
        var food = Seed("Food", CategoryKind.Expense);
        _repository.Transactions.Add(ExpenseWith(food.Id));

        var result = await new UpdateCategoryHandler(_repository, Clock, _user)
            .HandleAsync(food.Id, new UpdateCategoryRequest("Restaurants", "Expense", "red", "food", true), default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsArchived);
        Assert.True(result.Value.HasTransactions);
        Assert.Equal("red", food.Color);
    }

    [Fact]
    public async Task Delete_UsedCategory_ReturnsConflictAndUnusedIsDeleted()
    {
        // AC-3 de HU-65.
        var food = Seed("Food", CategoryKind.Expense);
        var gifts = Seed("Gifts", CategoryKind.Expense);
        _repository.Transactions.Add(ExpenseWith(food.Id));
        var handler = new DeleteCategoryHandler(_repository, _user);

        var used = await handler.HandleAsync(food.Id, default);
        var unused = await handler.HandleAsync(gifts.Id, default);

        Assert.Equal(DeleteCategoryHandler.InUseCode, used.ConflictCode);
        Assert.True(unused.IsSuccess);
        Assert.Equal([food], _repository.Items);
    }

    [Fact]
    public async Task UpdateDelete_OtherOwnersCategory_ReturnNotFound()
    {
        var foreign = Seed("Food", CategoryKind.Expense, owner: "someone-else");

        var update = await new UpdateCategoryHandler(_repository, Clock, _user)
            .HandleAsync(foreign.Id, new UpdateCategoryRequest("X", "Expense", "red", "food", false), default);
        var delete = await new DeleteCategoryHandler(_repository, _user).HandleAsync(foreign.Id, default);

        Assert.Equal(HandlerResultKind.NotFound, update.Kind);
        Assert.Equal(HandlerResultKind.NotFound, delete.Kind);
        Assert.Equal("Food", foreign.Name);
    }

    private Category Seed(string name, CategoryKind kind, string owner = TestCurrentUser.DefaultSubject)
    {
        var category = Category.Create(owner, name, kind, "orange", "food", Clock);
        _repository.Items.Add(category);
        return category;
    }

    private static Transaction ExpenseWith(Guid categoryId) => Transaction.Create(
        TestCurrentUser.DefaultSubject, TransactionType.Expense, Guid.NewGuid(), null, categoryId, 10m,
        new DateOnly(2026, 10, 20), null, null, Clock);
}
