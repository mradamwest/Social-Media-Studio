using SocialMediaStudio.Models;

namespace SocialMediaStudio.Services;

public sealed class AccountStateService
{
    private readonly SecureTokenStore _tokens;

    public AccountStateService(SecureTokenStore tokens) => _tokens = tokens;

    public void Restore(IEnumerable<SocialAccount> accounts)
    {
        foreach (var account in accounts)
        {
            try
            {
                var token = _tokens.LoadOAuth(account.Provider);
                if (token is null)
                {
                    account.State = ConnectionState.Disconnected;
                    account.TokenExpiresAt = null;
                    continue;
                }

                account.TokenExpiresAt = token.ExpiresAt;
                account.State = token.ExpiresAt is not null &&
                                token.ExpiresAt <= DateTimeOffset.UtcNow
                    ? ConnectionState.Expired
                    : ConnectionState.Connected;
            }
            catch
            {
                account.State = ConnectionState.Error;
                account.TokenExpiresAt = null;
            }
        }
    }

    public void Disconnect(SocialAccount account)
    {
        _tokens.Delete(account.Provider);
        account.State = ConnectionState.Disconnected;
        account.DisplayName = string.Empty;
        account.TokenExpiresAt = null;
    }
}
