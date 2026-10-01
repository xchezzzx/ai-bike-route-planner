namespace CyclingRoutes.Infrastructure.Routing;

public sealed class GraphHopperOptions
{
	public string BaseUrl { get; init; } = "http://127.0.0.1:8989/";
	public string Profile { get; init; } = "road";

	public bool IsValid() => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
		&& uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0
		&& uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.AbsolutePath == "/"
		&& Profile is { Length: > 0 and <= 64 }
		&& Profile.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}
