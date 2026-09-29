using System.Globalization;
using CyclingRoutes.Application.Interpretation;
using CyclingRoutes.Application.RoutePlanning;
using CyclingRoutes.Contracts.RoutePlanning;

namespace CyclingRoutes.Tests.Unit.RoutePlanning;

public class InterpretationServiceTests
{
	private static readonly RouteIntentExtraction Complete = new("loop", "road", null, 20000, null, []);
	private static InterpretRouteIntentRequest Request() => new() { Prompt = "A road loop of 20 km", Locale = "en", Start = new() { Latitude = 32, Longitude = 34 } };
	private static Task<InterpretationResult> Run(RouteIntentExtraction extraction, InterpretRouteIntentRequest? request = null) =>
		new InterpretationService(new Stub(extraction), new RouteIntentValidator()).InterpretAsync(request ?? Request(), TestContext.Current.CancellationToken);

	[Fact]
	public async Task CompleteLoop_ReturnsValidatedIntentAndExplicitDefault()
	{
		var result = await Run(Complete);
		Assert.Empty(result.Errors);
		Assert.Equal("ready", result.Response!.Status);
		Assert.Equal(20000, result.Response.Intent!.TargetDistanceMeters);
		Assert.Equal("balanced", result.Response.Intent.Elevation);
		Assert.Equal(new[] { "elevation_balanced" }, result.Response.Assumptions);
		Assert.Equal(32, result.Response.Intent.Start.Latitude);
	}

	[Theory]
	[InlineData("prompt", "")]
	[InlineData("prompt", "   ")]
	[InlineData("prompt", null)]
	[InlineData("locale", "EN")]
	[InlineData("locale", null)]
	[InlineData("start", "invalid")]
	[InlineData("destination", "invalid")]
	[InlineData("length", "invalid")]
	public async Task InvalidEnvelope_DoesNotCallProvider(string field, string? value)
	{
		var request = Request();
		request = field switch
		{
			"prompt" => request with { Prompt = value }, "locale" => request with { Locale = value },
			"start" => request with { Start = new() { Latitude = 91, Longitude = double.NaN } },
			"destination" => request with { Destination = new() },
			_ => request with { Prompt = new string('a', 4001) }
		};
		var stub = new Stub(Complete);
		var result = await new InterpretationService(stub, new()).InterpretAsync(request, TestContext.Current.CancellationToken);
		Assert.Null(result.Response);
		Assert.NotEmpty(result.Errors);
		Assert.Equal(0, stub.Calls);
	}

