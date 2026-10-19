using FlowTrim.Application.Insights;
using FlowTrim.Infrastructure.Insights;

namespace FlowTrim.Tests.Insights;

public sealed class FakeChatModelTests
{
    private static readonly ChatTool[] Tools =
    [
        new("get_summary", "Monthly totals.", "{}"),
        new("simulate_cancellation", "Savings if cancelled.", "{}"),
        new("get_upcoming_charges", "Next charges.", "{}"),
    ];

    private static Task<ChatCompletion> Ask(string question, params ChatMessage[] extra) =>
        new FakeChatModel().CompleteAsync(new ChatRequest([ChatMessage.User(question), .. extra], Tools), CancellationToken.None);

    [Theory]
    [InlineData("How much would I save if I cancel Netflix?", "simulate_cancellation")]
    [InlineData("¿Cuánto ahorro si cancelo Netflix?", "simulate_cancellation")]
    [InlineData("What are my upcoming charges?", "get_upcoming_charges")]
    [InlineData("¿Cuánto gasto al mes?", "get_summary")]
    public async Task Question_RequestsTheMatchingTool(string question, string tool)
    {
        var completion = await Ask(question);

        var call = Assert.Single(completion.ToolCalls);
        Assert.Equal(tool, call.Name);
        Assert.Equal("{}", call.ArgumentsJson);
        Assert.Equal("tool_calls", completion.FinishReason);
    }

    [Fact]
    public async Task ToolResult_IsExplainedByQuotingItVerbatim()
    {
        var completion = await Ask("How much do I spend?",
            ChatMessage.Assistant([new ChatToolCall("fake-call-1", "get_summary", "{}")]),
            ChatMessage.ToolResult("fake-call-1", "{\"COP\":38900}"));

        Assert.Equal(FakeChatModel.ExplanationPrefix + "{\"COP\":38900}", completion.Text);
        Assert.False(completion.HasToolCalls);
    }

    [Fact]
    public async Task UnrelatedQuestion_GetsTheOutOfScopeAnswer()
    {
        var completion = await Ask("Write me a poem about the sea");

        Assert.Equal(FakeChatModel.OutOfScopeAnswer, completion.Text);
    }

    [Fact]
    public async Task SameInput_GivesSameOutput()
    {
        var first = await Ask("¿Cuánto ahorro si cancelo Netflix?");
        var second = await Ask("¿Cuánto ahorro si cancelo Netflix?");

        Assert.Equal(first.ToolCalls, second.ToolCalls);
    }
}
