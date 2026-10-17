using System.Security.Claims;
using FlowTrim.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace FlowTrim.Api.Authentication;

/// <summary>Expone a la aplicación únicamente el subject estable del token actual.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal User => httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsAuthenticated => User.Identity?.IsAuthenticated == true;

    public string? Subject =>
        User.FindFirstValue("sub") ??
        User.FindFirstValue(ClaimTypes.NameIdentifier);
}
