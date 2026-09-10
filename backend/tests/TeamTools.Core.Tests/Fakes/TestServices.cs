using TeamTools.Core.Coffee;
using TeamTools.Core.Poker;
using TeamTools.Core.Retro;
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

    /// <summary>The Lean Coffee API (#35): the room engine plus the coffee service over it.</summary>
    public static CoffeeService Coffee(
        FakeRoomStore store,
        IShortCodeGenerator shortCodes,
        IClock clock,
        IPasswordHasher? passwordHasher = null) =>
        new(store, Rooms(store, shortCodes, clock, passwordHasher), clock);

    /// <summary>The coffee timebox sweep, wired to the same store and clock.</summary>
    public static CoffeeTimerService CoffeeTimers(
        FakeRoomStore store, IShortCodeGenerator shortCodes, IClock clock) =>
        new(store, store, Coffee(store, shortCodes, clock), clock);
}
