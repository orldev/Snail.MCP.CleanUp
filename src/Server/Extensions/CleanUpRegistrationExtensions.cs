using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.CleanUp.Adapters;
using Snail.MCP.CleanUp.Adapters.Resources;
using Snail.MCP.CleanUp.Adapters.Trails;
using Snail.MCP.CleanUp.Application;
using Snail.MCP.CleanUp.Configuration;
using Snail.MCP.CleanUp.Adapters.Ledger;
using Snail.MCP.CleanUp.Core.Adapters;
using Snail.MCP.CleanUp.Core.Application;

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

            return services
                .AddMachine()
                .AddLedger()
                .AddAccount();
        }

        /// <summary>How this machine is looked at: its programs, its processes and the sessions of its agents.</summary>
        internal IServiceCollection AddMachine()
        {
            services.AddSingleton<IPrograms, Programs>();
            services.AddSingleton<IProcessTable, ProcessTable>();
            services.AddSingleton<ISessions, MachineSessions>();
            services.AddSingleton<Disposal>();
            services.AddSingleton<FileTrail>();
            services.AddSingleton<ITrail>(provider => provider.GetRequiredService<FileTrail>());
            services.AddSingleton<ITrail, ProcessTrail>();
            services.AddSingleton<ITrail, PortTrail>();
            services.AddSingleton<ITrail, CrontabTrail>();
            services.AddSingleton<IResourceKind, ProcessKind>();
            services.AddSingleton<IResourceKind, FileKind>();
            services.AddSingleton<IResourceKind, SimulatorKind>();
            services.AddSingleton<ITrail, SimulatorTrail>();
            services.AddSingleton<Android>();
            services.AddSingleton<IResourceKind, EmulatorKind>();
            services.AddSingleton<IResourceKind, VirtualDeviceKind>();
            services.AddSingleton<IResourceKind, ForwardKind>();
            services.AddSingleton<Docker>();
            services.AddSingleton<IResourceKind, ContainerKind>();
            services.AddSingleton<IResourceKind, NetworkKind>();
            services.AddSingleton<IResourceKind, VolumeKind>();
            services.AddSingleton<IResourceKind, ImageKind>();
            services.AddSingleton<IResourceKind, BuildCacheKind>();
            services.AddSingleton<ITrail, DockerTrail>();
            services.AddSingleton<IResourceKind, EditorKind>();
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
            services.AddSingleton<IDiscoveries, SqliteDiscoveries>();
            services.AddSingleton<IRules>(provider => new JsonRules(
                Path.Combine(provider.GetRequiredService<ServerConfig>().DataDirectory, "rules.json"),
                MacPlaces.Shipped));

            return services;
        }

        /// <summary>Taking stock, deciding and carrying out.</summary>
        internal IServiceCollection AddAccount()
        {
            services.AddSingleton<Census>();
            services.AddSingleton<Cleaner>();
            services.AddSingleton<Health>();
            services.AddSingleton<Runs>();
            services.AddSingleton<Leases>();
            services.AddSingleton<Account>();
            services.AddSingleton<Hooks>();
            services.AddSingleton<HookInstallation>();
            services.AddSingleton<Snail.MCP.CleanUp.Tools.ToolIndex>();
            services.AddHostedService<Watch>();

            return services;
        }
    }
}
