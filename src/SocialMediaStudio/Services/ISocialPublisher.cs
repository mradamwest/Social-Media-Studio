using SocialMediaStudio.Models;
namespace SocialMediaStudio.Services;
public interface ISocialPublisher {
 string Provider { get; }
 Task ConnectAsync(CancellationToken cancellationToken = default);
 Task DisconnectAsync(CancellationToken cancellationToken = default);
 Task ValidateAsync(PostDraft draft, CancellationToken cancellationToken = default);
 Task PublishAsync(PostDraft draft, CancellationToken cancellationToken = default);
}