using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Ledger;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Adapters.Trails;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Configuration;
using Snail.Toolkit.Cleanup.Adapters;
using Snail.Toolkit.Cleanup.Application;

namespace Snail.MCP.CleanUp.Extensions;

/// <summary>The account of what agents left behind, and the ways of looking at it.</summary>
public static class CleanUpRegistrationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Everything the tools delegate to.</summary>
        public IServiceCollection AddCleanUp()
        {
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton<Performance>();

            return services
                .AddMachine()
                .AddLedger()
                .AddAccount();
        }

        /// <summary>How this machine is looked at: its programs, its processes and the sessions of its agents.</summary>
        internal IServiceCollection AddMachine()
        {
            services.AddSingleton<IPrograms, Programs>();
            services.AddSingleton<IProcessTable>(provider => OperatingSystem.IsMacOS()
                ? new Adapters.Native.NativeProcessTable(provider.GetRequiredService<TimeProvider>())
                : new ProcessTable(provider.GetRequiredService<IPrograms>(), provider.GetRequiredService<TimeProvider>()));
            services.AddSingleton<IHolders, MachineSessions>();
            services.AddSingleton<Disposal>();
            services.AddSingleton<Sizes>();
            services.AddSingleton<FileTrail>();
            services.AddSingleton<ITrail>(provider => provider.GetRequiredService<FileTrail>());
            services.AddSingleton<ITrail, ProcessTrail>();
            services.AddSingleton<ITrail, PortTrail>();
            services.AddSingleton<ITrail, CrontabTrail>();
            services.AddSingleton<ProcessKind>();
            services.AddSingleton<IResourceKind>(provider => provider.GetRequiredService<ProcessKind>());
            services.AddSingleton<IResourceKind, McpKind>();
            services.AddSingleton<IResourceKind, FileKind>();
            services.AddSingleton<IResourceKind, RemainsKind>();
            services.AddSingleton<IResourceKind, OutsideKind>();
            services.AddSingleton<IResourceKind, SimulatorKind>();
            services.AddSingleton<ITrail, SimulatorTrail>();
            services.AddSingleton<Android>();
            services.AddSingleton<IResourceKind, EmulatorKind>();
            services.AddSingleton<IResourceKind, VirtualDeviceKind>();
            services.AddSingleton<IResourceKind, ForwardKind>();
            services.AddSingleton<Docker>();
            services.AddSingleton<IDockerEngine>(provider => new DockerSocket(provider.GetRequiredService<TimeProvider>()));
            services.AddSingleton<IResourceKind, ContainerKind>();
            services.AddSingleton<IResourceKind, NetworkKind>();
            services.AddSingleton<IResourceKind, VolumeKind>();
            services.AddSingleton<IResourceKind, ImageKind>();
            services.AddSingleton<IResourceKind, BuildCacheKind>();
            services.AddSingleton<IResourceKind, DanglingImageKind>();
            services.AddSingleton<IResourceKind, BuildHistoryKind>();
            services.AddSingleton<ITrail, DockerTrail>();
            services.AddSingleton<IResourceKind, EditorKind>();
            services.AddSingleton<IResourceKind, TestResultsKind>();
            services.AddSingleton<IResourceKind, WorktreeKind>();
            services.AddSingleton<ITrail, CertificateTrail>();

            return services;
        }

        /// <summary>The ledger every session's server shares, with the rules it learned and what it discovered.</summary>
        internal IServiceCollection AddLedger()
        {
            services.AddSingleton(provider => new LedgerFile(Path.Combine(provider.GetRequiredService<ServerConfig>().DataDirectory, "ledger.db")));
            services.AddSingleton<ILedger, SqliteLedger>();
            services.AddSingleton<IPlans, SqlitePlans>();
            services.AddSingleton<IBaselines, SqliteBaselines>();
            services.AddSingleton<IShifts, SqliteShifts>();
            services.AddSingleton<IDiscoveries, SqliteDiscoveries>();
            services.AddSingleton<IDiskHistory, SqliteDiskHistory>();
            services.AddSingleton<IRules>(provider => new JsonRules(
                Path.Combine(provider.GetRequiredService<ServerConfig>().DataDirectory, "rules.json"),
                MacPlaces.Shipped));

            return services;
        }

        /// <summary>Taking stock, deciding and carrying out.</summary>
        internal IServiceCollection AddAccount()
        {
            services.AddSingleton<Census>();
            services.AddSingleton<IVolume, HomeVolume>();
            services.AddSingleton<DiskTrend>();
            services.AddSingleton<MeasuredCensus>();
            services.AddSingleton<Cleaner>();
            services.AddSingleton<Health>();
            services.AddSingleton<Runs>();
            services.AddSingleton<Leases>();
            services.AddSingleton<Account>();
            services.AddSingleton<Hooks>();
            services.AddSingleton<HookInstallation>();
            services.AddSingleton<Snail.MCP.CleanUp.Tools.ToolIndex>();
            services.AddSingleton<IChanges>(provider => OperatingSystem.IsMacOS()
                ? new Adapters.Native.KernelChanges(provider.GetRequiredService<FileTrail>())
                : new TimedChanges(provider.GetRequiredService<TimeProvider>()));
            services.AddHostedService<Watch>();

            return services;
        }
    }
}
