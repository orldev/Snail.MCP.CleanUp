using BenchmarkDotNet.Running;

BenchmarkSwitcher.FromAssembly(typeof(Snail.MCP.CleanUp.Benchmarks.Machine).Assembly).Run(args);
