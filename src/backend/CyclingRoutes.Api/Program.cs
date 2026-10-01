using System.Text.Json.Serialization;
using CyclingRoutes.Api.Access;
using CyclingRoutes.Application.Interpretation;
using CyclingRoutes.Infrastructure.Interpretation;
using CyclingRoutes.Api.RoutePlanning;
using CyclingRoutes.Application.RoutePlanning;
using CyclingRoutes.Application.Routing;
using CyclingRoutes.Infrastructure.Routing;
using CyclingRoutes.Infrastructure.Naming;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<AccessOptions>()
	.Configure(options =>
	{
		builder.Configuration.GetSection("Access").Bind(options);
		options.Mode = builder.Configuration["Access:Mode"] ??
			(builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? "Local" : "Protected");
		options.Password ??= "";
		options.PublicOrigin ??= "";
	})
	.Validate(options => options.IsValid(), "Access requires Local mode or Protected mode with a password of at least 24 characters (at most 750 UTF-8 credential bytes) and a canonical HTTPS PublicOrigin without path, query or fragment.")
	.ValidateOnStart();
builder.Services.AddSingleton<AccessRateLimits>();
builder.Services.AddRateLimiter(options =>
{
	options.GlobalLimiter = AccessRateLimits.CreateApiLimiter();
	options.OnRejected = (context, _) => new ValueTask(AccessRateLimits.RejectAsync(context.HttpContext, context.Lease));
});

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
builder.Services.AddOptions<RoutingOptions>()
	.Bind(builder.Configuration.GetSection("Routing"))
	.Validate(options => options.IsValid(), "Routing requires OpenRouteService or GraphHopper with a root HTTP(S) URL without credentials, query or fragment and a valid profile name.")
	.ValidateOnStart();
builder.Services.AddHttpClient<IRoutingProvider>(client =>
{
	client.Timeout = TimeSpan.FromSeconds(15);
	client.MaxResponseContentBufferSize = 8 * 1024 * 1024;
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
	.AddTypedClient<IRoutingProvider>((client, services) =>
	{
		var options = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RoutingOptions>>().Value;
		return options.Provider == "GraphHopper"
			? new GraphHopperProvider(client, options.GraphHopper)
			: new OpenRouteServiceProvider(client, services.GetRequiredService<OpenRouteServiceOptions>());
	});
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
// Validate before entering the hosting loop so startup failures are deterministic.
_ = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AccessOptions>>().Value;
_ = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RoutingOptions>>().Value;
app.UseMiddleware<AccessMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRouting();
app.UseRateLimiter();
// A routing fallback can mask API 404/405/415 responses. Serve the SPA only after
// routing/static files have declined a non-file, non-API GET/HEAD request.
app.Use(async (context, next) =>
{
	await next(context);
	if (context.GetEndpoint() is not null || context.Response.HasStarted || context.Response.StatusCode != 404 ||
		context.Request.Path.StartsWithSegments("/api") || Path.HasExtension(context.Request.Path.Value) ||
		!(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))) return;

	var index = app.Environment.WebRootFileProvider.GetFileInfo("index.html");
	if (!index.Exists) return;
	context.Response.StatusCode = StatusCodes.Status200OK;
	await Results.File(index.CreateReadStream(), "text/html; charset=utf-8").ExecuteAsync(context);
});
app.UseDefaultFiles();
app.UseStaticFiles();

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

// Render terminates TLS and redirects at the edge. Do not trust forwarded headers
// or redirect this internal HTTP hop, which would loop behind the TLS terminator.
app.Run();
