using Serilog;
using SismoPC.Service;

var dataDir = SismoPaths.DataDirectory;
Directory.CreateDirectory(Path.Combine(dataDir, "logs"));

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "SismoPC");
builder.Services.AddSerilog((_, logger) => logger
    .MinimumLevel.Information()
    .WriteTo.File(
        Path.Combine(dataDir, "logs", "service-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        formatProvider: System.Globalization.CultureInfo.InvariantCulture));
builder.Services.AddHostedService<CollectionWorker>();

await builder.Build().RunAsync().ConfigureAwait(false);
