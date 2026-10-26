using Davydov.Revit.Hosting.Scripting;
using Davydov.Revit.Hosting.Scripting.Execution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Hosting;

/// <summary>
///     Registers the Davydov.Revit.Hosting.Scripting module.
/// </summary>
[PublicAPI]
public static class ScriptingRegistration
{
    /// <summary>The configuration key that turns scripting on or off.</summary>
    public const string EnabledKey = "Scripting:Enabled";

    /// <param name="commands">The commands of <c>AddRevitCommands</c>.</param>
    extension(RevitCommandsBuilder commands)
    {
        /// <summary>
        ///     Adds the <see cref="ExecuteCode" /> command — C# compiled with Roslyn and run inside Revit — when scripting is on:
        ///     in the <c>Development</c> environment by default, otherwise with <c>Scripting:Enabled</c>.
        /// </summary>
        /// <param name="builder">The host application builder: its configuration decides, its services get the runner.</param>
        /// <returns>The <see cref="RevitCommandsBuilder" /> for chaining.</returns>
        /// <remarks>
        ///     Off, the command is not registered at all: no page and no AI client can reach it. Read once at startup — a changed
        ///     setting needs a Revit restart. Any code runs with the user's rights: turn it on only where that is wanted.
        /// </remarks>
        /// <example>
        ///     <code>
        ///     builder.AddRevitCommands(commands => commands
        ///         .FromAssemblyOf&lt;Application&gt;()
        ///         .AddScripting(builder));
        ///     </code>
        /// </example>
        public RevitCommandsBuilder AddScripting(IHostApplicationBuilder builder)
        {
            var enabled = builder.Configuration.GetValue<bool?>(EnabledKey) ?? builder.Environment.IsDevelopment();
            if (!enabled)
            {
                return commands;
            }

            builder.Services.AddSingleton<ScriptRunner>();
            return commands.FromTypes(typeof(ExecuteCode));
        }
    }
}
