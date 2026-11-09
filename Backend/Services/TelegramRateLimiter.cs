using System.Collections.Concurrent;

namespace TelegramBotApi.Services;

/// <summary>
/// In-memory rate limiter and anti-flood protection for Telegram updates.
/// Prevents denial of service, resource exhaustion, and Telegram API ban triggers.
/// </summary>
public class TelegramRateLimiter
{
    private class UserWindow
    {
        public int Count;
        public DateTime WindowStart;
    }

    // Rate limit per user: max 5 messages per 5-second window
    private const int MaxMessagesPerWindow = 5;
    private static readonly TimeSpan WindowDuration = TimeSpan.FromSeconds(5);

    // Throttle owner notifications for unauthorized users: max 1 notification per 15 minutes
    private static readonly TimeSpan OwnerNotificationCooldown = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<long, UserWindow> _userWindows = new();
    private readonly ConcurrentDictionary<long, DateTime> _ownerNotificationHistory = new();
    private DateTime _lastCleanup = DateTime.UtcNow;
    private readonly object _cleanupLock = new();

    /// <summary>
    /// Checks if a Telegram user is allowed to send a message (anti-flood).
    /// </summary>
    public bool CanProcessUserMessage(long userId)
    {
        PeriodicCleanup();

        var now = DateTime.UtcNow;
        var window = _userWindows.GetOrAdd(userId, _ => new UserWindow
        {
            Count = 0,
            WindowStart = now
        });

        lock (window)
        {
            if (now - window.WindowStart > WindowDuration)
            {
                window.Count = 1;
                window.WindowStart = now;
                return true;
            }

            if (window.Count >= MaxMessagesPerWindow)
            {
                return false; // Throttled
            }

            window.Count++;
            return true;
        }
    }

    /// <summary>
    /// Prevents notification floods to the bot owner when an unknown user or bot spams the bot.
    /// Returns true only if owner hasn't been notified about this user within the cooldown period.
    /// </summary>
    public bool ShouldNotifyOwnerAboutUnauthorized(long userId)
    {
        var now = DateTime.UtcNow;
        if (_ownerNotificationHistory.TryGetValue(userId, out var lastNotified))
        {
            if (now - lastNotified < OwnerNotificationCooldown)
            {
                return false;
            }
        }

        _ownerNotificationHistory[userId] = now;
        return true;
    }

    private void PeriodicCleanup()
    {
        var now = DateTime.UtcNow;
        if (now - _lastCleanup < TimeSpan.FromMinutes(5)) return;

        lock (_cleanupLock)
        {
            if (now - _lastCleanup < TimeSpan.FromMinutes(5)) return;
            _lastCleanup = now;

            // Remove stale windows
            foreach (var kvp in _userWindows)
            {
                if (now - kvp.Value.WindowStart > TimeSpan.FromMinutes(2))
                {
                    _userWindows.TryRemove(kvp.Key, out _);
                }
            }

            // Remove stale notification history
            foreach (var kvp in _ownerNotificationHistory)
            {
                if (now - kvp.Value > OwnerNotificationCooldown * 2)
                {
                    _ownerNotificationHistory.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}
