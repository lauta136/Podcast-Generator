namespace PodcastGenerator.Domain.Entities;

/// <summary>
/// A word or phrase the learner saved from a transcript, with its dictionary form, its meaning in that sentence and
/// a flashcard schedule (Leitner boxes). The sentence is copied in, so the card keeps its context if the episode goes.
/// </summary>
public class VocabularyItem
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Where it was saved; null once that episode is deleted.</summary>
    public Guid? EpisodeId { get; set; }

    public Guid? SegmentId { get; set; }

    /// <summary>Exactly what was selected, e.g. "Kaufleute".</summary>
    public required string Text { get; set; }

    /// <summary>Dictionary form, e.g. "der Kaufmann, die Kaufleute".</summary>
    public required string BaseForm { get; set; }

    /// <summary>Meaning in the sentence, in <see cref="TranslationLanguage"/>.</summary>
    public required string Translation { get; set; }

    public required string TranslationLanguage { get; set; }

    /// <summary>The sentence it came from.</summary>
    public required string Context { get; set; }

    /// <summary>0 = learning (due again soon); each "Got it" moves the card up, to longer gaps.</summary>
    public int Box { get; set; }

    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? LastReviewedAt { get; set; }
    public int ReviewCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
