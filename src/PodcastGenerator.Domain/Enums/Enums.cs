namespace PodcastGenerator.Domain.Enums;

public enum EpisodeStatus
{
    Pending,
    GeneratingOutline,
    GeneratingSections,
    GeneratingAudio,
    Ready,
    Failed,

    /// <summary>Stopped because the monthly free TTS budget is used up.</summary>
    PausedForBudget,
}

/// <summary>Status of one resumable unit of work (a section or an audio chunk).</summary>
public enum StepStatus
{
    Pending,
    Completed,
    Failed,
}

public enum JobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    PausedForBudget,
}

/// <summary>TTS voice family. Each has its own free monthly character quota.</summary>
public enum TtsModelFamily
{
    WaveNet,
    Chirp3HD,
}

public enum EpisodeStyle
{
    Conversational,
    Storytelling,
    Explainer,
}

public enum DifficultyRating
{
    TooEasy,
    JustRight,
    TooHard,
}