	[Theory]
	[InlineData("en", "en-US")]
	[InlineData("ru", "ru-RU")]
	[InlineData("he", "he-IL")]
	public async Task LocaleAndCulture_DoNotChangeNumbers(string locale, string culture)
	{
		var old = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
			var stub = new Stub(Complete with { TargetDistanceMeters = 20000.5, TargetDurationSeconds = 3600 });
			var request = Request() with { Prompt = new string('ש', 4000), Locale = locale };
			var result = await new InterpretationService(stub, new()).InterpretAsync(request, TestContext.Current.CancellationToken);
			Assert.Equal(20000.5, result.Response!.Intent!.TargetDistanceMeters);
			Assert.Equal(3600, result.Response.Intent.TargetDurationSeconds);
			Assert.Equal((request.Prompt, locale), stub.Input);
			Assert.Equal(1, stub.Calls);
		}
		finally { CultureInfo.CurrentCulture = old; }
	}

	[Theory]
	[InlineData("ambiguous")]
	[InlineData("invalid_value")]
	public async Task AmbiguousDistance_PreservesDraftButBlocksIntent(string code)
	{
		var result = (await Run(Complete with { Issues = [new("targetDistanceMeters", code), new("targetDistanceMeters", code)] })).Response!;
		Assert.Equal("needsClarification", result.Status);
		Assert.Equal(20000, result.Draft.TargetDistanceMeters);
		Assert.Null(result.Intent);
		var question = Assert.Single(result.Clarifications);
		Assert.Equal(code, question.Code);
	}

	[Fact]
	public async Task SelectedPointToPointWithoutTargets_ReturnsReady()
	{
		var result = (await Run(Complete with { Shape = "pointToPoint", TargetDistanceMeters = null },
			Request() with { Destination = new() { Latitude = 32.2, Longitude = 34.8 } })).Response!;
		Assert.Equal("ready", result.Status);
		Assert.Empty(result.Clarifications);
		Assert.NotNull(result.Intent);
		Assert.Null(result.Intent.TargetDistanceMeters);
		Assert.Null(result.Intent.TargetDurationSeconds);
	}

	[Fact]
	public async Task MissingTargets_ProducesOneQuestion()
	{
		var result = (await Run(Complete with { TargetDistanceMeters = null })).Response!;
		Assert.Null(result.Intent);
		Assert.Equal("target_required", Assert.Single(result.Clarifications).Code);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public async Task InvalidTargets_AreNotCorrected(double distance)
	{
		var result = (await Run(Complete with { TargetDistanceMeters = distance })).Response!;
		Assert.Equal(distance, result.Draft.TargetDistanceMeters);
		Assert.Equal("must_be_positive", Assert.Single(result.Clarifications).Code);
	}

	[Theory]
	[InlineData(999, "unsupported")]
	[InlineData(1000, "ready")]
	[InlineData(100000, "ready")]
	[InlineData(100001, "unsupported")]
	public async Task DistanceLimits_MatchCandidateCapabilities(double meters, string status)
	{
		var result = (await Run(Complete with { TargetDistanceMeters = meters })).Response!;
		Assert.Equal(status, result.Status);
		Assert.NotNull(result.Intent);
		Assert.Equal(status == "unsupported", result.Limitations.Contains("loop_search_distance_out_of_range"));
	}

	[Theory]
	[InlineData(179, "unsupported")]
	[InlineData(180, "ready")]
	[InlineData(18000, "ready")]
	[InlineData(18001, "unsupported")]
	[InlineData(922337203685, "unsupported")]
	[InlineData(922337203686, "needsClarification")]
	[InlineData(0, "needsClarification")]
	[InlineData(-1, "needsClarification")]
	public async Task DurationLimits_AreExplicit(long seconds, string status)
	{
		var result = (await Run(Complete with { TargetDistanceMeters = null, TargetDurationSeconds = seconds })).Response!;
		Assert.Equal(status, result.Status);
	}

	[Fact]
	public async Task UnsupportedPreferences_NeverSilentlyBecomeReady()
	{
		var result = (await Run(Complete with { Issues = [new("prompt", "unsupported_preference")] })).Response!;
		Assert.Equal("unsupported", result.Status);
		Assert.Null(result.Intent);
		Assert.Equal(new[] { "unsupported_preference" }, result.Limitations);
		var gravel = (await Run(Complete with { Profile = "gravel" })).Response!;
		Assert.Equal("unsupported", gravel.Status);
		Assert.NotNull(gravel.Intent);
		Assert.Contains("gravel_not_supported", gravel.Limitations);
	}

	[Theory]
	[InlineData("missing", "required")]
	[InlineData("equal", "must_differ_from_start")]
	[InlineData("loop", "destination_not_allowed")]
	public async Task DestinationInvariants_ArePreserved(string scenario, string code)
	{
		var request = Request() with { Destination = scenario == "missing" ? null : Request().Start };
		var result = (await Run(Complete with { Shape = scenario == "loop" ? "loop" : "pointToPoint" }, request)).Response!;
		Assert.Null(result.Intent);
		Assert.Contains(result.Clarifications, x => x.Field == "destination" && x.Code == code);
	}

	[Fact]
	public async Task PointToPointClimbs_AreNotSupported()
	{
		var result = (await Run(Complete with { Shape = "pointToPoint", Elevation = "seekClimbs" },
			Request() with { Destination = new() { Latitude = 33, Longitude = 35 } })).Response!;
		Assert.Equal("unsupported", result.Status);
		Assert.Contains("point_to_point_elevation_not_supported", result.Limitations);
	}

	[Fact]
	public async Task Questions_AreOrderedLocalizedAndNotRepeated()
	{
		var messages = new List<string>();
		foreach (var locale in new[] { "en", "ru", "he" })
		{
			var result = (await Run(new(null, null, null, null, null, []), Request() with { Start = null, Locale = locale })).Response!;
			Assert.Equal(new[] { "start", "shape", "profile", "targetDistanceMeters" }, result.Clarifications.Select(x => x.Field));
			Assert.All(result.Clarifications, x => Assert.False(string.IsNullOrWhiteSpace(x.Message)));
			messages.Add(result.Clarifications[0].Message);
		}
		Assert.Equal(3, messages.Distinct().Count());
	}

	[Theory]
	[InlineData("start", "location_requires_map_selection")]
	[InlineData("prompt", "ambiguous")]
	[InlineData("elevation", "ambiguous")]
	public async Task UnresolvedIssues_BlockIntent(string field, string code)
	{
		var result = (await Run(Complete with { Issues = [new(field, code)] })).Response!;
		Assert.Null(result.Intent);
		Assert.Equal("needsClarification", result.Status);
		if (field == "elevation") Assert.Empty(result.Assumptions);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task CallerCancellation_PropagatesBeforeAndAfterProvider(bool before)
	{
		using var source = new CancellationTokenSource();
		var stub = new Stub(Complete, before ? null : source.Cancel);
		if (before) source.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new InterpretationService(stub, new()).InterpretAsync(Request(), source.Token));
		Assert.Equal(before ? 0 : 1, stub.Calls);
	}

	[Theory]
	[InlineData("ambiguous")]
	[InlineData("invalid_value")]
	public async Task UnresolvedElevation_DoesNotInventDraftDefault(string code)
	{
		var result = (await Run(Complete with { Elevation = null, Issues = [new("elevation", code)] })).Response!;
		Assert.Null(result.Draft.Elevation);
		Assert.Null(result.Intent);
		Assert.Empty(result.Assumptions);
		Assert.Contains(result.Clarifications, x => x.Field == "elevation" && x.Code == code);
	}

	[Theory]
	[InlineData(true, null)]
	[InlineData(false, 0L)]
	[InlineData(false, 922337203686L)]
	public async Task KnownDistanceLimit_RemainsVisibleAlongsideUnrelatedQuestions(bool missingStart, long? duration)
	{
		var result = (await Run(Complete with { TargetDistanceMeters = 100001, TargetDurationSeconds = duration },
			Request() with { Start = missingStart ? null : Request().Start })).Response!;
		Assert.Equal("needsClarification", result.Status);
		Assert.Contains("loop_search_distance_out_of_range", result.Limitations);
		Assert.Null(result.Intent);
	}

	[Fact]
	public async Task KnownDurationLimit_RemainsVisibleWhenStartIsMissing()
	{
		var result = (await Run(Complete with { TargetDistanceMeters = null, TargetDurationSeconds = 18001 },
			Request() with { Start = null })).Response!;
		Assert.Contains("loop_search_distance_out_of_range", result.Limitations);
		Assert.Equal("needsClarification", result.Status);
	}

	[Theory]
	[InlineData("targetDistanceMeters")]
	[InlineData("shape")]
	[InlineData("profile")]
	public async Task AmbiguousSearchFields_DoNotClaimDistanceLimitation(string field)
	{
		var result = (await Run(Complete with { TargetDistanceMeters = 100001, Issues = [new(field, "ambiguous")] })).Response!;
		Assert.DoesNotContain("loop_search_distance_out_of_range", result.Limitations);
	}

	private sealed class Stub(RouteIntentExtraction value, Action? action = null) : IRouteIntentInterpreter
	{
		public int Calls { get; private set; }
		public (string, string) Input { get; private set; }
		public Task<RouteIntentExtraction> InterpretAsync(string prompt, string locale, CancellationToken cancellationToken)
		{
			Calls++; Input = (prompt, locale); action?.Invoke(); return Task.FromResult(value);
		}
	}
}
