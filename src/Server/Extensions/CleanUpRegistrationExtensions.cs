using Microsoft.Extensions.DependencyInjection;
using Snail.MCP.CleanUp.Application;

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
            services.AddSingleton<Health>();

            return services;
        }
    }
}
