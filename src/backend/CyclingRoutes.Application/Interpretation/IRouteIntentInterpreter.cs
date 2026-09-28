namespace CyclingRoutes.Application.Interpretation;

public interface IRouteIntentInterpreter
{
	Task<RouteIntentExtraction> InterpretAsync(string prompt, string locale, CancellationToken cancellationToken);
}
