using System.Security.Claims;
using FlowTrim.Application.Abstractions;

namespace FlowTrim.Api.Authentication;

/// <summary>Expone a la aplicación únicamente el subject estable del token actual.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal User => httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsAuthenticated => User.Identity?.IsAuthenticated == true;

    // Con MapInboundClaims = false el claim conserva su nombre JWT original ("sub").
    public string? Subject => User.FindFirstValue(JwtAuthenticationOptions.SubjectClaimType);
}
