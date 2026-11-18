namespace PodcastGenerator.Application.Auth;

/// <summary>Bound from "Auth:Registration". Who may create an account.</summary>
public class RegistrationOptions
{
    public const string SectionName = "Auth:Registration";

    /// <summary>
    /// When set, a new account needs this code. Every account can create episodes, which spends the LLM and TTS
    /// quotas, so a public server should set it and share the code only with people it invites. Empty means
    /// anyone can sign up (fine on your own machine).
    /// </summary>
    public string InviteCode { get; set; } = "";

    public bool InviteRequired => !string.IsNullOrWhiteSpace(InviteCode);
}
