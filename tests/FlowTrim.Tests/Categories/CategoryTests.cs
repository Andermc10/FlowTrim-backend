using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Common;
using FlowTrim.Tests.Common;

namespace FlowTrim.Tests.Categories;

/// <summary>Invariantes y listas fijas de <see cref="Category"/> (EP-12 D4; los casos de uso llegan con HU-65).</summary>
public sealed class CategoryTests
{
    private static readonly TimeProvider Clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Create_AllowListedColorAndIcon_Succeeds()
    {
        var category = Category.Create("owner-a", " Food ", CategoryKind.Expense, "orange", "food", Clock);

        Assert.Equal("Food", category.Name);
        Assert.Equal("orange", category.Color);
        Assert.Equal("food", category.Icon);
    }

    [Theory]
    [InlineData("magenta", "food", "color")]
    [InlineData("Orange", "food", "color")]
    [InlineData("orange", "rocket", "icon")]
    [InlineData("orange", null, "icon")]
    public void Create_ValueOutsideAllowList_ThrowsForField(string? color, string? icon, string field)
    {
        var exception = Assert.Throws<DomainValidationException>(() =>
            Category.Create("owner-a", "Food", CategoryKind.Expense, color, icon, Clock));

        Assert.Equal(field, exception.PropertyName);
    }

    [Fact]
    public void AllowLists_HaveTheSpecifiedSizes()
    {
        Assert.Equal(10, Category.Colors.Count);
        Assert.Equal(16, Category.Icons.Count);
    }
}
