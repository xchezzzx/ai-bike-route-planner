using System.Globalization;
using System.Text;
using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Application.Routing;

public sealed record ResolvedRouteName(string Value, string? Attribution);

public sealed class RouteNameResolver(ISettlementLookup settlements)
{
	public ResolvedRouteName Resolve(RoutedPath path, RouteShape shape, CyclingProfile profile)
	{
		var start = path.Points.Count > 0 ? settlements.FindNearest(path.Points[0].Position) : null;
		var end = shape == RouteShape.PointToPoint && path.Points.Count > 0
			? settlements.FindNearest(path.Points[^1].Position) : null;
		var first = SafePlace(start?.AsciiName);
		var last = SafePlace(end?.AsciiName);
		var place = shape == RouteShape.Loop ? (first ?? "Route") + "-loop"
			: first is null && last is null ? "Route"
			: first is not null && last is not null && start!.Id == end!.Id ? first
			: (first ?? "Unknown") + "-" + (last ?? "Unknown");
		var label = profile switch { CyclingProfile.Road => "road", CyclingProfile.Gravel => "gravel", _ => "cycling" };
		var kilometers = Math.Round(path.DistanceMeters / 1000, MidpointRounding.AwayFromZero);
		// Invalid provider metrics must not produce enormous or non-finite filename tokens.
		var distance = double.IsFinite(kilometers) && path.DistanceMeters >= 0 && kilometers <= 999999999
			? kilometers.ToString("0", CultureInfo.InvariantCulture) : "unknown";
		return new($"{place}-{label}-{distance}", first is not null || last is not null ? settlements.Attribution : null);
	}

	private static string? SafePlace(string? value)
	{
		if (string.IsNullOrWhiteSpace(value)) return null;
		var output = new StringBuilder(40);
		foreach (var character in value.Normalize(NormalizationForm.FormD))
		{
			if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
			if (char.IsAsciiLetterOrDigit(character)) output.Append(character);
			else if (output.Length > 0 && output[^1] != '-') output.Append('-');
			if (output.Length == 40) break;
		}
		var result = output.ToString().TrimEnd('-');
		return result.Length == 0 ? null : result;
	}
}
