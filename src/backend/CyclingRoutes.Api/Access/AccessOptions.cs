namespace CyclingRoutes.Api.Access;

public sealed class AccessOptions
{
	public string Mode { get; set; } = "Protected";
	public string Password { get; set; } = "";
	public string PublicOrigin { get; set; } = "";
	public bool IsLocal => Mode == "Local";

	public bool IsValid() => IsLocal || Mode == "Protected" &&
		Password.Length >= 24 && System.Text.Encoding.UTF8.GetByteCount("tester:" + Password) <= 750 &&
		!string.IsNullOrWhiteSpace(Password) &&
		Uri.TryCreate(PublicOrigin, UriKind.Absolute, out var uri) &&
		uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) &&
		PublicOrigin == uri.GetLeftPart(UriPartial.Authority);
}
