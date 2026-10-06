using System.Text.Json;

namespace ShowroomBot.Mail;

/// <summary>Delivery state only: never stores message bodies or changes Outlook's read state.</summary>
public sealed class MailDeliveryHistory(string? filePath = null, TimeProvider? timeProvider = null)
{
    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShowroomBot", "mail-delivery-history.json");
    private readonly string _filePath = Path.GetFullPath(filePath ?? DefaultFilePath);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public Session Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        // Hold across read -> send -> persist, including across ShowroomBot processes.
        var lease = new FileStream(_filePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { return new Session(_filePath, _clock, lease); }
        catch { lease.Dispose(); throw; }
    }

    public sealed record Entry(string StoreId, string EntryId, DateTimeOffset SentAtUtc);
    public sealed record Document(int Version, List<Entry> Entries);

    public sealed class Session : IDisposable
    {
        private readonly string _path;
        private readonly TimeProvider _clock;
        private readonly FileStream _lease;
        private readonly Dictionary<(string Store, string Item), Entry> _entries = [];

        internal Session(string path, TimeProvider clock, FileStream lease)
        {
            _path = path;
            _clock = clock;
            _lease = lease;
            if (File.Exists(path))
            {
                Document document;
                try
                {
                    document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path))
                        ?? throw new JsonException();
                }
                catch (JsonException exception)
                {
                    throw new InvalidDataException("CheckMail: история отправок повреждена. Восстановите файл истории перед повторным запуском.", exception);
                }
                if (document.Version != 1 || document.Entries is null)
                    throw new InvalidDataException("CheckMail: неизвестный формат истории отправок.");
                foreach (var entry in document.Entries)
                {
                    if (entry is null || string.IsNullOrWhiteSpace(entry.StoreId) ||
                        string.IsNullOrWhiteSpace(entry.EntryId) || entry.SentAtUtc == default)
                        throw new InvalidDataException("CheckMail: некорректная запись в истории отправок.");
                    if (entry.SentAtUtc >= clock.GetUtcNow().AddDays(-5))
                        _entries[Key(entry.StoreId, entry.EntryId)] = entry;
                }
            }
            // Prune on every CheckMail (even an empty inbox), and verify writes before delivery.
            Save();
        }

        public bool Contains(OutlookMail mail) => _entries.ContainsKey(Key(mail.StoreId, mail.EntryId));

        public void RecordSent(OutlookMail mail)
        {
            _entries[Key(mail.StoreId, mail.EntryId)] = new(mail.StoreId, mail.EntryId, _clock.GetUtcNow());
            // Deliberately not cancellable: a confirmed delivery must be recorded even on /stop.
            Save();
        }

        private static (string, string) Key(string store, string item)
        {
            if (string.IsNullOrWhiteSpace(store) || string.IsNullOrWhiteSpace(item))
                throw new InvalidDataException("CheckMail: у письма отсутствует StoreID или EntryID.");
            return (store.ToUpperInvariant(), item.ToUpperInvariant());
        }

        private void Save()
        {
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, new Document(1, _entries.Values.ToList()));
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, _path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        public void Dispose() => _lease.Dispose();
    }
}
