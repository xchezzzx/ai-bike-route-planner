using CyclingRoutes.Contracts.RoutePlanning;

namespace CyclingRoutes.Application.Interpretation;

public sealed record InterpretationResult(InterpretRouteIntentResponse? Response, IReadOnlyDictionary<string, string[]> Errors);
