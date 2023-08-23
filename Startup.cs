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

namespace Coflnet.Sky.ModCommands
{
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
            services.AddHostedService<FlipperService>(s => s.GetRequiredService<FlipperService>());
            services.AddJaeger(Configuration, 1, 1);
            services.AddTransient<ModService>();
            services.AddSingleton<ModeratorService>();
            services.AddSingleton<ChatService>();
            services.AddSingleton<ITutorialService, TutorialService>();
            services.AddSingleton<IFlipApi, FlipApi>(s => new FlipApi(Configuration["API_BASE_URL"]));
            services.AddSingleton<PreApiService>();
            services.AddSingleton<IIsSold>(s => s.GetRequiredService<PreApiService>());
            services.AddSingleton<IFlipReceiveTracker>(s => s.GetRequiredService<FlipTrackingService>());
            services.AddSingleton<ConnectionMultiplexer>(s => ConnectionMultiplexer.Connect(Configuration["MOD_REDIS_HOST"]));
            services.AddSingleton<IBaseApi, BaseApi>(s => new BaseApi(Configuration["PROXY_BASE_URL"]));
            services.AddSingleton<IProxyApi, ProxyApi>(s => new ProxyApi(Configuration["PROXY_BASE_URL"]));
            services.AddCoflService();
            services.AddHostedService<PreApiService>(s => s.GetRequiredService<PreApiService>());
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "SkyModCommands v1");
                c.RoutePrefix = "api";
            });

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapMetrics();
                endpoints.MapControllers();
            });
        }
    }
}
