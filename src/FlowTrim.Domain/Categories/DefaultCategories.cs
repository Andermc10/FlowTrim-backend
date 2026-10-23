namespace FlowTrim.Domain.Categories;

/// <summary>Idioma de los nombres de las categorías por defecto.</summary>
public enum CategoryLanguage
{
    English,
    Spanish,
}

/// <summary>
/// Set inicial de categorías (HU-65 FR-2): se crea una sola vez, cuando el usuario aún no tiene ninguna, en el idioma de
/// la petición. Después son categorías normales que el usuario puede editar, archivar o borrar.
/// </summary>
public static class DefaultCategories
{
    private sealed record Definition(CategoryKind Kind, string Color, string Icon, string English, string Spanish);

    private static readonly Definition[] Definitions =
    [
        new(CategoryKind.Expense, "orange", "food", "Food", "Comida"),
        new(CategoryKind.Expense, "green", "groceries", "Groceries", "Mercado"),
        new(CategoryKind.Expense, "blue", "transport", "Transport", "Transporte"),
        new(CategoryKind.Expense, "indigo", "home", "Home", "Hogar"),
        new(CategoryKind.Expense, "amber", "utilities", "Utilities", "Servicios"),
        new(CategoryKind.Expense, "red", "health", "Health", "Salud"),
        new(CategoryKind.Expense, "violet", "education", "Education", "Educación"),
        new(CategoryKind.Expense, "pink", "entertainment", "Entertainment", "Entretenimiento"),
        new(CategoryKind.Expense, "teal", "shopping", "Shopping", "Compras"),
        new(CategoryKind.Expense, "indigo", "subscriptions", "Subscriptions", "Suscripciones"),
        new(CategoryKind.Expense, "slate", "other", "Other", "Otros"),
        new(CategoryKind.Income, "green", "salary", "Salary", "Salario"),
        new(CategoryKind.Income, "teal", "freelance", "Freelance", "Independiente"),
        new(CategoryKind.Income, "slate", "other", "Other income", "Otros ingresos"),
    ];

    /// <summary>Crea las categorías por defecto del propietario (11 de gasto y 3 de ingreso).</summary>
    public static IReadOnlyList<Category> Create(string ownerId, CategoryLanguage language, TimeProvider timeProvider) =>
        Definitions
            .Select(definition => Category.Create(
                ownerId,
                language == CategoryLanguage.Spanish ? definition.Spanish : definition.English,
                definition.Kind,
                definition.Color,
                definition.Icon,
                timeProvider))
            .ToList();
}
