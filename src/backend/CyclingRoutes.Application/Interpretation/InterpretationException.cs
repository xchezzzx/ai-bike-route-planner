namespace CyclingRoutes.Application.Interpretation;

public enum InterpretationFailure
{
	NotConfigured, CredentialsRejected, RateLimited, Unavailable, Timeout, RequestRejected, InvalidResponse
}

public sealed class InterpretationException(InterpretationFailure failure)
	: Exception("Prompt interpretation failed.")
{
	public InterpretationFailure Failure { get; } = failure;
}
