using System.IO;
using System.Text.Json;
using AudioDeviceDebugger.Models;
using AudioDeviceDebugger.Services;
using static AudioDeviceDebugger.Tests.EventFactory;

namespace AudioDeviceDebugger.Tests;

public sealed class EventLogFileTests : IDisposable
{
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AudioDeviceDebugger.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static string DayFile(DateTime day) => $"events-{day:yyyy-MM-dd}.log";

    [Fact]
    public void File_name_uses_the_local_date()
    {
        var local = new DateTimeOffset(new DateTime(2026, 11, 25, 23, 30, 0, DateTimeKind.Local));

        Assert.Equal("events-2026-11-25.log", EventLogFile.FileName(local));
    }

    [Fact]
    public void Entries_are_appended_to_the_file_of_their_day()
    {
        var today = DateTime.Today;
        var log = new EventLogFile(directory: _directory);

        log.Append(new[]
        {
            Make(detail: "yesterday") with { Timestamp = new DateTimeOffset(today.AddDays(-1).AddHours(23)) },
            Make(detail: "today 1") with { Timestamp = new DateTimeOffset(today.AddHours(1)) },
        });
        log.Append(new[] { Make(detail: "today 2") with { Timestamp = new DateTimeOffset(today.AddHours(2)) } });
        log.Flush(FlushTimeout);

        string Detail(string line) => JsonDocument.Parse(line).RootElement.GetProperty("detail").GetString()!;
        Assert.Equal(new[] { "yesterday" },
            File.ReadAllLines(Path.Combine(_directory, DayFile(today.AddDays(-1)))).Select(Detail));
        Assert.Equal(new[] { "today 1", "today 2" },
            File.ReadAllLines(Path.Combine(_directory, DayFile(today))).Select(Detail));
    }

    [Fact]
    public void Files_older_than_the_kept_days_are_deleted_at_startup()
    {
        var today = DateTime.Today;
        Directory.CreateDirectory(_directory);
        string oldest = DayFile(today.AddDays(-(EventLogFile.KeepDays - 1)));
        string tooOld = DayFile(today.AddDays(-EventLogFile.KeepDays));
        foreach (var name in new[] { DayFile(today), oldest, tooOld, "events-notes.log", "other.txt" })
        {
            File.WriteAllText(Path.Combine(_directory, name), "");
        }

        new EventLogFile(directory: _directory).Flush(FlushTimeout);

        var left = Directory.GetFiles(_directory).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(new[] { DayFile(today), oldest, "events-notes.log", "other.txt" }.Order(), left);
    }

    [Fact]
    public void A_write_error_is_reported_once()
    {
        // A file where the folder should be: every write fails.
        Directory.CreateDirectory(Path.GetDirectoryName(_directory)!);
        File.WriteAllText(_directory, "");
        var errors = new List<string>();
        var log = new EventLogFile(errors.Add, _directory);

        log.Append(new[] { Make() });
        log.Append(new[] { Make() });
        log.Flush(FlushTimeout);
        File.Delete(_directory);

        Assert.Single(errors);
        Assert.StartsWith("Couldn't write the event log to ", errors[0]);
    }

    [Fact]
    public void Appending_nothing_creates_no_folder()
    {
        var log = new EventLogFile(directory: _directory);
        log.Append(Array.Empty<AudioEvent>());
        log.Flush(FlushTimeout);

        Assert.False(Directory.Exists(_directory));
    }
}
