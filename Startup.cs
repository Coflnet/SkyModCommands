using System;
using System.IO;
using System.Reflection;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.Core;
using Coflnet.Sky.Commands.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Coflnet.Sky.Proxy.Client.Api;
using Prometheus;
using Coflnet.Sky.Api.Client.Api;
using StackExchange.Redis;
using Coflnet.Sky.Commands;
using Cassandra;
using System.Security.Cryptography.X509Certificates;
using System.Security.Authentication;
using System.Linq;
using System.Collections.Generic;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Core.Services;
using Coflnet.Sky.ModCommands.Services.Vps;

namespace Coflnet.Sky.ModCommands;
public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers().AddNewtonsoftJson();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "SkyModCommands", Version = "v1" });
            // Set the comments path for the Swagger JSON and UI.
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            c.IncludeXmlComments(xmlPath);
        });

        // For common usages, see pull request #1233.
        var serverVersion = new MariaDbServerVersion(new Version(Configuration["MARIADB_VERSION"]));

        services.AddDbContext<HypixelContext>(
            dbContextOptions => dbContextOptions
                .UseMySql(Configuration["DB_CONNECTION"], serverVersion)
                .EnableSensitiveDataLogging() // <-- These two calls are optional but help
                .EnableDetailedErrors()       // <-- with debugging (remove for production).
        );
        services.AddHostedService<ModBackgroundService>();
        services.AddHostedService(s => s.GetRequiredService<FlipperService>());
        services.AddJaeger(Configuration, 1, 1);
        services.AddTransient<CounterService>();
        services.AddSingleton<ModeratorService>();
        services.AddSingleton<ChatService>();
        services.AddSingleton<ITutorialService, TutorialService>();
        services.AddSingleton<IFlipApi, FlipApi>(s => new FlipApi(Configuration["API_BASE_URL"]));
        services.AddSingleton<PreApiService>();
        services.AddSingleton<CommandSyncService>();
        services.AddSingleton<IIsSold>(s => s.GetRequiredService<PreApiService>());
        services.AddSingleton<IFlipReceiveTracker>(s => s.GetRequiredService<FlipTrackingService>());
        services.AddSingleton(s => ConnectionMultiplexer.Connect(Configuration["MOD_REDIS_HOST"]));
        services.AddSingleton<IConnectionMultiplexer, IConnectionMultiplexer>(s => s.GetRequiredService<ConnectionMultiplexer>());
        services.AddSingleton<IBaseApi, BaseApi>(s => new BaseApi(Configuration["PROXY_BASE_URL"]));
        services.AddSingleton<IProxyApi, ProxyApi>(s => new ProxyApi(Configuration["PROXY_BASE_URL"]));
        RegisterScyllaSession(services);
        services.AddHostedService(s => s.GetRequiredService<PreApiService>());
        services.AddSingleton<IAhActive, AhActiveService>();
        services.AddSingleton<CircumventTracker>();
        services.AddSingleton<ConfigStatsService>();
        services.AddSingleton<NecImportService>();
        services.AddSingleton<IBlockedService, BlockedService>();
        services.AddSingleton<Sniper.Client.Api.IAuctionApi, Sniper.Client.Api.AuctionApi>(s => new Sniper.Client.Api.AuctionApi(Configuration["SNIPER_BASE_URL"]));
        services.AddSingleton<McConnect.Api.IConnectApi, McConnect.Api.ConnectApi>(s => new McConnect.Api.ConnectApi(Configuration["MCCONNECT_BASE_URL"]));
        services.AddSingleton<HypixelItemService>();
        services.AddSingleton<IHypixelItemStore, HypixelItemService>(di => di.GetRequiredService<HypixelItemService>());
        services.AddSingleton<System.Net.Http.HttpClient>();
        services.AddSingleton<IPriceStorageService, PriceStorageService>();
        services.AddSingleton<DelayService>();
        services.AddSingleton<AltChecker>();
        services.AddSingleton<VpsInstanceManager>();
        services.AddSingleton<IDelayExemptList, DelayExemptionList>();
        services.AddCoflService();
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseExceptionHandler(errorApp =>
        {
            ErrorHandler.Add(errorApp, "modcommands");
        });
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "SkyModCommands v1");
            c.RoutePrefix = "api";
        });

        app.UseRouting();

        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapMetrics();
            endpoints.MapControllers();
        });
    }

    private void RegisterScyllaSession(IServiceCollection services)
    {
        services.AddSingleton<ISession>(p =>
        {
            Console.WriteLine("Connecting to Cassandra...");
            var builder = Cluster.Builder().AddContactPoints(Configuration["CASSANDRA:HOSTS"].Split(","))
                .WithLoadBalancingPolicy(new TokenAwarePolicy(new DCAwareRoundRobinPolicy()))
                .WithCredentials(Configuration["CASSANDRA:USER"], Configuration["CASSANDRA:PASSWORD"])
                .WithDefaultKeyspace(Configuration["CASSANDRA:KEYSPACE"]);

            Console.WriteLine("Connecting to servers " + Configuration["CASSANDRA:HOSTS"]);
            Console.WriteLine("Using keyspace " + Configuration["CASSANDRA:KEYSPACE"]);
            Console.WriteLine("Using replication class " + Configuration["CASSANDRA:REPLICATION_CLASS"]);
            Console.WriteLine("Using replication factor " + Configuration["CASSANDRA:REPLICATION_FACTOR"]);
            Console.WriteLine("Using user " + Configuration["CASSANDRA:USER"]);
            Console.WriteLine("Using password " + Configuration["CASSANDRA:PASSWORD"].Truncate(2) + "...");
            var certificatePaths = Configuration["CASSANDRA:X509Certificate_PATHS"];
            Console.WriteLine("Using certificate paths " + certificatePaths);
            Console.WriteLine("Using certificate password " + Configuration["CASSANDRA:X509Certificate_PASSWORD"].Truncate(2) + "...");
            var validationCertificatePath = Configuration["CASSANDRA:X509Certificate_VALIDATION_PATH"];
            if (!string.IsNullOrEmpty(certificatePaths))
            {
                var password = Configuration["CASSANDRA:X509Certificate_PASSWORD"] ?? throw new InvalidOperationException("CASSANDRA:X509Certificate_PASSWORD must be set if CASSANDRA:X509Certificate_PATHS is set.");
                CustomRootCaCertificateValidator certificateValidator = null;
                if (!string.IsNullOrEmpty(validationCertificatePath))
                    certificateValidator = new CustomRootCaCertificateValidator(new X509Certificate2(validationCertificatePath, password));
                var sslOptions = new SSLOptions(
                    // TLSv1.2 is required as of October 9, 2019.
                    // See: https://www.instaclustr.com/removing-support-for-outdated-encryption-mechanisms/
                    SslProtocols.Tls12,
                    false,
                    // Custom validator avoids need to trust the CA system-wide.
                    (sender, certificate, chain, errors) => certificateValidator?.Validate(certificate, chain, errors) ?? true
                ).SetCertificateCollection(new(certificatePaths.Split(',').Select(p => new X509Certificate2(p, password)).ToArray()));
                builder.WithSSL(sslOptions);
            }
            var cluster = builder.Build();
            var session = cluster.Connect(null);
            var defaultKeyspace = cluster.Configuration.ClientOptions.DefaultKeyspace;
            try
            {
                session.CreateKeyspaceIfNotExists(defaultKeyspace, new Dictionary<string, string>()
                {
                    {"class", Configuration["CASSANDRA:REPLICATION_CLASS"]},
                    {"replication_factor", Configuration["CASSANDRA:REPLICATION_FACTOR"]}
                });
                session.ChangeKeyspace(defaultKeyspace);
                Console.WriteLine("Created cassandra keyspace");
            }
            catch (UnauthorizedException)
            {
                Console.WriteLine("User unauthorized to create keyspace, trying to connect directly");
            }
            finally
            {
                session.ChangeKeyspace(defaultKeyspace);
            }
            return session;
        });
    }
}
