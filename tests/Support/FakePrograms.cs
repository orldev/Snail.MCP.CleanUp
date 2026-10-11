using Snail.MCP.CleanUp.Application.Ports;
using Snail.MCP.CleanUp.Domain;

namespace Snail.MCP.CleanUp.Tests.Support;

/// <summary>Outside programs that answer what the test says, and remember every call.</summary>
internal sealed class FakePrograms(Func<string, IReadOnlyList<string>, ProgramRun>? answer = null) : IPrograms
{
    public List<string> Calls { get; } = [];

    public Func<string, IReadOnlyList<string>, ProgramRun> Answer { get; set; } = answer ?? ((_, _) => new ProgramRun(0, string.Empty, string.Empty));

    public Task<ProgramRun> RunAsync(string program, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Calls.Add($"{program} {string.Join(' ', arguments)}");

        return Task.FromResult(Answer(program, arguments));
    }

    public static ProgramRun Said(string output) => new(0, output, string.Empty);

    public static ProgramRun Failed(string error = "") => new(1, string.Empty, error);
}
