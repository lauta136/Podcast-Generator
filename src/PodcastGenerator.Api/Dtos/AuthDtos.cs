using System.ComponentModel.DataAnnotations;
using PodcastGenerator.Domain.Entities;

namespace PodcastGenerator.Api.Dtos;

public sealed class CredentialsRequest
{
    // Length and format rules live in AuthService, so sign-in never reveals them for a wrong password.
    [Required, StringLength(254)]
    public string Email { get; init; } = "";

    [Required, StringLength(128)]
    public string Password { get; init; } = "";
}

public sealed class RegisterRequest
{
    [Required, StringLength(254)]
    public string Email { get; init; } = "";

    [Required, StringLength(128)]
    public string Password { get; init; } = "";

    /// <summary>Needed only when the server is invite-only.</summary>
    [StringLength(128)]
    public string? InviteCode { get; init; }
}

/// <summary>What the sign-up form needs to know before the user types anything.</summary>
public sealed record RegistrationDto(bool InviteRequired);

public sealed class ChangePasswordRequest
{
    [Required, StringLength(128)]
    public string CurrentPassword { get; init; } = "";

    [Required, StringLength(128)]
    public string NewPassword { get; init; } = "";
}

public sealed class DeleteAccountRequest
{
    [Required, StringLength(128)]
    public string Password { get; init; } = "";
}

public sealed record UserDto(Guid Id, string? Email, string DisplayName)
{
    public static UserDto From(User u) => new(u.Id, u.Email, u.DisplayName);
}
