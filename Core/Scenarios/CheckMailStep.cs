using ShowroomBot.Mail;

namespace ShowroomBot.Core.Scenarios;

public sealed class CheckMailStep(IOutlookInboxReader outlook, MailDeliveryHistory history,
    Func<OutlookMail, CancellationToken, Task> sendMail) : IScenarioStep
{
    public string Name => "Проверить локальный Outlook";

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deliveryHistory = history.Open();
        var messages = await outlook.ReadUnreadInboxAsync(cancellationToken);
        var sent = 0;
        foreach (var mail in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deliveryHistory.Contains(mail)) continue;
            await sendMail(mail, cancellationToken);
            deliveryHistory.RecordSent(mail);
            sent++;
        }
        // Mail contents, subjects, senders and Outlook IDs are not diagnostic data.
        ScenarioExecution.Log($"CheckMail: непрочитанных писем {messages.Count}, отправлено новых {sent}.");
    }
}
