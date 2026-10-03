namespace SocialMediaStudio.Models;
public enum PublishState { Draft, Scheduled, Publishing, Published, Failed, NeedsAttention }
public sealed class PostDraft {
 public string Caption { get; set; } = "";
 public string? Title { get; set; }
 public List<string> MediaFiles { get; } = [];
 public HashSet<string> Networks { get; } = [];
 public DateTimeOffset? ScheduledFor { get; set; }
 public PublishState State { get; set; } = PublishState.Draft;
}