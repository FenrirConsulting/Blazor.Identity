using Blazor.RCL.Application.Common.Configuration.Interfaces;
using Blazor.RCL.Infrastructure.Common.Configuration;
using Blazor.RCL.NLog.LogService.Interface;
using Blazor.RCL.NLog.LogService;
using Blazor.RCL.Infrastructure.Authentication.Interfaces;
using Blazor.RCL.Infrastructure.Authentication;
using Blazor.RCL.Infrastructure.Services.Interfaces;
using Blazor.RCL.Infrastructure.Services;
using Blazor.RCL.Infrastructure.Data;
using Blazor.RCL.Application.Interfaces;
using Blazor.RCL.Infrastructure.Common;
using Blazor.RCL.Infrastructure.Data.Repositories;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Distributed;
using Blazor.RCL.Application.Models;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Session;


namespace Blazor.Identity
{
    /// <summary>
    /// Provides dependency injection and configuration methods for the server application.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static class DependencyInjection
    {
        #region Public Methods
        /// <summary>
        /// Adds server UI services to the service collection.
        /// </summary>
        /// <param name="services">The IServiceCollection to add services to.</param>
        /// <param name="config">The configuration object.</param>
        /// Add Services apart from Standard When Needed
        /// <returns>The updated IServiceCollection.</returns>
        public static IServiceCollection AddServerUI(this IServiceCollection services, IConfiguration config)
        {
            StandardServices(services, config);

            /// <summary>
            /// Add application specific services not included in standard service set from WebCoreUtility
            /// </summary>
            


            return services;
        }

        /// <summary>
        /// Configures the server application.
        /// </summary>
        /// <param name="app">The WebApplication to configure.</param>
        /// <param name="config">The configuration object.</param>
        /// Add Configuration apart from Standard When Needed
        /// <returns>The configured WebApplication.</returns>
        public static WebApplication ConfigureServer(this WebApplication app, IConfiguration config)
        {
            StandardMiddleware(app);

            /// <summary>
            /// Add application specific configuration  not included in standard configuration set from WebCoreUtility
            /// </summary>

            return app;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Configures standard services for the application.
        /// </summary>
        /// <param name="services">The IServiceCollection to add services to.</param>
        /// <param name="config">The configuration object.</param>
        private static void StandardServices(IServiceCollection services, IConfiguration config)
        {
            #region Application Services
            // Core application services
            services.AddHttpContextAccessor();
            services.AddControllers();
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen();
            services.AddScoped<ErrorHandlerService>();

            #region Distributed Cache Services
            // Configure distributed cache for improved performance
            services.AddScoped<IDistributedCacheRepository, DistributedCacheRepository>();
            services.AddSingleton<IDistributedCache>(sp => new CustomDistributedCache(sp));
            #endregion

            // NLog Services for logging
            services.AddTransient<ILogService, LogService>();
            services.AddTransient<ILogHelper, LogHelper>();
            #endregion

            #region Static Configuration Services
            // Services for managing static configuration
            services.AddSingleton<LdapServerList>();
            services.AddSingleton<LdapRoleMappingConfig>();
            services.AddSingleton<NavLinksInfoList>();
            services.AddSingleton<RemoteScriptList>();
            services.AddSingleton<ServiceAccountList>();
            services.AddSingleton<ServerHostList>();
            services.AddSingleton<IRegistryHelperService, RegistryHelperService>();

            // Holds Azure AD Options in a Service Class
            services.AddSingleton<IAzureAdOptions>(sp =>
            {
                var registryHelper = sp.GetRequiredService<IRegistryHelperService>();
                var logHelper = sp.GetRequiredService<ILogHelper>();
                return new AzureAdOptionsService(config, registryHelper, logHelper);
            });

            // Holds Contents of AppSettings.JSON in a Service Class
            services.AddSingleton<IAppConfiguration>(sp =>
            {
                var registryHelper = sp.GetRequiredService<IRegistryHelperService>();
                var logHelper = sp.GetRequiredService<ILogHelper>();
                return new ServerAppConfiguration(config, registryHelper, logHelper);
            });

            services.AddScoped<IUserSettingsService, UserSettingsService>();
            #endregion

            #region Database Services
            // Add DbContext with connection string from appConfiguration
            var appConfiguration = services.BuildServiceProvider().GetRequiredService<IAppConfiguration>();
            services.AddDbContextFactory<AppDbContext>(options =>
            {
                options.UseSqlServer(appConfiguration.ConnectionString);
                options.UseLoggerFactory(LoggerFactory.Create(builder => builder.AddFilter((category, level) =>
                    category == DbLoggerCategory.Database.Command.Name
                    && level == LogLevel.Information)));
            });

            // Table Repository Services for data access
            services.AddScoped<ILDAPServerRepository, LDAPServerRepository>();
            services.AddScoped<IToolsConfigurationRepository, ToolsConfigurationRepository>();
            services.AddScoped<IRemoteScriptRepository, RemoteScriptRepository>();
            services.AddScoped<IServiceAccountRepository, ServiceAccountRepository>();
            services.AddScoped<IUserSettingsRepository, UserSettingsRepository>();
            services.AddScoped<IServerHostRepository, ServerHostRepository>();
            services.AddScoped<IDistributedCacheRepository, DistributedCacheRepository>();

            // Preload Configuration into Static Classes before Authentication is Called
            services.AddHostedService<ConfigurationInitializationService>();
            #endregion

            #region Authentication Services
            // Configure authentication services
            services.AddSingleton<IDomainUserGroupService, DomainUserGroupService>();
            services.AddScoped<IClaimsTransformation, CustomClaimsTransform>();
            services.AddScoped<ILdapAuthenticationService, LdapAuthenticationService>();
            services.AddScoped<RoleUpdateRequest>();

            // Cookies stored key ring for secure storage (switch to E:\ for Local Development
            if (config["EnvironmentLoaded"]?.Equals("Development", StringComparison.OrdinalIgnoreCase) == true)
            {
                services.AddDataProtection()
               .PersistKeysToFileSystem(new DirectoryInfo(@"E:\ApplicationName\TOKENS"))
               .SetApplicationName(config["CookieName"]!);
            }
            else
            {
                services.AddDataProtection()
               .PersistKeysToDbContext<AppDbContext>()
               .SetApplicationName(config["CookieName"]!);
            }

            services.AddSingleton<ISessionStore>(provider =>
            {
                var distributedCache = provider.GetRequiredService<IDistributedCache>();
                var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
                return new DistributedSessionStore(distributedCache, loggerFactory);
            });

            // Configure session
            services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(30);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });
            /// <summary>
            /// Configures cookie authentication options.
            /// </summary>
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
                {
                    options.Cookie.Name = config["CookieName"];
                    options.Cookie.Domain = config["DomainSite"];
                    options.Cookie.Path = "/";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.LoginPath = "/account/cookielogin";
                    options.ExpireTimeSpan = TimeSpan.FromHours(config.GetValue<int>("SessionTimeout", 1));
                    options.SlidingExpiration = true;
                });

