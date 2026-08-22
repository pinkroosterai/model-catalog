using Microsoft.Extensions.Logging.Console;

namespace ModelCatalog.Service.Observability;

public static class EstateLogging
{
    /// <summary>
    /// Structured JSON to stdout, to the field contract in architecture-guideline.md §7. The
    /// collector reads Docker's log stream, so this process knows nothing about the backend and
    /// the backend can be replaced without touching it.
    /// </summary>
    public static ILoggingBuilder AddEstateJsonLogging(
        this ILoggingBuilder logging,
        IConfiguration configuration,
        string service
    )
    {
        ArgumentNullException.ThrowIfNull(logging);
        ArgumentNullException.ThrowIfNull(configuration);

        logging.ClearProviders();
        logging.Services.Configure<JsonLogOptions>(options =>
        {
            options.Service = service;
            configuration.GetSection("Logging:Estate").Bind(options);
        });
        logging.Services.AddSingleton<ConsoleFormatter, JsonLogFormatter>();
        logging.AddConsole(console => console.FormatterName = JsonLogFormatter.FormatterName);
        return logging;
    }
}
