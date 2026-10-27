using FlowTrim.Application.Goals;

namespace FlowTrim.Api.Goals;

/// <summary>
/// Metas de ahorro y sus aportes declarados (EP-15 HU-77). Exigen sesión y solo ven datos del propietario: una meta o un
/// aporte ajeno responde 404. Los conflictos responden 409 con <c>code</c>.
/// </summary>
public static class GoalEndpoints
{
    public static void MapGoalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/goals").RequireAuthorization();

        group.MapGet("", ListAsync).WithName("ListGoals");
        group.MapPost("", CreateAsync).WithName("CreateGoal");
        group.MapGet("/{id:guid}", GetAsync).WithName("GetGoal");
        group.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateGoal");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteGoal");
        group.MapPost("/{id:guid}/contributions", AddContributionAsync).WithName("AddGoalContribution");
        group.MapDelete("/{id:guid}/contributions/{contributionId:guid}", DeleteContributionAsync).WithName("DeleteGoalContribution");
    }

    private static async Task<IResult> ListAsync(IListGoalsHandler handler, CancellationToken cancellationToken) =>
        Results.Ok(await handler.HandleAsync(cancellationToken));

    // 201 / 400.
    private static async Task<IResult> CreateAsync(GoalRequest request, ICreateGoalHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToHttpResult(created => Results.Created($"/api/goals/{created.Id}", created));

    // Meta con sus últimos 50 aportes. 200 / 404.
    private static async Task<IResult> GetAsync(Guid id, IGetGoalHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(id, cancellationToken) is { } goal ? Results.Ok(goal) : ProblemResults.NotFound();

    // 200 / 400 / 404 / 409 goal_currency_locked.
    private static async Task<IResult> UpdateAsync(
        Guid id, GoalRequest request, IUpdateGoalHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult(Results.Ok);

    // Borra la meta y sus aportes. 204 / 404.
    private static async Task<IResult> DeleteAsync(Guid id, IDeleteGoalHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToHttpResult(_ => Results.NoContent());

    // Aporte (positivo) o retiro (negativo). 201 / 400 / 404.
    private static async Task<IResult> AddContributionAsync(
        Guid id, ContributionRequest request, IAddContributionHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken))
            .ToHttpResult(created => Results.Created($"/api/goals/{id}/contributions/{created.Id}", created));

    // 204 / 404 / 409 goal_saved_negative.
    private static async Task<IResult> DeleteContributionAsync(
        Guid id, Guid contributionId, IDeleteContributionHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, contributionId, cancellationToken)).ToHttpResult(_ => Results.NoContent());
}
