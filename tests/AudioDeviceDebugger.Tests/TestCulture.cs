using System.Globalization;
using System.Runtime.CompilerServices;

namespace AudioDeviceDebugger.Tests;

internal static class TestCulture
{
    /// <summary>The app formats numbers and times in the user's culture; the expected strings in these tests are
    /// written for the invariant culture, so every test thread uses it whatever the machine's settings.</summary>
    [ModuleInitializer]
    internal static void UseInvariantCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}
