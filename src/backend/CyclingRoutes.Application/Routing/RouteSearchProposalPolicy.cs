namespace CyclingRoutes.Application.Routing;

public static class RouteSearchProposalPolicy
{
	public static bool IsValid(RouteSearchAdvice advice, RouteSearchContext context)
	{
		if (!Enum.IsDefined(advice.Action) || !Enum.IsDefined(advice.Reason)) return false;
		if (advice.Action == RouteSearchAction.Stop)
			return advice.Seed is null && advice.RequestedLengthMeters is null && advice.Reason == RouteSearchReason.Stop;
		return advice.Reason != RouteSearchReason.Stop
			&& advice.Seed is >= 3 and <= 16 && !context.Observations.Any(x => x.Seed == advice.Seed)
			&& advice.RequestedLengthMeters is { } length && double.IsFinite(length)
			&& length is >= 1000 and <= 100000
			&& length >= context.InitialLengthMeters * 0.5 && length <= context.InitialLengthMeters * 1.5;
	}
}
