namespace SocialMediaStudio.Services;

public sealed record ProviderConnectionDefinition(
    string Provider,
    string ClientIdSetting,
    string AuthorizationEndpoint,
    string TokenEndpoint,
    string Scope,
    string DeveloperPortal);

public static class ProviderConnectionCatalog
{
    public static IReadOnlyDictionary<string, ProviderConnectionDefinition> Definitions { get; } =
        new Dictionary<string, ProviderConnectionDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Facebook"] = new(
                "Facebook",
                "SOCIAL_MEDIA_STUDIO_META_APP_ID",
                "https://www.facebook.com/v24.0/dialog/oauth",
                "https://graph.facebook.com/v24.0/oauth/access_token",
                "pages_show_list pages_read_engagement pages_manage_posts",
                "https://developers.facebook.com/"),
            ["Instagram"] = new(
                "Instagram",
                "SOCIAL_MEDIA_STUDIO_META_APP_ID",
                "https://www.facebook.com/v24.0/dialog/oauth",
                "https://graph.facebook.com/v24.0/oauth/access_token",
                "instagram_basic instagram_content_publish pages_show_list",
                "https://developers.facebook.com/"),
            ["Threads"] = new(
                "Threads",
                "SOCIAL_MEDIA_STUDIO_THREADS_APP_ID",
                "https://threads.net/oauth/authorize",
                "https://graph.threads.net/oauth/access_token",
                "threads_basic threads_content_publish",
                "https://developers.facebook.com/")
        };

    public static bool TryCreateSettings(string provider, out OAuthProviderSettings? settings)
    {
        settings = null;
        if (!Definitions.TryGetValue(provider, out var definition)) return false;

        var clientId = Environment.GetEnvironmentVariable(definition.ClientIdSetting);
        if (string.IsNullOrWhiteSpace(clientId)) return false;

        settings = new OAuthProviderSettings(
            definition.Provider,
            clientId,
            definition.AuthorizationEndpoint,
            definition.TokenEndpoint,
            definition.Scope);
        return true;
    }
}
