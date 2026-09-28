using System.Text.Json;
using CyclingRoutes.Application.RoutePlanning;
using CyclingRoutes.Contracts.RoutePlanning;

namespace CyclingRoutes.Tests.Integration;

public class PromptEvaluationCorpusTests
{
	[Fact]
	public void Corpus_HasExplicitMultilingualExpectationsAndValidReadyIntents()
	{
		using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "prompt-interpretation-v1.json")));
		var root = json.RootElement;
		Assert.Equal("prompt-interpretation-v1", root.GetProperty("contractVersion").GetString());
		var cases = root.GetProperty("cases").EnumerateArray().ToArray();
		Assert.True(cases.Length >= 25);
		Assert.Equal(cases.Length, cases.Select(x => x.GetProperty("id").GetString()).Distinct().Count());
		foreach (var locale in new[] { "en", "ru", "he" })
			Assert.Equal(6, cases.Count(x => x.GetProperty("core").GetBoolean() && x.GetProperty("request").GetProperty("locale").GetString() == locale));
		foreach (var item in cases)
		{
			var request = item.GetProperty("request").Deserialize<InterpretRouteIntentRequest>(JsonSerializerOptions.Web)!;
			Assert.False(string.IsNullOrWhiteSpace(request.Prompt)); Assert.InRange(request.Prompt.Length, 1, 4000);
			Assert.Contains(request.Locale, new[] { "en", "ru", "he" });
			var status = item.GetProperty("expectedStatus").GetString();
			Assert.Contains(status, new[] { "ready", "unsupported", "needsClarification" });
			Assert.Equal(JsonValueKind.Array, item.GetProperty("expectedClarifications").ValueKind);
			Assert.Equal(JsonValueKind.Array, item.GetProperty("expectedLimitations").ValueKind);
			Assert.Equal(JsonValueKind.Array, item.GetProperty("expectedAssumptions").ValueKind);
			foreach (var field in item.GetProperty("expectedFields").EnumerateObject())
				Assert.Matches(@"^(intent|draft)(\.(shape|profile|elevation|targetDistanceMeters|targetDurationSeconds|start\.(latitude|longitude)|destination\.(latitude|longitude)))?$", field.Name);
			if (status == "ready")
			{
				var fields = item.GetProperty("expectedFields");
				var intent = new RouteIntentRequest { Start = request.Start, Destination = request.Destination,
					Shape = fields.GetProperty("intent.shape").GetString(), Profile = fields.GetProperty("intent.profile").GetString(),
					Elevation = fields.GetProperty("intent.elevation").GetString(),
					TargetDistanceMeters = fields.TryGetProperty("intent.targetDistanceMeters", out var distance) && distance.ValueKind != JsonValueKind.Null ? distance.GetDouble() : null,
					TargetDurationSeconds = fields.TryGetProperty("intent.targetDurationSeconds", out var duration) && duration.ValueKind != JsonValueKind.Null ? duration.GetInt64() : null };
				Assert.Empty(new RouteIntentValidator().Validate(intent, TestContext.Current.CancellationToken).Errors);
			}
		}
	}
}
