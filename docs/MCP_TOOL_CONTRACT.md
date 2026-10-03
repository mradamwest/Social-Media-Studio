# Social Media Studio MCP Tool Contract

This document defines the external automation surface that will sit on top of the desktop app's existing services. The MCP transport must call these application services rather than platform APIs directly.

## Read tools

### list_connected_accounts
Returns provider connection state only. Never returns OAuth access tokens, refresh tokens, client secrets, or DPAPI payloads.

### get_post_status
Returns the state and per-network results for a previously created or published post.

## Write tools

### create_post
Creates a draft with caption/title/media references and selected networks. Does not publish.

### publish_now
Publishes through `AutomationPublishingService`, which delegates to the existing `PublishingCoordinator`. Each provider is validated independently and one provider failure does not stop the remaining providers.

### schedule_post
Stores a future publish time and selected networks. Scheduling must use the same publishing coordinator when execution time arrives.

### edit_post
Updates a draft or scheduled post before publication.

### delete_post
Deletes a draft/scheduled item. It must not imply deletion from social networks unless a provider-specific remote-delete implementation exists.

## Security rules

- Social-network credentials stay in Social Media Studio's secure token store.
- MCP responses must never expose OAuth tokens, refresh tokens, app secrets, or encrypted token-store contents.
- Publishing requires explicit target networks.
- Empty posts are rejected.
- Local media paths must be validated by the desktop app before use.
- External automation must not bypass `ISocialPublisher.ValidateAsync`.
- Tool errors should return sanitized messages rather than raw secrets or HTTP authorization headers.
- Read and write actions remain distinct so the ChatGPT/plugin permission layer can require confirmation for publishing or destructive actions.

## Initial implementation status

`AutomationPublishingService.PublishNowAsync` is the first application-facing entry point. It converts an `AutomationPostRequest` into the existing `PostDraft` model and delegates to `PublishingCoordinator.PublishAsync`.

The next implementation stage is the MCP transport/server that exposes this contract to ChatGPT.
