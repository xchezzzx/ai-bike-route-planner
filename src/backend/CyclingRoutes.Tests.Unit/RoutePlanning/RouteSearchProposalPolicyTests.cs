using CyclingRoutes.Application.Routing;
using CyclingRoutes.Domain.RoutePlanning;

namespace CyclingRoutes.Tests.Unit.RoutePlanning;

public class RouteSearchProposalPolicyTests
{
	private static RouteSearchContext Context(double length = 20000) => new(
		new(RouteShape.Loop, CyclingProfile.Road, ElevationPreference.Balanced, length, null), length,
		[new(1, length, RouteSearchOutcome.Accepted, 25000, 4000, null, 5000, null),
		 new(2, length, RouteSearchOutcome.NoRoute, null, null, null, null, null)]);

	[Theory]
	[InlineData(2, 20000)] [InlineData(17, 20000)] [InlineData(0, 20000)]
	[InlineData(3, 999)] [InlineData(3, 100001)] [InlineData(3, 9800)] [InlineData(3, 30200)]
	[InlineData(3, double.NaN)] [InlineData(3, double.PositiveInfinity)] [InlineData(3, double.NegativeInfinity)]
	public void RejectsOutOfBoundsAndNonFiniteLength(int seed, double length) =>
		Assert.False(RouteSearchProposalPolicy.IsValid(new(RouteSearchAction.Search, seed, length, RouteSearchReason.Distance), Context()));

	[Theory]
	[InlineData(3, 10000, 20000)] [InlineData(16, 30000, 20000)]
	[InlineData(3, 1000, 2000)] [InlineData(16, 100000, 100000)]
	public void AcceptsInclusiveBounds(int seed, double length, double initial) =>
		Assert.True(RouteSearchProposalPolicy.IsValid(new(RouteSearchAction.Search, seed, length, RouteSearchReason.Explore), Context(initial)));

	[Fact]
	public void RejectsReusedSeedEvenInsideAdvisedRange()
	{
		var context = Context() with { Observations = [new(4, 20000, RouteSearchOutcome.NoRoute, null, null, null, null, null)] };
		Assert.False(RouteSearchProposalPolicy.IsValid(new(RouteSearchAction.Search, 4, 20000, RouteSearchReason.Explore), context));
	}

	[Fact]
	public void RequiresStrictStopAndSearchFields()
	{
		Assert.True(RouteSearchProposalPolicy.IsValid(new(RouteSearchAction.Stop, null, null, RouteSearchReason.Stop), Context()));
		RouteSearchAdvice[] invalid = [new(RouteSearchAction.Stop, 3, null, RouteSearchReason.Stop),
			new(RouteSearchAction.Stop, null, 20000, RouteSearchReason.Stop), new(RouteSearchAction.Stop, null, null, RouteSearchReason.Distance),
			new(RouteSearchAction.Search, null, 20000, RouteSearchReason.Distance), new(RouteSearchAction.Search, 3, null, RouteSearchReason.Distance),
			new(RouteSearchAction.Search, 3, 20000, RouteSearchReason.Stop), new((RouteSearchAction)99, 3, 20000, RouteSearchReason.Distance),
			new(RouteSearchAction.Search, 3, 20000, (RouteSearchReason)99)];
		Assert.All(invalid, advice => Assert.False(RouteSearchProposalPolicy.IsValid(advice, Context())));
	}
}
