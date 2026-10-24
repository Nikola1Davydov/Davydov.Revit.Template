using Microsoft.Extensions.DependencyInjection;
using Davydov.Revit.Addin.ServiceDefaults.Logging;
using Davydov.Revit.Addin.ServiceDefaults.Serialization;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Hosting;

/// <summary>
///     Provides extension methods for <see cref="IHostApplicationBuilder" /> to add the defaults shared by the projects of the add-in.
/// </summary>
[PublicAPI]
public static class ServiceDefaultsRegistration
{
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder.</param>
    extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        /// <summary>
        ///     Adds the shared logging, diagnostics, serialization and container defaults to the specified <see cref="IHostApplicationBuilder" />.
        /// </summary>
        /// <returns>The <typeparamref name="TBuilder" /> for chaining.</returns>
        public TBuilder AddServiceDefaults()
        {
            builder.AddLoggingDefaults();
            builder.ConfigureJsonSerializer();
            builder.ConfigureContainerValidation();

            return builder;
        }

        /// <summary>
        ///     Validates the service registrations when the host is built.
        /// </summary>
        /// <returns>The <typeparamref name="TBuilder" /> for chaining.</returns>
        /// <remarks>
        ///     <see cref="HostApplicationBuilderSettings.DisableDefaults" /> turns the validation off,
        ///     so a missing registration would surface only on the first button click without this call.
        /// </remarks>
        public TBuilder ConfigureContainerValidation()
        {
            builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            }));

            return builder;
        }
    }
}
