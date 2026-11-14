namespace PodcastGenerator.Application.Auth;

public enum PasswordCheck
{
    Wrong,
    Correct,

    /// <summary>Correct, but hashed with outdated settings: store a fresh hash.</summary>
    CorrectNeedsRehash,
}

public interface IPasswordHasher
{
    string Hash(string password);

    PasswordCheck Verify(string hash, string password);
}
