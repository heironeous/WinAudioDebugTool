namespace AudioDeviceDebugger.Models;

public sealed class TestOutcome
{
    public required bool Success { get; init; }
    public required string Message { get; init; }

    public static TestOutcome Ok(string message) => new() { Success = true, Message = message };
    public static TestOutcome Fail(string message) => new() { Success = false, Message = message };
}
