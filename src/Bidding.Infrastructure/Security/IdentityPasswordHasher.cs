using Bidding.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace Bidding.Infrastructure.Security;

/// <summary>ASP.NET Core Identity's hasher (PBKDF2, salted, versioned) without the full Identity stack.</summary>
internal sealed class IdentityPasswordHasher : Application.Common.IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(User user, string password) => _inner.HashPassword(user, password);

    public bool Verify(User user, string password) =>
        !string.IsNullOrEmpty(user.PasswordHash) &&
        _inner.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
}
