﻿using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using CoreX.Extensions.Metrics;
using CorrelationId;
using CorrelationId.DependencyInjection;

using MicroserviceTemplate.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.FeatureManagement;
using Microsoft.OpenApi.Models;

[assembly: ApiConventionType(typeof(DefaultApiConventions))]
namespace MicroserviceTemplate
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }
        public OpenApiInfo OpenApiInfo { get; set; } = new OpenApiInfo
        {
            Title = "MicroserviceTemplate",
            Version = "v1"
        };

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews();

            services.AddMetrics();
         
            // Register Feature Management
            services.AddFeatureManagement();

            // Register the Swagger generator, defining 1 or more Swagger documents
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", OpenApiInfo);

                // Set the comments path for the Swagger JSON and UI.
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                c.IncludeXmlComments(xmlPath);

                c.EnableAnnotations();
            });

            // Enable the logging for common 400 bad request errors, for easier traceability
            services.EnableLoggingForBadRequests();

            // Register CorrelationId middleware
            services.AddDefaultCorrelationId();
            
            // Register HttpClientFactory
            services.AddHttpClient();

            // Register global header propagation for any HttpClient that comes from HttpClientFactory
            // default headers are already added like x-correlation-id
            services.AddHeaderPropagation(options => { });

            // Register Logging for HttpClient
            services.AddHttpClientLogging(Configuration);

            // Register HttpLog middleware for "/log"
            services.AddHttpLog(Configuration);

            // Register Developer Dashboard middleware
            services.AddDeveloperDashboard();

            // Register Health checks
            services.AddHealthChecks()
                .AddSqlServer(Configuration["ConnectionStrings:DefaultConnection"], name: "DefaultConnection");

            // Register Application Insights
            services.AddApplicationInsightsTelemetry();

            // Register the DbContext
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlServer(
                    Configuration["ConnectionStrings:DefaultConnection"]);
            });

            // Configure ForwardedHeaders
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.All;
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public async void Configure(IApplicationBuilder app, IWebHostEnvironment env, IFeatureManager featureManager)
        {
            app.UseCorrelationId();

            if (await featureManager.IsEnabledAsync(Features.Metrics))
            {
                // Enables API Metrics Calculation in Memory
                app.UseMetrics();
            }

            if (await featureManager.IsEnabledAsync(Features.ForwardedHeaders))
            {
                // Enables ForwardedHeaders to enable hosting behind reverse proxies like NGINX or inside Kubernetes
                app.UseForwardedHeaders();
            }

            if (await featureManager.IsEnabledAsync(Features.HttpLogger))
            {
                // Enable HttpLog middleware for "/log"
                app.UseHttpLog();
            }

            if (await featureManager.IsEnabledAsync(Features.Healthz))
            {
                // Enable middleware for "/healthz"
                app.UseHealthChecks("/healthz");
            }

            // Enable developer exception page for debugging
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            if (await featureManager.IsEnabledAsync(Features.Swagger))
            {
                // Enable middleware to serve generated Swagger as a JSON endpoint.
                app.UseSwagger();

                // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.),
                // specifying the Swagger JSON endpoint.
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MicroserviceTemplate");
                });
            }

            app.UseRouting();
            app.UseAuthorization();

            if (await featureManager.IsEnabledAsync(Features.DeveloperDashboard))
            {
                // Enable Developer Dashboard
                app.UseDeveloperDashboard(env);
            }

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
