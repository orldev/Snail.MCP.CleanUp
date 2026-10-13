using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Domain;

namespace Snail.MCP.CleanUp.Tests.Adapters;

/// <summary>What <c>dotnet build-server shutdown</c> stops and what editors start, told apart from a build or a program of its own.</summary>
public sealed class SharedServersTests
{
    private static RunningProcess Process(string command, string? executable = null, string? name = null) =>
        new(10, 1, DateTimeOffset.UnixEpoch, command) { Executable = executable, Name = name };

    [Theory]
    [InlineData("/Users/me/.dotnet/sdk/10.0.302/Roslyn/bincore/VBCSCompiler -pipename:abc")]
    [InlineData("dotnet /Users/me/.dotnet/sdk/10.0.302/Roslyn/bincore/VBCSCompiler.dll -pipename:abc")]
    [InlineData("/Users/me/.dotnet/dotnet /Users/me/.dotnet/sdk/10.0.302/MSBuild.dll /noautoresponse /nologo /nodemode:1 /nodeReuse:true /low:false")]
    [InlineData("/Users/me/.dotnet/dotnet /Users/me/.dotnet/sdk/10.0.302/MSBuild.dll -nologo -nodemode:8")]
    [InlineData("dotnet /Users/me/.dotnet/sdk/10.0.302/Sdks/Microsoft.NET.Sdk.Razor/tools/rzc.dll server -p abc")]
    [InlineData("/Users/me/.vscode/extensions/ms-dotnettools.csharp-2.90/.roslyn/Microsoft.CodeAnalysis.LanguageServer --logLevel Information")]
    [InlineData("dotnet /Users/me/.vscode/extensions/ms-dotnettools.csharp-2.90/.roslyn/Microsoft.CodeAnalysis.LanguageServer.dll --logLevel Information")]
    [InlineData("dotnet /x/.roslyn/BuildHost-netcore/Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll --pipe y")]
    [InlineData("/Users/me/.vscode/extensions/ms-dotnettools.csharp-2.90/.razor/rzls --logLevel 2")]
    [InlineData("/Users/me/.omnisharp/OmniSharp -s /work")]
    [InlineData("/Users/me/Applications/Rider.app/Contents/lib/ReSharperHost/macos-arm64/Rider.Backend --x")]
    public void BuildAndEditorServers_AreShared(string command) =>
        Assert.True(SharedServers.Is(Process(command, name: Path.GetFileName(command.Split(' ')[0]))));

    [Theory]
    [InlineData("dotnet /Users/me/.dotnet/sdk/10.0.302/MSBuild.dll -restore /work/App.csproj")]
    [InlineData("dotnet build")]
    [InlineData("node server.js")]
    [InlineData("/usr/bin/python3 -m http.server 8000")]
    [InlineData("dotnet /work/bin/Debug/net10.0/Server.dll")]
    public void ABuildOrAProgramOfItsOwn_IsNot(string command) =>
        Assert.False(SharedServers.Is(Process(command, name: Path.GetFileName(command.Split(' ')[0]))));
}
