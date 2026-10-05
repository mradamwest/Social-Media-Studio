# Account connection deployment

The desktop client and server implement browser OAuth sign-in without asking normal users for developer credentials. This is **not a live integration yet**: no production host, registered app credentials, or provider approvals have been supplied or verified.

## Publish the service

GitHub Actions produces a `Social-Media-Studio-Connection-Service` artifact. Run it with the ASP.NET Core 8 runtime, or build the Dockerfile from the repository root:

```sh
docker build -f src/SocialMediaStudio.ConnectionService/Dockerfile -t social-media-studio-connections .
```

Host behind an HTTPS reverse proxy. Set `CONNECTION_SERVICE_PUBLIC_URL` to the HTTPS origin (no path, query, or user info). Keep request/response body logging off. The reverse proxy must not log OAuth callback query strings or connection proof headers. `/health` is the health probe.

This implementation stores five-minute connection sessions in process memory. Use a single server instance; restarting it requires pending sign-ins to be started again. Do not deploy multiple replicas until sessions are moved to shared storage. Provider tokens are returned to the initiating desktop once, protected by a separate proof; the desktop encrypts them for its Windows user.

## Configure registered applications

Set the provider client IDs from `ProviderConnectionCatalog` as server environment variables. Use a server secret manager for secrets; never commit them or place them in the installer.

| Provider | Client ID/key variable | Server secret variable |
|---|---|---|
| Facebook / Instagram | SOCIAL_MEDIA_STUDIO_META_APP_ID | SOCIAL_MEDIA_STUDIO_META_APP_SECRET |
| Threads | SOCIAL_MEDIA_STUDIO_THREADS_APP_ID | SOCIAL_MEDIA_STUDIO_THREADS_APP_SECRET |
| YouTube / Google Business Profile | SOCIAL_MEDIA_STUDIO_GOOGLE_CLIENT_ID | SOCIAL_MEDIA_STUDIO_GOOGLE_CLIENT_SECRET |
| TikTok | SOCIAL_MEDIA_STUDIO_TIKTOK_CLIENT_KEY | SOCIAL_MEDIA_STUDIO_TIKTOK_CLIENT_SECRET |
| LinkedIn | SOCIAL_MEDIA_STUDIO_LINKEDIN_CLIENT_ID | SOCIAL_MEDIA_STUDIO_LINKEDIN_CLIENT_SECRET |
| X | SOCIAL_MEDIA_STUDIO_X_CLIENT_ID | Public PKCE client |
| Pinterest | SOCIAL_MEDIA_STUDIO_PINTEREST_APP_ID | SOCIAL_MEDIA_STUDIO_PINTEREST_APP_SECRET |

Google credentials for this service must be registered as a **web application**, not a desktop application. LinkedIn uses its server authorization endpoint. Register each exact callback URI as `https://YOUR_HOST/oauth/callback/PROVIDER` (percent-encode spaces in Google Business Profile).

Registrations, scopes, eligible account types, review/audit requirements, and provider endpoint compatibility require actual portal configuration and live testing. In particular, TikTok public direct posting requires its separate audit. Automated session tests do not prove provider approval or live publishing.

## Configure the desktop release

Set `serviceUrl` in `src/SocialMediaStudio/connection-service.json` to the deployed HTTPS origin before publishing the installer. It is public configuration, containing no app secrets. `SOCIAL_MEDIA_STUDIO_CONNECTION_SERVICE_URL` overrides this for testing. A null URL intentionally leaves connections unavailable; no dummy endpoint is shipped.

Manual credential setup remains only in explicitly enabled development mode (`SOCIAL_MEDIA_STUDIO_DEVELOPER_MODE=1`). Normal installations show an unavailable connection notice until the publisher configures the service.

The hosted renewal path is initially wired for YouTube. Other providers require reconnection on expiration until provider-specific renewal support is implemented. Live token exchange, renewal, publishing, consent cancellation, and clean Windows installation must be tested after configuration.

## Research sources

- https://learn.microsoft.com/en-us/windows/apps/develop/security/oauth2
- https://developers.google.com/identity/protocols/oauth2/native-app
- https://developers.tiktok.com/docs/en/content-sharing-guidelines
