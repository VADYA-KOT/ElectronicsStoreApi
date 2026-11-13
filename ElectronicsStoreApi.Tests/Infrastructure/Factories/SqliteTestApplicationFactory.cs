
using System.Collections.Generic;
using ElectronicsStoreApi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ElectronicsStoreApi.Tests;

public sealed class SqliteTestApplicationFactory
    : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection =
        new("Data Source=:memory:");

    private readonly SaveChangesInterceptor? _saveChangesInterceptor;

    public SqliteTestApplicationFactory(
        SaveChangesInterceptor? saveChangesInterceptor = null)
    {
        _saveChangesInterceptor = saveChangesInterceptor;

        // Держим соединение открытым всё время жизни фабрики.
        // Иначе SQLite in-memory база будет уничтожена.
        _connection.Open();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Port=5432;Database=unused;Username=unused;Password=unused",

                    ["Jwt:Issuer"] =
                        "ElectronicsStoreApi.Tests",

                    ["Jwt:Audience"] =
                        "ElectronicsStoreApi.Tests.Client",

                    ["Jwt:Key"] =
                        "dGVzdC1vbmx5LWtleS1mb3ItaW50ZWdyYXRpb24tdGVzdHMtMzJieXRlcw=="
                });
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<
                IDbContextOptionsConfiguration<StoreDbContext>>();

            services.RemoveAll<
                DbContextOptions<StoreDbContext>>();

            services.RemoveAll<StoreDbContext>();

            services.AddDbContext<StoreDbContext>(options =>
            {
                options.UseSqlite(_connection);

                if (_saveChangesInterceptor is not null)
                {
                    options.AddInterceptors(_saveChangesInterceptor);
                }
            });
        });
    }

    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();

        var context =
            scope.ServiceProvider.GetRequiredService<StoreDbContext>();

        await context.Database.EnsureCreatedAsync();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
