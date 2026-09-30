using System.Text.Json.Serialization;
using CyclingRoutes.Application.Interpretation;
using CyclingRoutes.Infrastructure.Interpretation;
using CyclingRoutes.Api.RoutePlanning;
using CyclingRoutes.Application.RoutePlanning;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Infrastructure.Routing;
using CyclingRoutes.Infrastructure.Naming;

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
builder.Services.AddSingleton<ISettlementLookup>(_ => GeoNamesSettlementLookup.LoadEmbedded());
builder.Services.AddSingleton<RouteNameResolver>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new GeminiOptions
{
	ApiKey = builder.Configuration["Ai:Gemini:ApiKey"] ?? "",
	Model = builder.Configuration["Ai:Gemini:Model"] ?? ""
});
builder.Services.AddHttpClient<IRouteIntentInterpreter, GeminiRouteIntentInterpreter>(client =>
{
	client.Timeout = Timeout.InfiniteTimeSpan;
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddTransient<InterpretationService>();
builder.Services.AddSingleton<RouteCandidateRanker>();
builder.Services.AddSingleton<RoadQualityAssessor>();
builder.Services.AddSingleton<RoadCandidateSelector>();
builder.Services.AddTransient<RouteCandidateService>();
builder.Services.AddHttpClient<IRouteSearchAdvisor, GeminiRouteSearchAdvisor>(client =>
{
	client.Timeout = Timeout.InfiniteTimeSpan;
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddTransient<RoutePlanningService>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
	options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
	options.SerializerOptions.AllowDuplicateProperties = false;
});
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
app.MapRouteCandidatesEndpoints();
app.MapRoutePlanEndpoints();
app.MapInterpretRouteIntentEndpoints();

app.UseHttpsRedirection();

app.Run();
