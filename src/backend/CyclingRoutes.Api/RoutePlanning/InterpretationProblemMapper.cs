using CyclingRoutes.Application.Interpretation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CyclingRoutes.Api.RoutePlanning;

internal static class InterpretationProblemMapper
{
	public static ProblemHttpResult ToProblem(InterpretationFailure failure)
	{
		var (status, code) = failure switch
		{
			InterpretationFailure.NotConfigured => (503, "ai_not_configured"),
			InterpretationFailure.CredentialsRejected => (503, "ai_credentials_rejected"),
			InterpretationFailure.RateLimited => (503, "ai_rate_limited"),
			InterpretationFailure.Unavailable => (503, "ai_unavailable"),
			InterpretationFailure.Timeout => (504, "ai_timeout"),
			InterpretationFailure.RequestRejected => (422, "ai_request_rejected"),
			InterpretationFailure.InvalidResponse => (502, "ai_invalid_response"),
			_ => throw new InvalidOperationException("Unknown interpretation failure.")
		};
		return TypedResults.Problem(statusCode: status, title: "Prompt interpretation failed.",
			extensions: new Dictionary<string, object?> { ["code"] = code });
	}
}
