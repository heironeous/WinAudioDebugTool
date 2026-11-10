using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AudioDeviceDebugger.Models;

namespace AudioDeviceDebugger.Services;

public enum EventLogFormat
{
    Csv,
    Markdown,
    JsonLines,
    PlainText,
}

/// <summary>Writes event-timeline entries (oldest first) as CSV, a Markdown table, JSON Lines, or aligned plain text.</summary>
public static class EventLogExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static EventLogFormat FormatFromExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".csv" => EventLogFormat.Csv,
        ".md" or ".markdown" => EventLogFormat.Markdown,
        ".jsonl" or ".json" => EventLogFormat.JsonLines,
        _ => EventLogFormat.PlainText,
    };

    public static string ToText(IEnumerable<AudioEvent> events, EventLogFormat format)
    {
        var writer = new StringWriter();
        Write(events, format, writer);
        return writer.ToString();
    }

    public static void Write(IEnumerable<AudioEvent> events, EventLogFormat format, TextWriter writer)
    {
        switch (format)
        {
            case EventLogFormat.Csv:
                writer.WriteLine("Time,LastTime,Event,Kind,Source,Device,Direction,DeviceId,Detail,Count");
                foreach (var e in events)
                {
                    writer.WriteLine(string.Join(",",
                        Csv(e.Timestamp.ToString("o")), Csv(e.LastTimestamp.ToString("o")), Csv(e.KindText), Csv(e.Kind.ToString()),
                        Csv(e.SourceText), Csv(e.DeviceName), Csv(e.Direction?.ToString()), Csv(e.DeviceId), Csv(e.Detail),
                        e.Count.ToString()));
                }
                break;

            case EventLogFormat.Markdown:
                writer.WriteLine("| Time | Event | Device | Detail | By |");
                writer.WriteLine("|---|---|---|---|---|");
                foreach (var e in events)
                {
                    string detail = e.Count > 1 ? $"{e.Detail} ({e.Count}×)" : e.Detail;
                    writer.WriteLine($"| {e.Timestamp:yyyy-MM-dd HH:mm:ss.fff} | {Md(e.KindText)} | {Md(e.DeviceName)} | {Md(detail)} | {e.SourceText} |");
                }
                break;

            case EventLogFormat.JsonLines:
                foreach (var e in events)
                {
                    writer.WriteLine(JsonSerializer.Serialize(new
                    {
                        time = e.Timestamp,
                        lastTime = e.Count > 1 ? e.LastTimestamp : (DateTimeOffset?)null,
                        kind = e.Kind,
                        source = e.Source,
                        device = e.DeviceName,
                        direction = e.Direction,
                        deviceId = e.DeviceId,
                        detail = e.Detail,
                        count = e.Count,
                    }, JsonOptions));
                }
                break;

            default:
                foreach (var e in events)
                {
                    var line = new StringBuilder();
                    line.Append(e.Timestamp.ToString("HH:mm:ss.fff")).Append("  ").Append(e.KindText.PadRight(12));
                    if (e.DeviceName is not null) line.Append(e.DeviceName).Append(" — ");
                    line.Append(e.Detail);
                    if (e.Count > 1) line.Append($" ({e.Count}×)");
                    if (e.Source == AudioEventSource.ThisApp) line.Append(" [this app]");
                    writer.WriteLine(line.ToString());
                }
                break;
        }
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static string Md(string? value) =>
        (value ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
