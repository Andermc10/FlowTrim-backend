using FlowTrim.Application.Abstractions;

namespace FlowTrim.Tests.Common;

/// <summary>Identidad controlada para pruebas de aplicación sin tokens reales.</summary>
public sealed class TestCurrentUser(string subject = "test-user") : ICurrentUser
{
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Subject);

    public string? Subject { get; } = subject;
}
