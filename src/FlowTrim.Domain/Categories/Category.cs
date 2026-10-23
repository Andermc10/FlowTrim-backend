using FlowTrim.Domain.Common;

namespace FlowTrim.Domain.Categories;

/// <summary>
/// Categoría personal de gasto o ingreso (HU-65). El color y el ícono son claves de listas fijas: la interfaz los
/// traduce a tokens del tema y a SVG propios, así el usuario nunca aporta CSS ni SVG.
/// </summary>
public sealed class Category
{
    /// <summary>Longitud máxima del nombre.</summary>
    public const int NameMaxLength = 40;

    /// <summary>Mensaje del nombre repetido en la misma clase (sin distinguir mayúsculas, archivadas incluidas).</summary>
    public const string DuplicateNameMessage = "A category of this kind with this name already exists.";

    /// <summary>Colores admitidos; cada uno corresponde a un token del tema con contraste suficiente en ambos temas.</summary>
    public static readonly IReadOnlyList<string> Colors =
        ["slate", "red", "orange", "amber", "green", "teal", "blue", "indigo", "violet", "pink"];

    /// <summary>Íconos admitidos; cada uno es un SVG en línea del frontend.</summary>
    public static readonly IReadOnlyList<string> Icons =
    [
        "food", "groceries", "transport", "home", "utilities", "health", "education", "entertainment", "shopping",
        "travel", "gifts", "subscriptions", "salary", "freelance", "investments", "other",
    ];

    public Guid Id { get; private set; }
    public string OwnerId { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public CategoryKind Kind { get; private set; }
    public string Color { get; private set; } = null!;
    public string Icon { get; private set; } = null!;
    public bool IsArchived { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Constructor privado sin parámetros requerido por EF Core.
    private Category()
    {
    }

    /// <summary>Crea una categoría activa. Lanza <see cref="DomainValidationException"/> ante la primera invariante incumplida.</summary>
    public static Category Create(
        string? ownerId,
        string? name,
        CategoryKind kind,
        string? color,
        string? icon,
        TimeProvider timeProvider)
    {
        var validOwnerId = OwnerIdentity.Require(ownerId);
        var (validName, validColor, validIcon) = ValidateAndNormalize(name, kind, color, icon);

        var now = timeProvider.GetUtcNow();
        return new Category
        {
            Id = Guid.NewGuid(),
            OwnerId = validOwnerId,
            Name = validName,
            Kind = kind,
            Color = validColor,
            Icon = validIcon,
            IsArchived = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Actualiza los campos editables. Que la clase no cambie cuando ya hay movimientos lo decide el caso de uso.
    /// </summary>
    public void Update(
        string? name,
        CategoryKind kind,
        string? color,
        string? icon,
        bool isArchived,
        TimeProvider timeProvider)
    {
        var (validName, validColor, validIcon) = ValidateAndNormalize(name, kind, color, icon);

        Name = validName;
        Kind = kind;
        Color = validColor;
        Icon = validIcon;
        IsArchived = isArchived;
        UpdatedAt = timeProvider.GetUtcNow();
    }

    private static (string Name, string Color, string Icon) ValidateAndNormalize(
        string? name,
        CategoryKind kind,
        string? color,
        string? icon)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new DomainValidationException("name", "Name is required.");
        }

        if (name.Length > NameMaxLength)
        {
            throw new DomainValidationException("name", $"Name must be at most {NameMaxLength} characters.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new DomainValidationException("kind", "Category kind is not supported.");
        }

        // Las claves distinguen mayúsculas, como los enums de la API.
        color = color?.Trim() ?? string.Empty;
        if (!Colors.Contains(color))
        {
            throw new DomainValidationException("color", "Color is not supported.");
        }

        icon = icon?.Trim() ?? string.Empty;
        if (!Icons.Contains(icon))
        {
            throw new DomainValidationException("icon", "Icon is not supported.");
        }

        return (name, color, icon);
    }
}
