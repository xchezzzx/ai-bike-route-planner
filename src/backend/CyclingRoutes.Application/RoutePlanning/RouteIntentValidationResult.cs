using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Application.RoutePlanning;

public sealed record RouteIntentValidationResult(
	RouteIntent? Intent,
	IReadOnlyDictionary<string, string[]> Errors);
