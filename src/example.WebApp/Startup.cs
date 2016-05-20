﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.Cookies;
using cloudscribe.Core.Web.Controllers;

namespace example.WebApp
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true);

            appBasePath = env.ContentRootPath;

            if (env.IsDevelopment())
            {
                // For more details on using the user secret store see http://go.microsoft.com/fwlink/?LinkID=532709
                builder.AddUserSecrets();
            }

            // this file name is ignored by gitignore
            // so you can create it and use on your local dev machine
            // remember last config source added wins if it has the same settings
            builder.AddJsonFile("appsettings.local.overrides.json", optional: true);

            // most common use of environment variables would be in azure hosting
            // since it is added last anything in env vars would trump the same setting in previous config sources
            // so no risk of messing up settings if deploying a new version to azure
            builder.AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        private string appBasePath;
        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            //services.AddDbContext<ApplicationDbContext>(options =>
            //    options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection")));

            //services.AddIdentity<ApplicationUser, IdentityRole>()
            //    .AddEntityFrameworkStores<ApplicationDbContext>()
            //    .AddDefaultTokenProviders();

            //services.AddDataProtection(configure =>
            //{
            //    string pathToCryptoKeys = appBasePath + System.IO.Path.DirectorySeparatorChar + "dp_keys" + System.IO.Path.DirectorySeparatorChar;
            //    configure.PersistKeysToFileSystem(new System.IO.DirectoryInfo(pathToCryptoKeys));
                

            //});

            //bool enableGlimpse = Configuration.GetValue("DiagnosticOptions:EnableGlimpse", false);

            //if (enableGlimpse)
            //{
            //    services.AddGlimpse();
            //}

            //services.AddCaching();
            services.AddMemoryCache();
            // we currently only use session for alerts, so we can fire an alert on the next request
            // if session is disabled this feature fails quietly with no errors
            services.AddSession();
            

            ConfigureAuthPolicy(services);

            services.AddOptions();

            /* optional and only needed if you are using cloudscribe Logging  */
            //services.AddScoped<cloudscribe.Logging.Web.LogManager>();

            /* these are optional and only needed if using cloudscribe Setup */
            //services.Configure<SetupOptions>(Configuration.GetSection("SetupOptions"));
            //services.AddScoped<SetupManager, SetupManager>();
            //services.AddScoped<IVersionProvider, SetupVersionProvider>();
            //services.AddScoped<IVersionProvider, CloudscribeLoggingVersionProvider>();
            /* end cloudscribe Setup */
            
            services.AddCloudscribeCore(Configuration);

            services.AddCloudscribeIdentity(options => {

                options.Cookies.ApplicationCookie.AuthenticationScheme 
                    = cloudscribe.Core.Identity.AuthenticationScheme.Application;
                
                options.Cookies.ApplicationCookie.CookieName 
                    = cloudscribe.Core.Identity.AuthenticationScheme.Application;

                //options.Cookies.ApplicationCookie.DataProtectionProvider = 
                //DataProtectionProvider.Create(new DirectoryInfo("C:\\Github\\Identity\\artifacts"));
            });

            services.AddMvc()
                    .AddViewLocalization(options =>
                    {
                        options.ResourcesPath = "AppResources";
                    })
                    .AddRazorOptions(options =>
                    {
                        options.ViewLocationExpanders.Add(new cloudscribe.Core.Web.Components.SiteViewLocationExpander());
                    });

            ConfigureDataStorage(services);

            //var container = new Container();
            //container.Populate(services);

            //return container.GetInstance<IServiceProvider>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(
            IApplicationBuilder app, 
            IHostingEnvironment env, 
            ILoggerFactory loggerFactory,
            IOptions<cloudscribe.Core.Models.MultiTenantOptions> multiTenantOptionsAccessor,
            //IOptions<IdentityOptions> identityOptionsAccessor,
            //cloudscribe.Core.Identity.SiteAuthCookieValidator cookieValidator,
            //Microsoft.AspNetCore.Identity.ISecurityStampValidator securityStampValidator,
            ILogger<cloudscribe.Core.Identity.SiteAuthCookieValidator> logger,
            IServiceProvider serviceProvider
            )
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseDatabaseErrorPage();
                app.UseBrowserLink();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseStaticFiles();

            app.UseSession();

            app.UseMultitenancy<cloudscribe.Core.Models.SiteSettings>();

            //app.UseTenantContainers<SiteSettings>();
            var multiTenantOptions = multiTenantOptionsAccessor.Value;

            app.UsePerTenant<cloudscribe.Core.Models.SiteSettings>((ctx, builder) =>
            {

                //var tenantIdentityOptionsProvider = app.ApplicationServices.GetRequiredService<IOptions<IdentityOptions>>();
                //var cookieOptions = tenantIdentityOptionsProvider.Value.Cookies;
                //var cookieOptions = identityOptionsAccessor.Value.Cookies;
                var tenant = ctx.Tenant;

                var shouldUseFolder = !multiTenantOptions.UseRelatedSitesMode
                                        && multiTenantOptions.Mode == cloudscribe.Core.Models.MultiTenantMode.FolderName
                                        && ctx.Tenant.SiteFolderName.Length > 0;

                //var tenantPathBase = string.IsNullOrEmpty(tenant.SiteFolderName)
                //    ? PathString.Empty
                //    : new PathString("/" + tenant.SiteFolderName);

                // TODO: I'm not sure newing this up here is agood idea
                // are we missing any default configuration thast would normally be set for identity?
                var identityOptions = new IdentityOptions();

                var cookieEvents = new CookieAuthenticationEvents();
                //var cookieValidator = new cloudscribe.Core.Identity.SiteAuthCookieValidator(securityStampValidator, logger);
                var cookieValidator = new cloudscribe.Core.Identity.SiteAuthCookieValidator(logger);

                SetupAppCookie(
                    identityOptions.Cookies.ApplicationCookie, 
                    cookieEvents,
                    cookieValidator,
                    cloudscribe.Core.Identity.AuthenticationScheme.Application, 
                    tenant
                    );
                SetupOtherCookies(identityOptions.Cookies.ExternalCookie, cloudscribe.Core.Identity.AuthenticationScheme.External, tenant);
                SetupOtherCookies(identityOptions.Cookies.TwoFactorRememberMeCookie, cloudscribe.Core.Identity.AuthenticationScheme.TwoFactorRememberMe, tenant);
                SetupOtherCookies(identityOptions.Cookies.TwoFactorUserIdCookie, cloudscribe.Core.Identity.AuthenticationScheme.TwoFactorUserId, tenant);

                var cookieOptions = identityOptions.Cookies;

                builder.UseCookieAuthentication(cookieOptions.ExternalCookie);
                builder.UseCookieAuthentication(cookieOptions.TwoFactorRememberMeCookie);
                builder.UseCookieAuthentication(cookieOptions.TwoFactorUserIdCookie);
                builder.UseCookieAuthentication(cookieOptions.ApplicationCookie);

                // known issue here is if a site is updated to populate the
                // social auth keys, it currently requires a restart so that the middleware gets registered
                // in order for it to work or for the social auth buttons to appear 
                //builder.UseSocialAuth(ctx.Tenant, cookieOptions, shouldUseFolder);
                
            });


            UseMvc(app, multiTenantOptions.Mode == cloudscribe.Core.Models.MultiTenantMode.FolderName);

            // this doesn't seem to be getting it from appsettings.json after rc2
            var devOptions = Configuration.GetValue<DevOptions>("DevOptions", new DevOptions { DbPlatform = "ef" });

            switch (devOptions.DbPlatform)
            {
                case "NoDb":
                    CoreNoDbStartup.InitializeDataAsync(app.ApplicationServices).Wait();
                    break;

                case "ef":
                default:
                    // this creates ensures the database is created and initial data
                    CoreEFStartup.InitializeDatabaseAsync(app.ApplicationServices).Wait();

                    // this one is only needed if using cloudscribe Logging with EF as the logging storage
                    //cloudscribe.Logging.EF.LoggingDbInitializer.InitializeDatabaseAsync(app.ApplicationServices).Wait();

                    break;
            }

            //app.UseIdentity();

            // Add external authentication middleware below. To configure them please see http://go.microsoft.com/fwlink/?LinkID=532715

            //app.UseMvc(routes =>
            //{
            //    routes.MapRoute(
            //        name: "default",
            //        template: "{controller=Home}/{action=Index}/{id?}");
            //});
        }

        private void SetupAppCookie(
            CookieAuthenticationOptions options,
            CookieAuthenticationEvents cookieEvents,
            cloudscribe.Core.Identity.SiteAuthCookieValidator siteValidator,
            string scheme, 
            cloudscribe.Core.Models.SiteSettings tenant
            )
        {
            options.AuthenticationScheme = $"{scheme}-{tenant.SiteFolderName}";
            options.CookieName = $"{scheme}-{tenant.SiteFolderName}";
            options.CookiePath = "/" + tenant.SiteFolderName;

            var tenantPathBase = string.IsNullOrEmpty(tenant.SiteFolderName)
                ? PathString.Empty
                : new PathString("/" + tenant.SiteFolderName);

            options.LoginPath = tenantPathBase + "/account/login";
            options.LogoutPath = tenantPathBase + "/account/logoff";

            cookieEvents.OnValidatePrincipal = siteValidator.ValidatePrincipal;
            options.Events = cookieEvents;

            options.AutomaticAuthenticate = true;
            options.AutomaticChallenge = true;
        }

        private void SetupOtherCookies(
            CookieAuthenticationOptions options, 
            string scheme, 
            cloudscribe.Core.Models.SiteSettings tenant
            )
        {
            //var tenantPathBase = string.IsNullOrEmpty(tenant.SiteFolderName)
            //    ? PathString.Empty
            //    : new PathString("/" + tenant.SiteFolderName);

            options.AuthenticationScheme = $"{scheme}-{tenant.SiteFolderName}";
            options.CookieName = $"{scheme}-{tenant.SiteFolderName}";
            options.CookiePath = "/" + tenant.SiteFolderName;

        }

        private void UseMvc(IApplicationBuilder app, bool useFolders)
        {
            app.UseMvc(routes =>
            {
                if (useFolders)
                {
                    routes.MapRoute(
                        name: "folderdefault",
                        template: "{sitefolder}/{controller}/{action}/{id?}",
                        defaults: new { controller = "Home", action = "Index" },
                        constraints: new { name = new cloudscribe.Core.Web.Components.SiteFolderRouteConstraint() });
                }


                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}"
                    //,defaults: new { controller = "Home", action = "Index" }
                    );
            });
        }


        private void ConfigureAuthPolicy(IServiceCollection services)
        {
            services.AddAuthorization(options =>
            {
                options.AddPolicy(
                    "ServerAdminPolicy",
                    authBuilder =>
                    {
                        authBuilder.RequireRole("ServerAdmins");
                    });

                options.AddPolicy(
                    "CoreDataPolicy",
                    authBuilder =>
                    {
                        authBuilder.RequireRole("ServerAdmins");
                    });

                options.AddPolicy(
                    "AdminPolicy",
                    authBuilder =>
                    {
                        authBuilder.RequireRole("ServerAdmins", "Administrators");
                    });

                options.AddPolicy(
                    "UserManagementPolicy",
                    authBuilder =>
                    {
                        authBuilder.RequireRole("ServerAdmins", "Administrators");
                    });

                options.AddPolicy(
                    "RoleAdminPolicy",
                    authBuilder =>
                    {
                        authBuilder.RequireRole("Role Administrators", "Administrators");
                    });

                options.AddPolicy(
                    "SystemLogPolicy",
                    authBuilder =>
                    {
                        authBuilder.RequireRole("ServerAdmins");
                    });

                options.AddPolicy(
                    "SetupSystemPolicy",
                    authBuilder =>
                    {
                        authBuilder.RequireRole("ServerAdmins, Administrators");
                    });

            });

        }

        private void ConfigureDataStorage(IServiceCollection services)
        {
            services.AddScoped<cloudscribe.Core.Models.Setup.ISetupTask, cloudscribe.Core.Web.Components.EnsureInitialDataSetupTask>();

            //var devOptions = configuration.Get<DevOptions>("DevOptions");
            // this doesn't seem to be getting it from appsettings.json after rc2
            var devOptions = Configuration.GetValue<DevOptions>("DevOptions", new DevOptions { DbPlatform = "ef" });

            switch (devOptions.DbPlatform)
            {
                case "NoDb":
                    services.AddCloudscribeCoreNoDbStorage();
                    break;

                case "ef":
                default:
                    var connectionString = Configuration.GetConnectionString("EntityFrameworkConnectionString");
                    services.AddCloudscribeCoreEFStorage(connectionString);

                    // only needed if using cloudscribe logging with EF storage
                    //services.AddCloudscribeLoggingEFStorage(connectionString);


                    break;
            }
        }

        private void ConfigureLogging(ILoggerFactory loggerFactory, IServiceProvider serviceProvider)
        {
            //var logRepository = serviceProvider.GetService<cloudscribe.Logging.Web.ILogRepository>();

            loggerFactory.AddConsole(minLevel: LogLevel.Warning);

            // a customizable filter for logging
            //LogLevel minimumLevel = LogLevel.Warning;

            //// add exclusions to remove noise in the logs
            //var excludedLoggers = new List<string>
            //{
            //    "Microsoft.Data.Entity.Storage.Internal.RelationalCommandBuilderFactory",
            //    "Microsoft.Data.Entity.Query.Internal.QueryCompiler",
            //    "Microsoft.Data.Entity.DbContext",
            //};

            //Func<string, LogLevel, bool> logFilter = (string loggerName, LogLevel logLevel) =>
            //{
            //    if (logLevel < minimumLevel)
            //    {
            //        return false;
            //    }

            //    if (excludedLoggers.Contains(loggerName))
            //    {
            //        return false;
            //    }

            //    return true;
            //};

            //loggerFactory.AddDbLogger(serviceProvider, logRepository, logFilter);
        }
    }
}
