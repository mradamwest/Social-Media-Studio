namespace SocialMediaStudio.Services;

public sealed record ProviderConnectionDefinition(string Provider,string ClientIdSetting,string AuthorizationEndpoint,string TokenEndpoint,string Scope,string DeveloperPortal,string? ClientSecretSetting = null, bool UsePkce = true, bool RequestOfflineAccess = false);

public static class ProviderConnectionCatalog
{
    public static IReadOnlyDictionary<string, ProviderConnectionDefinition> Definitions { get; } =
        new Dictionary<string, ProviderConnectionDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["Facebook"]=new("Facebook","SOCIAL_MEDIA_STUDIO_META_APP_ID","https://www.facebook.com/v24.0/dialog/oauth","https://graph.facebook.com/v24.0/oauth/access_token","pages_show_list pages_read_engagement pages_manage_posts","https://developers.facebook.com/","SOCIAL_MEDIA_STUDIO_META_APP_SECRET", UsePkce: false),
            ["Instagram"]=new("Instagram","SOCIAL_MEDIA_STUDIO_META_APP_ID","https://www.facebook.com/v24.0/dialog/oauth","https://graph.facebook.com/v24.0/oauth/access_token","instagram_basic instagram_content_publish pages_show_list","https://developers.facebook.com/","SOCIAL_MEDIA_STUDIO_META_APP_SECRET", UsePkce: false),
            ["Threads"]=new("Threads","SOCIAL_MEDIA_STUDIO_THREADS_APP_ID","https://threads.net/oauth/authorize","https://graph.threads.net/oauth/access_token","threads_basic threads_content_publish","https://developers.facebook.com/"),
            ["YouTube"]=new("YouTube","SOCIAL_MEDIA_STUDIO_GOOGLE_CLIENT_ID","https://accounts.google.com/o/oauth2/v2/auth","https://oauth2.googleapis.com/token","https://www.googleapis.com/auth/youtube.upload https://www.googleapis.com/auth/youtube.readonly","https://console.cloud.google.com/", RequestOfflineAccess: true),
            ["TikTok"]=new("TikTok","SOCIAL_MEDIA_STUDIO_TIKTOK_CLIENT_KEY","https://www.tiktok.com/v2/auth/authorize/","https://open.tiktokapis.com/v2/oauth/token/","user.info.basic video.publish video.upload","https://developers.tiktok.com/"),
            ["LinkedIn"]=new("LinkedIn","SOCIAL_MEDIA_STUDIO_LINKEDIN_CLIENT_ID","https://www.linkedin.com/oauth/v2/authorization","https://www.linkedin.com/oauth/v2/accessToken","openid profile w_member_social","https://www.linkedin.com/developers/"),
            ["X"]=new("X","SOCIAL_MEDIA_STUDIO_X_CLIENT_ID","https://twitter.com/i/oauth2/authorize","https://api.x.com/2/oauth2/token","tweet.read tweet.write users.read offline.access","https://developer.x.com/"),
            ["Pinterest"]=new("Pinterest","SOCIAL_MEDIA_STUDIO_PINTEREST_APP_ID","https://www.pinterest.com/oauth/","https://api.pinterest.com/v5/oauth/token","boards:read pins:read pins:write user_accounts:read","https://developers.pinterest.com/"),
            ["Google Business Profile"]=new("Google Business Profile","SOCIAL_MEDIA_STUDIO_GOOGLE_CLIENT_ID","https://accounts.google.com/o/oauth2/v2/auth","https://oauth2.googleapis.com/token","https://www.googleapis.com/auth/business.manage","https://console.cloud.google.com/")
        };

    public static bool TryCreateSettings(string provider, out OAuthProviderSettings? settings)
    {
        settings=null;
        if(!Definitions.TryGetValue(provider,out var d)) return false;
        var id=Environment.GetEnvironmentVariable(d.ClientIdSetting);
        if(string.IsNullOrWhiteSpace(id)) return false;
        settings=new OAuthProviderSettings(d.Provider,id,d.AuthorizationEndpoint,d.TokenEndpoint,d.Scope);
        return true;
    }
}
