using FlowTrim.Application.Abstractions;

namespace FlowTrim.Tests.Common;

/// <summary>Identidad controlada para pruebas de aplicación sin tokens reales.</summary>
public sealed class TestCurrentUser(string? subject = TestCurrentUser.DefaultSubject) : ICurrentUser
{
    /// <summary>Subject usado por defecto en las pruebas que no comparan propietarios.</summary>
    public const string DefaultSubject = "test-user";

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Subject);

    public string? Subject { get; } = subject;
}
