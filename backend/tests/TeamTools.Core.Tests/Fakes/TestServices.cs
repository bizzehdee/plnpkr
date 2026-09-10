using TeamTools.Core.Poker;
using TeamTools.Core.Security;

namespace TeamTools.Core.Tests.Fakes;

/// <summary>
/// Builds the service pair the tests drive. Since #19 split the old single service into the room
/// engine plus a tool service, a test that wants "the poker API" needs both wired together; this
/// keeps that assembly in one place instead of in every constructor.
/// </summary>
public static class TestServices
{
    public static PokerService Poker(
        FakeRoomStore store,
        IShortCodeGenerator shortCodes,
        IClock clock,
        IPasswordHasher? passwordHasher = null,
        SessionLimits? limits = null) =>
        new(store, Rooms(store, shortCodes, clock, passwordHasher, limits), clock);

    public static RoomService Rooms(
        FakeRoomStore store,
        IShortCodeGenerator shortCodes,
        IClock clock,
        IPasswordHasher? passwordHasher = null,
        SessionLimits? limits = null) =>
        new(store, shortCodes, clock, passwordHasher, limits);

    public static PokerTimerService Timers(FakeRoomStore store, IClock clock) =>
        new(store, store, clock);

    public static RoomMaintenanceService Maintenance(FakeRoomStore store, IClock clock) =>
        new(store, clock);
}