            // Uses the Authentication Cookie as a Handler for Post Requests to other Applications
            services.AddTransient<CookieHandler>();
            services.AddHttpClient(config["CookieName"]!)
                    .AddHttpMessageHandler<CookieHandler>();

            services.AddCors(options =>
            {

               options.AddPolicy("AllowLocalhost", 
                    builder =>
                    {
                        builder.SetIsOriginAllowed(origin => new Uri(origin).Host == "localhost")
                               .AllowAnyHeader()
                               .AllowAnyMethod()
                               .AllowCredentials();
                    });
            });
            #endregion

            #region Authorization Services
            // Configure authorization policies
            services.AddAuthorization(options =>
            {
                var ldapRoleMappingConfig = services.BuildServiceProvider().GetRequiredService<LdapRoleMappingConfig>();
                foreach (var key in ldapRoleMappingConfig.LdapRoleMappings.Keys)
                {
                    options.AddPolicy(key, policy => policy.RequireRole(key));
                }

                options.FallbackPolicy = options.DefaultPolicy;
            });
            #endregion
        }

        /// <summary>
        /// Configures the middleware pipeline for the application.
        /// </summary>
        /// <param name="app">The WebApplication to configure.</param>
        private static void StandardMiddleware(WebApplication app)
        {
            #region Middleware Configuration
            var appConfiguration = app.Services.GetRequiredService<IAppConfiguration>();
            var baseUrl = appConfiguration.BaseUrl;
            app.UsePathBase(baseUrl);

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint($"{baseUrl}swagger/v1/swagger.json", "Auth API V1");
                    c.RoutePrefix = string.Empty;
                });
            }

            // Set up the middleware pipeline
            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseSession();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();
            app.Run();
            #endregion
        }
        #endregion
    }
}