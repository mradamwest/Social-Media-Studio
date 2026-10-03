namespace SocialMediaStudio.Models;
public enum ConnectionState { Disconnected, Connecting, Connected, Expired, Error }
public sealed class SocialAccount {
 public required string Provider { get; init; }
 public string DisplayName { get; set; } = "";
 public ConnectionState State { get; set; } = ConnectionState.Disconnected;
 public DateTimeOffset? TokenExpiresAt { get; set; }
}