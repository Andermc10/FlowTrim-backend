using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlowTrim.Tests.Api;

/// <summary>
/// Esquema de autenticación falso para pruebas HTTP: toma el subject de una cabecera en lugar de un
/// token real. Sin cabecera la petición queda anónima, igual que sin bearer token.
/// </summary>
public sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";

    /// <summary>Cabecera con el subject del usuario simulado.</summary>
    public const string SubjectHeader = "X-Test-Subject";

    /// <summary>Cabecera que simula un token válido pero sin claim <c>sub</c>.</summary>
    public const string NoSubjectHeader = "X-Test-No-Subject";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>();

        if (Request.Headers.TryGetValue(SubjectHeader, out var subject))
        {
            claims.Add(new Claim("sub", subject.ToString()));
        }
        else if (!Request.Headers.ContainsKey(NoSubjectHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
