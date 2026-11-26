using System.Text.Json;
using AudioDeviceDebugger.Models;
using AudioDeviceDebugger.Services;
using static AudioDeviceDebugger.Tests.EventFactory;

namespace AudioDeviceDebugger.Tests;

public class EventLogExporterTests
{
    private static string[] Lines(string text) =>
        text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    [Theory]
    [InlineData("log.csv", EventLogFormat.Csv)]
    [InlineData("LOG.MD", EventLogFormat.Markdown)]
    [InlineData("log.markdown", EventLogFormat.Markdown)]
    [InlineData("log.jsonl", EventLogFormat.JsonLines)]
    [InlineData("log.json", EventLogFormat.JsonLines)]
    [InlineData("log.txt", EventLogFormat.PlainText)]
    [InlineData("log", EventLogFormat.PlainText)]
    public void The_file_extension_picks_the_format(string path, EventLogFormat expected) =>
        Assert.Equal(expected, EventLogExporter.FormatFromExtension(path));

    [Fact]
    public void Csv_quotes_fields_with_commas_quotes_and_line_breaks()
    {
        var e = Make(detail: "say \"hi\", then\nleave", deviceName: "Speakers, front") with
        {
            LastTimestamp = T0,
        };

        var lines = Lines(EventLogExporter.ToText(new[] { e }, EventLogFormat.Csv));

        Assert.Equal("Time,LastTime,Event,Kind,Source,Device,Direction,DeviceId,Detail,Count", lines[0]);
        Assert.Equal("2026-11-25T10:00:00.0000000+01:00,2026-11-25T10:00:00.0000000+01:00,Volume,VolumeChanged,Windows,"
            + "\"Speakers, front\",Output,dev-1,\"say \"\"hi\"\", then\nleave\",1", lines[1]);
    }

    [Fact]
    public void Csv_leaves_missing_values_empty()
    {
        var e = Make(AudioEventKind.Started, detail: "Started", deviceId: null, deviceName: null) with { LastTimestamp = T0 };

        var row = Lines(EventLogExporter.ToText(new[] { e }, EventLogFormat.Csv))[1];

        Assert.EndsWith(",Started,Started,Windows,,,,Started,1", row);
    }

    [Fact]
    public void Markdown_escapes_pipes_and_line_breaks_and_shows_merge_counts()
    {
        var e = Make(detail: "a|b\nc", source: AudioEventSource.ThisApp) with { Count = 4 };

        var lines = Lines(EventLogExporter.ToText(new[] { e }, EventLogFormat.Markdown));

        Assert.Equal("| Time | Event | Device | Detail | By |", lines[0]);
        Assert.Equal("|---|---|---|---|---|", lines[1]);
        Assert.Equal("| 2026-11-25 10:00:00.000 | Volume | Speakers | a\\|b c (4×) | this app |", lines[2]);
    }

    [Fact]
    public void Plain_text_marks_this_apps_own_actions()
    {
        var e = Make(AudioEventKind.DefaultRequested, detail: "Default", source: AudioEventSource.ThisApp);

        Assert.Equal("10:00:00.000  Set default Speakers — Default [this app]",
            Lines(EventLogExporter.ToText(new[] { e }, EventLogFormat.PlainText))[0]);
    }

    [Fact]
    public void Json_line_omits_nulls_and_the_last_time_of_a_single_event()
    {
        var e = Make(AudioEventKind.Started, detail: "Started", deviceId: null, deviceName: null) with { LastTimestamp = T0 };

        using var json = JsonDocument.Parse(EventLogExporter.ToJsonLine(e));
        var names = json.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Equal(new[] { "time", "kind", "source", "detail", "count" }, names);
        Assert.Equal("Started", json.RootElement.GetProperty("kind").GetString());
        Assert.Equal("Windows", json.RootElement.GetProperty("source").GetString());
    }

    [Fact]
    public void Json_line_of_a_merged_event_has_its_last_time()
    {
        var e = Make() with { Count = 2, LastTimestamp = T0.AddSeconds(3) };

        using var json = JsonDocument.Parse(EventLogExporter.ToJsonLine(e));

        Assert.Equal(T0.AddSeconds(3), json.RootElement.GetProperty("lastTime").GetDateTimeOffset());
        Assert.Equal("Output", json.RootElement.GetProperty("direction").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public void Json_lines_writes_one_object_per_line()
    {
        var text = EventLogExporter.ToText(new[] { Make(seconds: 0), Make(seconds: 1) }, EventLogFormat.JsonLines);

        Assert.All(Lines(text), line => JsonDocument.Parse(line).Dispose());
        Assert.Equal(2, Lines(text).Length);
    }
}
