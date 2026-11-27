using System.Runtime.InteropServices;
using AudioDeviceDebugger.Services;

namespace AudioDeviceDebugger.Tests;

public class AppElevationTests
{
    [Theory]
    [InlineData(new string[0], "")]
    [InlineData(new[] { "--theme=light" }, "--theme=light")]
    [InlineData(new[] { "a", "b" }, "a b")]
    [InlineData(new[] { "with space" }, "\"with space\"")]
    [InlineData(new[] { "" }, "\"\"")]
    [InlineData(new[] { "say \"hi\"" }, "\"say \\\"hi\\\"\"")]
    [InlineData(new[] { @"C:\some dir\" }, "\"C:\\some dir\\\\\"")]
    [InlineData(new[] { @"C:\no\space\" }, @"C:\no\space\")]
    public void JoinArguments_QuotesOnlyWhereNeeded(string[] args, string expected) =>
        Assert.Equal(expected, AppElevation.JoinArguments(args));

    // What the restarted exe gets back must be exactly what this one was given.
    [Theory]
    [InlineData("--theme=dark")]
    [InlineData("two words", "")]
    [InlineData(@"C:\some dir\", "x")]
    [InlineData("quote\"inside", @"back\\slashes\""quote")]
    [InlineData("\t", @"\\server\share\")]
    public void JoinArguments_RoundTripsThroughWindowsParsing(params string[] args)
    {
        string commandLine = "app.exe " + AppElevation.JoinArguments(args);
        Assert.Equal(args, SplitLikeWindows(commandLine).Skip(1));
    }

    private static string[] SplitLikeWindows(string commandLine)
    {
        nint argv = CommandLineToArgvW(commandLine, out int count);
        Assert.NotEqual(0, argv);
        try
        {
            return Enumerable.Range(0, count)
                .Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * nint.Size))!)
                .ToArray();
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint hMem);
}
