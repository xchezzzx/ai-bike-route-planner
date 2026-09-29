namespace CyclingRoutes.Application.Routing;

public enum RouteSearchAdvisorFailure { NotConfigured, Authentication, Quota, Unavailable, Timeout, InvalidResponse }

public enum RouteSearchAdvisorDiagnostic
{
	MalformedEnvelope, PromptBlocked, OutputTokenLimit, IncompleteCandidate, InvalidParts,
	InvalidAdviceJson, InvalidAdviceFields, MissingAdviceFields, InvalidAdviceReason,
	InvalidAdviceSeed, InvalidAdviceLength, SearchSeedInvalid, SearchLengthInvalid,
	HttpError, ResponseTooLarge, InvalidUtf8, TransportError, Deadline
}

public sealed class RouteSearchAdvisorException(RouteSearchAdvisorFailure failure,
	RouteSearchAdvisorDiagnostic? diagnostic = null, int? httpStatusCode = null) : Exception("Route search advisor failed.")
{
	public RouteSearchAdvisorFailure Failure { get; } = failure;
	public RouteSearchAdvisorDiagnostic? Diagnostic { get; } = diagnostic;
	public int? HttpStatusCode { get; } = httpStatusCode;
}
