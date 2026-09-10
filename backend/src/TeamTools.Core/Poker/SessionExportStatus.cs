namespace TeamTools.Core.Poker;

/// <summary>
/// Why a poker session's round history was refused, or Ok. See #30.
/// <para>
/// The round history is the whole session in one payload — every story, every note, every estimate —
/// so it is guarded by the room's join password exactly as the retro export is (#28). Before #30 it
/// was guarded by session existence alone, which made a short code enough to read a protected
/// session's entire history without ever joining it.
/// </para>
/// </summary>
public enum SessionExportStatus
{
    Ok,
    SessionNotFound,
    /// <summary>The session has a password and the wrong one (or none) was supplied.</summary>
    PasswordRequired,
}
