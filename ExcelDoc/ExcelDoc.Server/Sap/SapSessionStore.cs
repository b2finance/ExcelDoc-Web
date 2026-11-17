using System.Collections.Concurrent;

namespace ExcelDoc.Server.Sap;

public sealed class SapSessionStore : ISapSessionStore
{
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions =
        new(StringComparer.Ordinal);

    public void Add(SapSessionContext session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _sessions[session.SessionKey] = new SessionEntry(session);
    }

    public bool TryGet(string sessionKey, out SapSessionContext? session)
    {
        session = null;
        if (!_sessions.TryGetValue(sessionKey, out var entry))
        {
            return false;
        }

        lock (entry.SyncRoot)
        {
            if (entry.LogoutRequested || entry.Session.IsInvalidated)
            {
                return false;
            }

            if (entry.Session.ExpiresAtUtc > DateTime.UtcNow)
            {
                session = entry.Session;
                return true;
            }

            if (entry.ActiveJobs == 0)
            {
                _sessions.TryRemove(sessionKey, out _);
            }

            return false;
        }
    }

    public bool TryGetForJob(string sessionKey, out SapSessionContext? session)
    {
        session = null;
        if (!_sessions.TryGetValue(sessionKey, out var entry))
        {
            return false;
        }

        lock (entry.SyncRoot)
        {
            if (entry.ActiveJobs <= 0 || entry.Session.IsInvalidated)
            {
                return false;
            }

            session = entry.Session;
            return true;
        }
    }

    public bool Remove(string sessionKey, out SapSessionContext? session)
    {
        if (_sessions.TryRemove(sessionKey, out var entry))
        {
            session = entry.Session;
            return true;
        }

        session = null;
        return false;
    }

    public bool TryAcquireJob(string sessionKey)
    {
        if (!_sessions.TryGetValue(sessionKey, out var entry))
        {
            return false;
        }

        lock (entry.SyncRoot)
        {
            if (entry.LogoutRequested ||
                entry.Session.ExpiresAtUtc <= DateTime.UtcNow)
            {
                if (entry.ActiveJobs == 0)
                {
                    _sessions.TryRemove(sessionKey, out _);
                }

                return false;
            }

            entry.ActiveJobs++;
            return true;
        }
    }

    public SapSessionContext? ReleaseJob(string sessionKey)
    {
        if (!_sessions.TryGetValue(sessionKey, out var entry))
        {
            return null;
        }

        lock (entry.SyncRoot)
        {
            if (entry.ActiveJobs > 0)
            {
                entry.ActiveJobs--;
            }

            if (entry.ActiveJobs > 0 ||
                (!entry.LogoutRequested &&
                 entry.Session.ExpiresAtUtc > DateTime.UtcNow))
            {
                return null;
            }

            return _sessions.TryRemove(sessionKey, out var removed)
                ? removed.Session
                : null;
        }
    }

    public SapSessionContext? Invalidate(string sessionKey)
    {
        if (!_sessions.TryGetValue(sessionKey, out var entry))
        {
            return null;
        }

        lock (entry.SyncRoot)
        {
            entry.Session.Invalidate();
            entry.LogoutRequested = true;
            if (entry.ActiveJobs > 0)
            {
                return null;
            }

            return _sessions.TryRemove(sessionKey, out var removed) ? removed.Session : null;
        }
    }

    public SapSessionContext? RequestLogout(string sessionKey)
    {
        if (!_sessions.TryGetValue(sessionKey, out var entry))
        {
            return null;
        }

        lock (entry.SyncRoot)
        {
            if (entry.ActiveJobs > 0)
            {
                entry.LogoutRequested = true;
                return null;
            }

            return _sessions.TryRemove(sessionKey, out var removed)
                ? removed.Session
                : null;
        }
    }

    public IReadOnlyCollection<SapSessionContext> GetActiveJobSessions()
    {
        var sessions = new List<SapSessionContext>();

        foreach (var entry in _sessions.Values)
        {
            lock (entry.SyncRoot)
            {
                if (entry.ActiveJobs > 0)
                {
                    sessions.Add(entry.Session);
                }
            }
        }

        return sessions;
    }

    public IReadOnlyCollection<SapSessionContext> RemoveExpired()
    {
        var removedSessions = new List<SapSessionContext>();
        var now = DateTime.UtcNow;

        foreach (var (sessionKey, entry) in _sessions)
        {
            lock (entry.SyncRoot)
            {
                if (entry.ActiveJobs > 0 ||
                    entry.Session.ExpiresAtUtc > now)
                {
                    continue;
                }

                if (_sessions.TryRemove(sessionKey, out var removed))
                {
                    removedSessions.Add(removed.Session);
                }
            }
        }

        return removedSessions;
    }

    private sealed class SessionEntry(SapSessionContext session)
    {
        public object SyncRoot { get; } = new();

        public SapSessionContext Session { get; } = session;

        public int ActiveJobs { get; set; }

        public bool LogoutRequested { get; set; }
    }
}
