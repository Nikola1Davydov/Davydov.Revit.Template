using Build.Modules;
using Build.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPipelines;
using ModularPipelines.Extensions;

var builder = Pipeline.CreateBuilder();

builder.Configuration.AddJsonFile("appsettings.json");
builder.Configuration.AddUserSecrets<Program>();
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddOptions<BuildOptions>().Bind(builder.Configuration.GetSection("Build")).ValidateDataAnnotations();

builder.Services.AddModule<CompileClientAppModule>();
builder.Services.AddModule<CompileProjectModule>();

// Our tests only: the template of phase 6 leaves this block and the Test modules out (plan, phase 6)
if (args.Contains("test"))
{
    builder.Services.AddModule<TestProjectModule>();
    builder.Services.AddModule<TestRevitModule>();
    builder.Services.AddModule<TestRevitBridgeModule>();
}

await builder.Build().RunAsync();
