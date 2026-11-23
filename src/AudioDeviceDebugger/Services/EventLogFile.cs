using System.Globalization;
using System.IO;
using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.Services;

/// <summary>
/// Keeps the event timeline on disk: one JSON Lines file per day (<c>events-YYYY-MM-DD.log</c>, local date of each
/// entry) in <c>%LOCALAPPDATA%\AudioDeviceDebugger</c>, in the same format as Export… → JSON Lines. Files older than
/// <see cref="KeepDays"/> days are deleted at startup.
/// </summary>
/// <remarks>
/// Writes run on the thread pool, one batch after another, so a slow disk (or a virus scanner) never stalls the UI.
/// A failure is reported once through the callback and otherwise ignored: the log must never break the app.
/// </remarks>
public sealed class EventLogFile
{
    public const int KeepDays = 14;
    private const string FilePrefix = "events-";
    private const string FileExtension = ".log";
    private const string DateFormat = "yyyy-MM-dd";

    private readonly object _lock = new();
    private readonly Action<string>? _onError;
    private Task _tail = Task.CompletedTask;
    private int _errorReported;

    public EventLogFile(Action<string>? onError = null, string? directory = null)
    {
        _onError = onError;
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioDeviceDebugger");
        Enqueue(() => DeleteOldFiles(DateTime.Today));
    }

    /// <summary>The folder holding the daily files.</summary>
    public string Directory { get; }

    public static string FileName(DateTimeOffset timestamp) =>
        FilePrefix + timestamp.LocalDateTime.ToString(DateFormat, CultureInfo.InvariantCulture) + FileExtension;

    /// <summary>Appends entries (oldest first) to the file of each entry's day.</summary>
    public void Append(IReadOnlyList<AudioEvent> events)
    {
        if (events.Count == 0) return;
        Enqueue(() =>
        {
            System.IO.Directory.CreateDirectory(Directory);
            foreach (var day in events.GroupBy(e => FileName(e.Timestamp)))
            {
                File.AppendAllLines(Path.Combine(Directory, day.Key), day.Select(EventLogExporter.ToJsonLine));
            }
        });
    }

    /// <summary>Waits (up to <paramref name="timeout"/>) for queued writes to finish; called when the app closes.</summary>
    public void Flush(TimeSpan timeout)
    {
        Task tail;
        lock (_lock) tail = _tail;
        try
        {
            tail.Wait(timeout);
        }
        catch
        {
            // Write errors are reported (or swallowed) inside the task.
        }
    }

    private void DeleteOldFiles(DateTime today)
    {
        if (!System.IO.Directory.Exists(Directory)) return;
        var oldest = today.AddDays(-(KeepDays - 1));
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, FilePrefix + "*" + FileExtension))
        {
            string date = Path.GetFileNameWithoutExtension(path)[FilePrefix.Length..];
            if (DateTime.TryParseExact(date, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                && day < oldest)
            {
                File.Delete(path);
            }
        }
    }

    private void Enqueue(Action write)
    {
        lock (_lock)
        {
            _tail = _tail.ContinueWith(_ =>
            {
                try
                {
                    write();
                }
                catch (Exception ex)
                {
                    if (Interlocked.Exchange(ref _errorReported, 1) == 0)
                    {
                        _onError?.Invoke($"Couldn't write the event log to {Directory}: {ex.Message}");
                    }
                }
            }, TaskScheduler.Default);
        }
    }
}
