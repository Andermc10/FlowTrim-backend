using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Transactions;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Api;

/// <summary>Contrato HTTP de las categorías (HU-65): set por defecto por idioma, listas fijas, 409 y aislamiento.</summary>
public sealed class CategoriesApiTests : IClassFixture<FlowTrimApiFactory>
{
    private const string OwnerA = "auth0|owner-a";
    private const string OwnerB = "auth0|owner-b";
    private static readonly TimeProvider Clock = new FixedTimeProvider(FlowTrimApiFactory.DefaultNow);

    private readonly FlowTrimApiFactory _factory;

    public CategoriesApiTests(FlowTrimApiFactory factory)
    {
        _factory = factory;
        _factory.Categories.Items.Clear();
        _factory.Accounts.Transactions.Clear();
    }

    [Theory]
    [InlineData("es-CO,es;q=0.9,en;q=0.8", "Comida")]
    [InlineData("es", "Comida")]
    [InlineData("en-US,en;q=0.9,es;q=0.8", "Food")]
    [InlineData(null, "Food")]
    public async Task List_FirstTime_CreatesDefaultsInRequestLanguage(string? acceptLanguage, string expected)
    {
        var client = _factory.CreateClientFor(OwnerA);
        if (acceptLanguage is not null)
        {
            client.DefaultRequestHeaders.Add("Accept-Language", acceptLanguage);
        }

        var body = await GetJsonAsync(client, "/api/categories?kind=Expense");

        var names = body.EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();
        Assert.Contains(expected, names);
        Assert.Equal(11, names.Count);
    }

    [Fact]
    public async Task List_UnknownKind_Returns400()
    {
        var response = await _factory.CreateClientFor(OwnerA).GetAsync("/api/categories?kind=Savings");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ColorOrIconOutsideAllowList_Returns400OnField()
    {
        // AC-2 de HU-65.
        var client = _factory.CreateClientFor(OwnerA);

        var color = await client.PostAsJsonAsync("/api/categories", new { name = "Pets", kind = "Expense", color = "#ff0000", icon = "other" });
        var icon = await client.PostAsJsonAsync("/api/categories", new { name = "Pets", kind = "Expense", color = "teal", icon = "<svg>" });

        Assert.Equal(HttpStatusCode.BadRequest, color.StatusCode);
        Assert.True((await color.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("color", out _));
        Assert.Equal(HttpStatusCode.BadRequest, icon.StatusCode);
        Assert.True((await icon.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("icon", out _));
    }

    [Fact]
    public async Task Create_Valid_Returns201()
    {
        var response = await _factory.CreateClientFor(OwnerA).PostAsJsonAsync(
            "/api/categories", new { name = "Pets", kind = "Expense", color = "teal", icon = "other" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal($"/api/categories/{body.GetProperty("id").GetGuid()}", response.Headers.Location?.OriginalString);
        Assert.False(body.GetProperty("hasTransactions").GetBoolean());
    }

    [Fact]
    public async Task UsedCategory_DeleteAndKindChange_Return409WithCodes()
    {
        var food = Seed(OwnerA, "Food");
        _factory.Accounts.Transactions.Add(Transaction.Create(
            OwnerA, TransactionType.Expense, Guid.NewGuid(), null, food.Id, 10m, new DateOnly(2026, 10, 20), null, null, Clock));
        var client = _factory.CreateClientFor(OwnerA);

        var delete = await client.DeleteAsync($"/api/categories/{food.Id}");
        var put = await client.PutAsJsonAsync($"/api/categories/{food.Id}", new
        {
            name = "Food", kind = "Income", color = "orange", icon = "food", isArchived = false,
        });
        var list = await GetJsonAsync(client, "/api/categories");

        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
        Assert.Equal("category_in_use", (await delete.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, put.StatusCode);
        Assert.Equal("category_kind_locked", (await put.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.True(Assert.Single(list.EnumerateArray()).GetProperty("hasTransactions").GetBoolean());
    }

    [Fact]
    public async Task OtherOwnersCategory_IsHiddenAndAnswers404()
    {
        var foreign = Seed(OwnerB, "Owner B food");
        Seed(OwnerA, "Mine");
        var client = _factory.CreateClientFor(OwnerA);

        var put = await client.PutAsJsonAsync($"/api/categories/{foreign.Id}", new
        {
            name = "Changed", kind = "Expense", color = "orange", icon = "food", isArchived = false,
        });
        var delete = await client.DeleteAsync($"/api/categories/{foreign.Id}");
        var list = await GetJsonAsync(client, "/api/categories?includeArchived=true");

        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(["Mine"], list.EnumerateArray().Select(c => c.GetProperty("name").GetString()));
        Assert.Equal("Owner B food", foreign.Name);
    }

    private Category Seed(string owner, string name)
    {
        var category = Category.Create(owner, name, CategoryKind.Expense, "orange", "food", Clock);
        _factory.Categories.Items.Add(category);
        return category;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
