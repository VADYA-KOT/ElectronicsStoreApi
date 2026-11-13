using ElectronicsStoreApi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ElectronicsStoreApi.Tests;

public sealed class TestApplicationFactory
    : WebApplicationFactory<Program>
{
    private readonly string _databaseName =
        $"ApiTests_{Guid.NewGuid():N}";

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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
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
                options.UseInMemoryDatabase(_databaseName));
        });
    }
}
