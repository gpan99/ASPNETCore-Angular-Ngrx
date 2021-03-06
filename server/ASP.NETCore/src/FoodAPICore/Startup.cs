﻿using FoodAPICore.Entities;
using FoodAPICore.Repositories.Food;
using FoodAPICore.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using FoodAPICore.Services;
using FoodAPICore.Dtos;
using Swashbuckle.AspNetCore.Swagger;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Newtonsoft.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using IdentityServer4.AccessTokenValidation;
using FoodAPICore.Hubs;
using FoodAPICore.Repositoriess;
using FoodAPICore.Helpers;

namespace FoodAPICore
{
    public class Startup
    {
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit http://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddOptions();

            services.AddCors(options =>
            {
                options.AddPolicy("AllowAllOrigins",
                    builder =>
                    {
                        builder
                            .AllowAnyOrigin()
                            .AllowAnyHeader()
                            .AllowAnyMethod()
                            .AllowCredentials();
                    });
            });

            // services.AddDbContext<FoodDbContext>(opt => opt.UseInMemoryDatabase("FoodDatabase"));
            services.AddDbContext<FoodDbContext>(options => options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection")));

            services.AddIdentity<IdentityUser, IdentityRole>()
                .AddEntityFrameworkStores<FoodDbContext>()
                .AddDefaultTokenProviders();

            services.AddRouting(options => options.LowercaseUrls = true);
            services.AddSingleton<IActionContextAccessor, ActionContextAccessor>();
            services.AddScoped<IUrlHelper>(implementationFactory =>
            {
                var actionContext = implementationFactory.GetService<IActionContextAccessor>().ActionContext;
                return new UrlHelper(actionContext);
            });

            // Identity options.
            services.Configure<IdentityOptions>(options =>
            {
                //options.User.RequireUniqueEmail = true;
                // Password settings.
                options.Password.RequireDigit = false;
                options.Password.RequiredLength = 5;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
            });

            services.AddSignalR();

            // Claims-Based Authorization: role claims.
            services.AddAuthorization(options =>
            {
                options.AddPolicy("Manage Accounts", policy => policy.RequireRole("administrator"));
                options.AddPolicy("Access Resources", policy => policy.RequireRole("administrator", "user"));
                options.AddPolicy("Modify Resources", policy => policy.RequireRole("administrator"));
            });

            services.AddIdentityServer()
                .AddDeveloperSigningCredential()
                .AddInMemoryPersistedGrants()
                .AddInMemoryIdentityResources(IdentityConfig.GetIdentityResources())
                .AddInMemoryApiResources(IdentityConfig.GetApiResources())
                .AddInMemoryClients(IdentityConfig.GetClients())
                .AddAspNetIdentity<IdentityUser>();

            services.AddAuthentication(IdentityServerAuthenticationDefaults.AuthenticationScheme)
                 .AddIdentityServerAuthentication(options =>
                 {
                     // this line gets overwritten on your server, points to localhost right now
                     options.Authority = Configuration["IdentityServerEndpoint"];
                     options.RequireHttpsMetadata = Configuration["IdentityServerEndpoint"].StartsWith("https");
                     options.ApiName = "WebAPI";
                 });

            services.AddScoped<IFoodRepository, FoodRepository>();
            services.AddScoped<IProductRepository, ProductRepository>();
            services.AddScoped<IEnsureDatabaseDataService, EnsureDatabaseDataService>();
            services.AddScoped<IUserNotesRepository, UserNotesRepository>();
            services.AddScoped<ICustomersRepository, CustomersRepository>();
            services.AddScoped<IStatesRepository, StatesRepository>();
            services.AddScoped<IPeriodicElementsRepository, PeriodicElementsRepository>();
            // register the repository
            services.AddScoped<ILibraryRepository, LibraryRepository>();

            services.AddMvc().AddJsonOptions(options =>
            {
                options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            }).SetCompatibilityVersion(CompatibilityVersion.Version_2_1);

            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new Info { Title = "FoodAPICore", Version = "v1" });
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory,
            FoodDbContext foodDbContext)
        {
          //  loggerFactory.AddConsole();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler(errorApp =>
                {
                    errorApp.Run(async context =>
                    {
                        context.Response.StatusCode = 500;
                        context.Response.ContentType = "text/plain";
                        var errorFeature = context.Features.Get<IExceptionHandlerFeature>();
                        if (errorFeature != null)
                        {
                            var logger = loggerFactory.CreateLogger("Global exception logger");
                            logger.LogError(500, errorFeature.Error, errorFeature.Error.Message);
                        }

                        await context.Response.WriteAsync("There was an error");
                    });
                });
            }

            app.UseCors("AllowAllOrigins");
            AutoMapper.Mapper.Initialize(mapper =>
            {
                mapper.CreateMap<FoodItem, FoodItemDto>().ReverseMap();
                mapper.CreateMap<FoodItem, FoodItemUpdateDto>().ReverseMap();
                mapper.CreateMap<FoodItem, FoodItemCreateDto>().ReverseMap();
                mapper.CreateMap<Product, ProductDto>().ReverseMap();
                mapper.CreateMap<PeriodicElement, PeriodicElementDto>().ReverseMap();
                //    mapper.CreateMap<Product, ProductUpdateDto>().ReverseMap();
                mapper.CreateMap<Entities.Author, Models.AuthorDto>()
                 .ForMember(dest => dest.Name, opt => opt.MapFrom(src =>
                 $"{src.FirstName} {src.LastName}"))
                 .ForMember(dest => dest.Age, opt => opt.MapFrom(src =>
                 src.DateOfBirth.GetCurrentAge()));

                mapper.CreateMap<Entities.Book, Models.BookDto>();

                mapper.CreateMap<Models.AuthorForCreationDto, Entities.Author>();

                mapper.CreateMap<Models.BookForCreationDto, Entities.Book>();

                mapper.CreateMap<Models.BookForUpdateDto, Entities.Book>();

                mapper.CreateMap<Entities.Book, Models.BookForUpdateDto>();
            });
            foodDbContext.EnsureSeedDataForContext();

            // IdentityServer4.AccessTokenValidation: authentication middleware for the API.
            app.UseIdentityServer();

            app.UseAuthentication();

            app.UseStaticFiles();
            app.UseDefaultFiles();

            app.UseSignalR(routes =>
            {
                routes.MapHub<FoodHub>("/foodhub");
            });
            // Enable middleware to serve generated Swagger as a JSON endpoint.
            app.UseSwagger();

            // Enable middleware to serve swagger-ui (HTML, JS, CSS etc.), specifying the Swagger JSON endpoint.
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "FoodAPICore V1");
            });

            app.UseMvcWithDefaultRoute();
        }
    }
}
