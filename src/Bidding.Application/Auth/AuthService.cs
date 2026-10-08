using Bidding.Application.Common;
using Bidding.Domain.Users;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Bidding.Application.Auth;

public sealed record LoginRequest(string Email, string Password);

public sealed record RegisterRequest(string Email, string DisplayName, string Password);

public sealed record UserDto(int Id, string Email, string DisplayName, UserRole Role);

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserDto User);

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(200);
    }
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10).MaximumLength(200)
            .WithMessage("Use at least 10 characters.");
    }
}

public sealed class AuthService(IAppDbContext db, IPasswordHasher hasher, ITokenService tokens, ICurrentUser currentUser)
{
    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email && !u.IsBot, ct);
        if (user is null || !hasher.Verify(user, request.Password))
            return null;
        return Issue(user);
    }

    /// <summary>Anyone can sign up as a bidder so reviewers can try the demo with their own account.</summary>
    public async Task<LoginResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("An account with this email already exists.");

        var user = new User(email, request.DisplayName, UserRole.Bidder);
        user.SetPasswordHash(hasher.Hash(user, request.Password));
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return Issue(user);
    }

    public async Task<UserDto> MeAsync(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == currentUser.UserId, ct)
            ?? throw new NotFoundException("User was not found.");
        return ToDto(user);
    }

    private LoginResponse Issue(User user)
    {
        var (token, expiresAt) = tokens.CreateToken(user);
        return new LoginResponse(token, expiresAt, ToDto(user));
    }

    private static UserDto ToDto(User u) => new(u.Id, u.Email, u.DisplayName, u.Role);
}
