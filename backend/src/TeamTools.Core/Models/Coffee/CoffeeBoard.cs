using TeamTools.Core;

namespace TeamTools.Core.Models;

/// <summary>
/// The Lean Coffee payload of a coffee <see cref="Room"/> (#35) — the proposed topics, the dots
/// spent on them, and the log of what actually got discussed.
/// <para>
/// One-to-one with its room and keyed by <see cref="RoomId"/> (a shared primary key), exactly like a
/// poker round and a retro board, so a room and its payload are always loaded and deleted together.
/// </para>
/// </summary>
public class CoffeeBoard
{
    /// <summary>Primary key, shared with the owning <see cref="Room"/>.</summary>
    public Guid RoomId { get; set; }

    public Room? Room { get; set; }

    /// <summary>Where the facilitator has moved the room to.</summary>
    public CoffeePhase Phase { get; set; } = CoffeePhase.Propose;

    /// <summary>
    /// Configured phase-countdown length in seconds, or null for none. During Discuss this is the
    /// <b>per-topic</b> timebox and restarts for each topic — which is the whole shape of a Lean
    /// Coffee.
    /// </summary>
    public int? PhaseDurationSeconds { get; set; }

    /// <summary>
    /// UTC instant the running countdown expires; null when nothing is running. The same
    /// deadline-broadcast primitive both other tools use (<see cref="Countdown"/>, #34), so clients
    /// tick locally against one server-authoritative instant.
    /// </summary>
    public DateTimeOffset? PhaseDeadline { get; set; }

    /// <summary>How many dots each participant gets. See <see cref="DotBudget"/>.</summary>
    public int VoteBudget { get; set; } = DefaultVoteBudget;

    /// <summary>
    /// Whether a voter may stack dots on one topic. Off by default: spreading them surfaces more of
    /// what the room wants to talk about, which is the point.
    /// </summary>
    public bool AllowMultiplePerItem { get; set; }

    /// <summary>
    /// The topic under discussion, or null before Discuss starts and after the last one is done.
    /// </summary>
    public Guid? CurrentTopicId { get; set; }

    /// <summary>
    /// Whether an extension vote is open on the current topic. While it is, individual answers are
    /// hidden and only the count of answers is broadcast — see <c>CoffeeService.ToSnapshot</c>.
    /// </summary>
    public bool ExtendVoteOpen { get; set; }

    public List<CoffeeTopic> Topics { get; set; } = new();

    /// <summary>Every dot spent on this board. One row per dot.</summary>
    public List<CoffeeVote> Votes { get; set; } = new();

    /// <summary>Answers to the current (or last) extension vote.</summary>
    public List<CoffeeExtendVote> ExtendVotes { get; set; } = new();

    /// <summary>What the room decided, against the topic that produced it.</summary>
    public List<CoffeeDecision> Decisions { get; set; } = new();

    /// <summary>Three dots is the usual facilitation default — enough to rank, few enough to choose.</summary>
    public const int DefaultVoteBudget = 3;
}

/// <summary>
/// Something a participant wants to talk about (#35).
/// <para>
/// <b>Never anonymous, unlike a retro card.</b> A topic is something you are volunteering to lead a
/// conversation about, so the author's name is the useful part rather than a leak — which is why
/// this board has no anonymity flag.
/// </para>
/// </summary>
public class CoffeeTopic
{
    public Guid Id { get; set; }

    public Guid BoardId { get; set; }

    public CoffeeBoard? Board { get; set; }

    /// <summary>Who proposed it.</summary>
    public string AuthorUserId { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    /// <summary>Position as proposed; the tie-breaker when two topics have equal dots.</summary>
    public int Order { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the room started on it, or null if it never came up.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When the room finished with it, or null while it is current or unvisited.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>
    /// How long the room actually spent on it, accumulated across extensions. The interesting
    /// number in the minutes: what the team *said* it wanted to discuss versus what it did.
    /// </summary>
    public int DiscussedSeconds { get; set; }

    /// <summary>How many times the room voted to keep going.</summary>
    public int Extensions { get; set; }

    /// <summary>Whether the room has finished with this topic.</summary>
    public bool IsDiscussed => FinishedAt is not null;
}

/// <summary>
/// One dot, spent by one participant on one topic (#35). A row per dot, so the budget is a row
/// count — see <see cref="DotBudget"/>, shared with the retro.
/// </summary>
public class CoffeeVote : IDotVote
{
    public Guid Id { get; set; }

    public Guid BoardId { get; set; }

    public CoffeeBoard? Board { get; set; }

    /// <summary>Who spent it. Never broadcast to other participants — only tallies are.</summary>
    public string VoterUserId { get; set; } = string.Empty;

    /// <summary>The topic it was spent on.</summary>
    public Guid TargetId { get; set; }
}

/// <summary>One participant's keep-going/move-on answer for one topic (#35).</summary>
public class CoffeeExtendVote
{
    public Guid Id { get; set; }

    public Guid BoardId { get; set; }

    public CoffeeBoard? Board { get; set; }

    /// <summary>The topic the vote is about — kept so a resolved round can be read back.</summary>
    public Guid TopicId { get; set; }

    public string VoterUserId { get; set; } = string.Empty;

    public ExtendChoice Choice { get; set; }
}

/// <summary>
/// Something the room decided while discussing a topic (#35) — the reason to hold a Lean Coffee at
/// all rather than just talk. Same shape as a retro action item, and the same rules
/// (<see cref="ActionItemRules"/>): an optional owner who need not be in the room, an optional due
/// date, and a done state that gets ticked days later.
/// </summary>
public class CoffeeDecision
{
    public Guid Id { get; set; }

    public Guid BoardId { get; set; }

    public CoffeeBoard? Board { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>The topic it came out of, when it came out of one.</summary>
    public Guid? TopicId { get; set; }

    public string? OwnerUserId { get; set; }

    public string? OwnerName { get; set; }

    public DateTimeOffset? DueDate { get; set; }

    public DateTimeOffset? DoneAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public bool IsDone => DoneAt is not null;
}
