using FlowTrim.Application.Categories;
using FlowTrim.Domain.Categories;
using Microsoft.AspNetCore.Http.Headers;

namespace FlowTrim.Api.Categories;

/// <summary>
/// Categorías personales (HU-65). Todas las rutas exigen sesión y solo ven datos del propietario: una categoría ajena
/// responde 404. Los conflictos de negocio responden 409 con <c>code</c>.
/// </summary>
public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/categories").RequireAuthorization();

        group.MapGet("", ListAsync).WithName("ListCategories");
        group.MapPost("", CreateAsync).WithName("CreateCategory");
        group.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateCategory");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteCategory");
    }

    // ?kind=Expense|Income, ?includeArchived=true. La primera lista de un usuario sin categorías crea el set por
    // defecto en el idioma de Accept-Language. 200 / 400 si la clase no es válida.
    private static async Task<IResult> ListAsync(
        HttpRequest request,
        IListCategoriesHandler handler,
        CancellationToken cancellationToken,
        string? kind,
        bool? includeArchived) =>
        (await handler.HandleAsync(kind, includeArchived ?? false, LanguageOf(request), cancellationToken))
            .ToHttpResult(Results.Ok);

    // 201 con la categoría creada, o 400 con errores por campo (color o ícono fuera de las listas incluidos).
    private static async Task<IResult> CreateAsync(
        CreateCategoryRequest request, ICreateCategoryHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken))
            .ToHttpResult(created => Results.Created($"/api/categories/{created.Id}", created));

    // 200 / 400 / 404 / 409 category_kind_locked.
    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateCategoryRequest request, IUpdateCategoryHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult(Results.Ok);

    // 204 / 404 / 409 category_in_use (se puede archivar en su lugar).
    private static async Task<IResult> DeleteAsync(Guid id, IDeleteCategoryHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToHttpResult(_ => Results.NoContent());

    // Español si el idioma preferido (mayor calidad) es "es" o "es-*"; inglés en cualquier otro caso.
    private static CategoryLanguage LanguageOf(HttpRequest request)
    {
        RequestHeaders headers = request.GetTypedHeaders();
        var preferred = headers.AcceptLanguage
            .OrderByDescending(language => language.Quality ?? 1)
            .Select(language => language.Value.Value)
            .FirstOrDefault();
        return preferred is not null
               && (preferred.Equals("es", StringComparison.OrdinalIgnoreCase)
                   || preferred.StartsWith("es-", StringComparison.OrdinalIgnoreCase))
            ? CategoryLanguage.Spanish
            : CategoryLanguage.English;
    }
}
