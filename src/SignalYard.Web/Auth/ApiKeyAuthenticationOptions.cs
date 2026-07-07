using Microsoft.AspNetCore.Authentication;

namespace SignalYard.Web.Auth;

public static class ApiKeyAuthenticationDefaults
{
    public const string AuthenticationScheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
}

public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
}
