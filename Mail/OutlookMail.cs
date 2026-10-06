namespace ShowroomBot.Mail;

public sealed record OutlookMail(string StoreId, string EntryId, string Sender, string Subject, string Body);

public interface IOutlookInboxReader
{
    Task<IReadOnlyList<OutlookMail>> ReadUnreadInboxAsync(CancellationToken cancellationToken);
}
