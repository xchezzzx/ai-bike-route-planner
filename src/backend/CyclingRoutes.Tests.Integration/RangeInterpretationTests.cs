using System.Text.Json;
using CyclingRoutes.Application.Interpretation;
using CyclingRoutes.Infrastructure.Interpretation;
using static CyclingRoutes.Tests.Integration.GeminiRouteIntentInterpreterTests;

namespace CyclingRoutes.Tests.Integration;

public class RangeInterpretationTests
{
	private const string Ranges = """{"shape":"loop","profile":"road","elevation":null,"targetDistanceMeters":null,"targetDurationSeconds":null,"targetDistanceRangeMeters":{"min":18000,"max":22000},"targetDurationRangeSeconds":{"min":3000,"max":4200},"issues":[]}""";

	[Theory]
	[InlineData("en", "A road loop between 18 and 22 km, lasting 50 to 70 minutes.")]
	[InlineData("ru", "Шоссейное кольцо от 18 до 22 км, длительностью от 50 до 70 минут.")]
	[InlineData("he", "מסלול כביש מעגלי בין 18 ל-22 קילומטרים, במשך 50 עד 70 דקות.")]
	public async Task OfflineIntervalFixturesPreserveBothBoundsInDraftAndIntent(string locale, string prompt)
	{
		var result = await Interpret(Ranges, locale, prompt);
		Assert.Equal("ready", result.Status);
		var json = JsonSerializer.SerializeToElement(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
		foreach (var field in new[] { "draft", "intent" })
		{
			Assert.Equal(18000, json.GetProperty(field).GetProperty("targetDistanceRangeMeters").GetProperty("min").GetDouble());
			Assert.Equal(22000, json.GetProperty(field).GetProperty("targetDistanceRangeMeters").GetProperty("max").GetDouble());
			Assert.Equal(3000, json.GetProperty(field).GetProperty("targetDurationRangeSeconds").GetProperty("min").GetInt64());
			Assert.Equal(4200, json.GetProperty(field).GetProperty("targetDurationRangeSeconds").GetProperty("max").GetInt64());
			Assert.Equal(JsonValueKind.Null, json.GetProperty(field).GetProperty("targetDistanceMeters").ValueKind);
			Assert.Equal(JsonValueKind.Null, json.GetProperty(field).GetProperty("targetDurationSeconds").ValueKind);
		}
		Assert.Empty(result.Clarifications);
	}

	[Theory]
	[InlineData("{\"min\":1}")]
	[InlineData("{\"min\":1,\"max\":null}")]
	[InlineData("{\"min\":1,\"max\":2,\"extra\":3}")]
	[InlineData("{\"min\":1,\"min\":1,\"max\":2}")]
	[InlineData("{\"min\":1,\"Min\":1,\"max\":2}")]
	[InlineData("{\"min\":\"1\",\"max\":2}")]
	[InlineData("{\"min\":1,\"max\":1e999}")]
	public async Task MalformedExtractionRangesAreRejected(string range)
	{
		var error = await Assert.ThrowsAsync<InterpretationException>(() => Interpret(Ranges.Replace("{\"min\":18000,\"max\":22000}", range)));
		Assert.Equal(InterpretationFailure.InvalidResponse, error.Failure);
	}

	[Theory]
	[InlineData("{\"min\":1.5,\"max\":2}")]
	[InlineData("{\"min\":1,\"max\":9223372036854775808}")]
	public async Task DurationExtractionRequiresIntegerBounds(string range)
	{
		var error = await Assert.ThrowsAsync<InterpretationException>(() => Interpret(Ranges.Replace("{\"min\":3000,\"max\":4200}", range)));
		Assert.Equal(InterpretationFailure.InvalidResponse, error.Failure);
	}

	[Theory]
	[InlineData("18000", "-1", "targetDistanceRangeMeters.min", "must_be_positive")]
	[InlineData("18000", "23000", "targetDistanceRangeMeters", "range_reversed")]
	[InlineData("4200", "922337203686", "targetDurationRangeSeconds.max", "out_of_range")]
	public async Task InvalidSemanticBoundsStayInDraftForClarification(string from, string to, string field, string code)
	{
		var result = await Interpret(Ranges.Replace(from, to));
		Assert.Equal("needsClarification", result.Status);
		Assert.Null(result.Intent);
		Assert.Contains(result.Clarifications, x => x.Field == field && x.Code == code);
	}

	[Theory]
	[InlineData("targetDistanceRangeMeters")]
	[InlineData("targetDurationRangeSeconds")]
	public async Task AmbiguousRangeProducesOnlyItsQuestionAndPreservesDraft(string field)
	{
		var result = await Interpret(Ranges.Replace("\"issues\":[]", "\"issues\":[{\"field\":\"" + field + "\",\"code\":\"ambiguous\"}]"));
		Assert.Equal("needsClarification", result.Status);
		Assert.Equal(field, Assert.Single(result.Clarifications).Field);
		Assert.Null(result.Intent);
	}

	[Theory]
	[InlineData(false)] [InlineData(true)]
	public async Task CapabilityLimitUsesRangeMidpointEvenWithMissingStart(bool durationOnly)
	{
		var extraction = durationOnly
			? Ranges.Replace("{\"min\":18000,\"max\":22000}", "null").Replace("{\"min\":3000,\"max\":4200}", "{\"min\":18000,\"max\":20000}")
			: Ranges.Replace("{\"min\":18000,\"max\":22000}", "{\"min\":100000,\"max\":100002}");
		var result = await Interpret(extraction, missingStart: true);
		Assert.Equal("needsClarification", result.Status);
		Assert.Contains("loop_search_distance_out_of_range", result.Limitations);
	}

	private static async Task<CyclingRoutes.Contracts.RoutePlanning.InterpretRouteIntentResponse> Interpret(
		string extraction, string locale = "en", string prompt = "road loop between 18 and 22 km", bool missingStart = false)
	{
		using var client = new HttpClient(new Handler(async (request, ct) =>
		{
			using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
			Assert.False(json.RootElement.TryGetProperty("tools", out _));
			return Response(Envelope(extraction));
		}));
		var interpreter = new GeminiRouteIntentInterpreter(client, new() { ApiKey = "test", Model = "test" }, TimeProvider.System);
		return (await new InterpretationService(interpreter, new()).InterpretAsync(new()
		{
			Prompt = prompt, Locale = locale, Start = missingStart ? null : new() { Latitude = 32, Longitude = 34 }
		}, TestContext.Current.CancellationToken)).Response!;
	}
}
