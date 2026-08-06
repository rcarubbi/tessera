using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tessera.Domain.Ports;
using Tessera.Infrastructure.Data;
using Tessera.Infrastructure.Storage;

namespace Tessera.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddTesseraInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TesseraDb")
            ?? "Host=localhost;Port=5432;Database=tessera;Username=tessera;Password=tessera";

        services.AddDbContext<TesseraDbContext>(options =>
        {
            if (configuration.GetValue<bool>("Database:InMemory"))
            {
                options.UseInMemoryDatabase(configuration["Database:Name"] ?? "tessera")
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            }
            else
            {
                options.UseNpgsql(connectionString);
            }
        });

        var objectStoreRoot = configuration["ObjectStore:Root"]
            ?? Path.Combine(AppContext.BaseDirectory, "objects");

        services.AddSingleton<IObjectStore>(new FileSystemObjectStore(objectStoreRoot));

        return services;
    }
}
