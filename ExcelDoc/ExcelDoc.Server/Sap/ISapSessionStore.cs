namespace ExcelDoc.Server.Sap;

public interface ISapSessionStore
{
    void Add(SapSessionContext session);

    bool TryGet(string sessionKey, out SapSessionContext? session);

    bool TryGetForJob(string sessionKey, out SapSessionContext? session);

    bool Remove(string sessionKey, out SapSessionContext? session);

    bool TryAcquireJob(string sessionKey);

    SapSessionContext? ReleaseJob(string sessionKey);

    SapSessionContext? RequestLogout(string sessionKey);

    SapSessionContext? Invalidate(string sessionKey);

    IReadOnlyCollection<SapSessionContext> GetActiveJobSessions();

    IReadOnlyCollection<SapSessionContext> RemoveExpired();
}
