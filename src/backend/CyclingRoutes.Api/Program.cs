using System.Text.Json.Serialization;
using CyclingRoutes.Api.RoutePlanning;
using CyclingRoutes.Application.RoutePlanning;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Infrastructure.Routing;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<RouteIntentValidator>();
builder.Services.AddSingleton(new OpenRouteServiceOptions
{
	ApiKey = builder.Configuration["Routing:OpenRouteService:ApiKey"] ?? ""
});
builder.Services.AddHttpClient<IRoutingProvider, OpenRouteServiceProvider>(client =>
{
	client.Timeout = TimeSpan.FromSeconds(15);
	client.MaxResponseContentBufferSize = 8 * 1024 * 1024;
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddTransient<RouteGenerationService>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
// Keep malformed requests as 400 responses in Development as well as Production.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
	app.MapOpenApi();
}

app.MapHealthChecks("/health");
app.MapRouteIntentEndpoints();
app.MapRouteGenerationEndpoints();

app.UseHttpsRedirection();

app.Run();
