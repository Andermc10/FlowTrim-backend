using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FlowTrim.Domain.Accounts;
using FlowTrim.Domain.Categories;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Api;

/// <summary>EP-16 HU-79 por HTTP: vista previa, elecciones inválidas, confirmación y reimportación sin duplicar.</summary>
public sealed class TransactionImportApiTests : IClassFixture<FlowTrimApiFactory>
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(FlowTrimApiFactory.DefaultNow);
    private const string File = "date,description,amount,category\n2026-10-05,Exito,-45000.50,Food\n2026-10-06,Payroll,2500000,\n2026-10-07,Bad,abc,\n";

    private readonly FlowTrimApiFactory _factory;

    public TransactionImportApiTests(FlowTrimApiFactory factory) => _factory = factory;

    [Fact]
    public async Task PreviewThenConfirm_CreatesValidRows_AndASecondConfirmCreatesNothing()
    {
        var (client, bank, other, food, salary) = Seed();

        var preview = await PostAsync(client, "preview", File, bank.Id, other.Id, salary.Id);
        var first = await PostAsync(client, "confirm", File, bank.Id, other.Id, salary.Id);
        var second = await PostAsync(client, "confirm", File, bank.Id, other.Id, salary.Id);

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var body = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("summary").GetProperty("valid").GetInt32());
        var rows = body.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(food.Id, rows[0].GetProperty("values").GetProperty("categoryId").GetGuid());
        Assert.Equal("invalid_amount", rows[2].GetProperty("errors")[0].GetProperty("code").GetString());

        var created = await first.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((3, 2, 1, 0), (created.GetProperty("total").GetInt32(), created.GetProperty("created").GetInt32(),
            created.GetProperty("invalid").GetInt32(), created.GetProperty("duplicate").GetInt32()));
        var again = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((0, 2), (again.GetProperty("created").GetInt32(), again.GetProperty("duplicate").GetInt32()));

        var account = await client.GetFromJsonAsync<JsonElement>($"/api/accounts/{bank.Id}");
        Assert.Equal(1000m - 45000.50m + 2500000m, account.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task Choices_ForeignAccountOrWrongKindCategories_Return400OnEachField()
    {
        var (client, _, other, _, salary) = Seed();
        var (_, foreignBank, _, _, _) = Seed();

        var response = await PostAsync(client, "preview", File, foreignBank.Id, salary.Id, other.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("accountId", out _));
        Assert.True(errors.TryGetProperty("expenseCategoryId", out _));
        Assert.True(errors.TryGetProperty("incomeCategoryId", out _));
    }

    [Fact]
    public async Task FileProblems_UseTheImportCodes()
    {
        var (client, bank, other, _, salary) = Seed();

        var wrongType = await PostAsync(client, "preview", File, bank.Id, other.Id, salary.Id, fileName: "bank.txt");
        var noFile = await client.PostAsync("/api/transaction-imports/preview", new MultipartFormDataContent
        {
            { new StringContent(bank.Id.ToString()), "accountId" },
            { new StringContent(other.Id.ToString()), "expenseCategoryId" },
            { new StringContent(salary.Id.ToString()), "incomeCategoryId" },
        });

        Assert.Equal("unsupported_file_type", (await wrongType.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal("file_required", (await noFile.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    private (HttpClient Client, Account Bank, Category Other, Category Food, Category Salary) Seed()
    {
        var owner = $"auth0|bank-import-{Guid.NewGuid():N}";
        var bank = Account.Create(owner, "Bank", AccountType.Bank, "COP", 1000m, Clock);
        var other = Category.Create(owner, "Other", CategoryKind.Expense, "slate", "other", Clock);
        var food = Category.Create(owner, "Food", CategoryKind.Expense, "orange", "food", Clock);
        var salary = Category.Create(owner, "Salary", CategoryKind.Income, "green", "salary", Clock);
        _factory.Accounts.Items.Add(bank);
        _factory.Categories.Items.AddRange([other, food, salary]);
        return (_factory.CreateClientFor(owner), bank, other, food, salary);
    }

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client, string action, string csv, Guid accountId, Guid expenseId, Guid incomeId, string fileName = "bank.csv")
    {
        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", fileName },
            { new StringContent(accountId.ToString()), "accountId" },
            { new StringContent(expenseId.ToString()), "expenseCategoryId" },
            { new StringContent(incomeId.ToString()), "incomeCategoryId" },
        };
        return client.PostAsync($"/api/transaction-imports/{action}", form);
    }
}
