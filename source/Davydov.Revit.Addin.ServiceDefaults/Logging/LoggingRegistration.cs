using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Davydov.Revit.Addin.ServiceDefaults.Diagnostics;
using Nice3point.Revit.Logging;

namespace Davydov.Revit.Addin.ServiceDefaults.Logging;

/// <summary>
///     Provides extension methods for <see cref="IHostApplicationBuilder" /> to add the application logging defaults.
/// </summary>
/// <example>
///     <code lang="csharp">
///         public partial class Class(ILogger&lt;Class&gt; logger)
///         {
///             private void Execute()
///             {
///                 LogMessage(logger);
///             }
///
///             [LoggerMessage(LogLevel.Information, "Message")]
///             private static partial void LogMessage(ILogger&lt;Class&gt; logger);
///         }
///     </code>
/// </example>
public static class LoggingRegistration
{
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The host application builder.</param>
    extension<TBuilder>(TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        /// <summary>
        ///     Adds the default logging providers to the specified <see cref="IHostApplicationBuilder" />.
        /// </summary>
        /// <returns>The <typeparamref name="TBuilder" /> for chaining.</returns>
        /// <remarks>
        ///     The Revit journal keeps the whole session and may be sent to Autodesk with an error report,
        ///     so release builds write only warnings and errors there (plan, section 11).
        /// </remarks>
        public TBuilder AddLoggingDefaults()
        {
            var journalLevel = builder.Environment.IsDevelopment() ? LogLevel.Information : LogLevel.Warning;

            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = nameof(LogLevel.Debug),
                ["Logging:RevitJournal:LogLevel:Default"] = journalLevel.ToString()
            });

            builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
            builder.Logging.AddDebug();
            builder.Logging.AddRevitJournal(RevitApiContext.Application, options => options.ApplicationName = builder.Environment.ApplicationName);

            builder.Services.AddHostedService<AppDomainExceptionsHandler>();
            builder.Services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);

            return builder;
        }
    }
}
