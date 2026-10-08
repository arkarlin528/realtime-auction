using Bidding.Domain.Common;

namespace Bidding.Domain.Users;

public enum UserRole
{
    Bidder = 1,
    Admin = 2,
}

public sealed class User : Entity
{
    private User() { }

    public User(string email, string displayName, UserRole role, bool isBot = false)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("Display name is required.");

        Email = email.Trim().ToLowerInvariant();
        DisplayName = displayName.Trim();
        Role = role;
        IsBot = isBot;
    }

    public string Email { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public UserRole Role { get; private set; }

    /// <summary>Simulated bidders that keep the public demo busy. They bid through the same rules as people.</summary>
    public bool IsBot { get; private set; }

    public void SetPasswordHash(string hash) => PasswordHash = hash;

    /// <summary>"Mali Kittisak" → "Mali K." Other bidders only ever see this short form.</summary>
    public static string PublicName(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "Bidder",
            1 => parts[0],
            _ => $"{parts[0]} {char.ToUpperInvariant(parts[^1][0])}.",
        };
    }
}
