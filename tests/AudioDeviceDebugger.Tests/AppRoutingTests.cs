using AudioDeviceDebugger.Interop;
using AudioDeviceDebugger.Models;
using AudioDeviceDebugger.Services;
using AudioDeviceDebugger.ViewModels;

namespace AudioDeviceDebugger.Tests;

public class AppRoutingTests
{
    private const string Speakers = "{0.0.0.00000000}.{aaaaaaaa-0000-0000-0000-000000000001}";
    private const string Headphones = "{0.0.0.00000000}.{aaaaaaaa-0000-0000-0000-000000000002}";
    private const string Dock = "{0.0.0.00000000}.{aaaaaaaa-0000-0000-0000-000000000003}";
    private const string Mic = "{0.0.1.00000000}.{aaaaaaaa-0000-0000-0000-000000000004}";

    private static DeviceInfo Device(string id, string name, AudioDirection direction = AudioDirection.Output,
        DeviceConnectionState state = DeviceConnectionState.Active, bool isDefault = false) => new()
    {
        Id = id,
        FriendlyName = name,
        Direction = direction,
        State = state,
        IsDefaultDevice = isDefault,
    };

    private static readonly DeviceInfo[] Devices =
    [
        Device(Speakers, "Speakers (Realtek)", isDefault: true),
        Device(Headphones, "Headphones (Jabra)"),
        Device(Dock, "Headphones (HP dock)", state: DeviceConnectionState.Unplugged),
        Device(Mic, "Microphone (Jabra)", AudioDirection.Input, isDefault: true),
    ];

    [Fact]
    public void Choices_are_the_default_then_the_active_devices_of_the_direction_by_name()
    {
        var choices = AppRouting.BuildChoices(Devices, AudioDirection.Output, currentEndpointId: null);

        Assert.Equal(["Windows default (Speakers (Realtek))", "Headphones (Jabra)", "Speakers (Realtek)"],
            choices.Select(c => c.Name));
        Assert.Equal([null, Headphones, Speakers], choices.Select(c => c.EndpointId));
        Assert.Equal([true, false, false], choices.Select(c => c.IsCurrent));
        Assert.All(choices, c => Assert.True(c.IsAvailable));
    }

    [Fact]
    public void The_current_device_is_ticked_whatever_its_capitalisation()
    {
        var choices = AppRouting.BuildChoices(Devices, AudioDirection.Output, Headphones.ToUpperInvariant());

        Assert.Equal([false, true, false], choices.Select(c => c.IsCurrent));
    }

    [Fact]
    public void An_app_set_to_an_inactive_device_shows_it_unavailable()
    {
        var choices = AppRouting.BuildChoices(Devices, AudioDirection.Output, Dock);

        var last = choices[^1];
        Assert.Equal("Headphones (HP dock) (unplugged)", last.Name);
        Assert.True(last.IsCurrent);
        Assert.False(last.IsAvailable);
        Assert.Single(choices, c => c.IsCurrent);
    }

    [Fact]
    public void An_app_set_to_a_forgotten_device_shows_unknown_device()
    {
        var choices = AppRouting.BuildChoices(Devices, AudioDirection.Output, "{0.0.0.00000000}.{gone}");

        Assert.Equal("Unknown device", choices[^1].Name);
        Assert.False(choices[^1].IsAvailable);
    }

    [Fact]
    public void Inputs_offer_only_inputs()
    {
        var choices = AppRouting.BuildChoices(Devices, AudioDirection.Input, null);

        Assert.Equal(["Windows default (Microphone (Jabra))", "Microphone (Jabra)"], choices.Select(c => c.Name));
    }

    [Theory]
    [InlineData(AudioDirection.Output, @"\\?\SWD#MMDEVAPI#{0.0.0.00000000}.{abc}#{e6327cad-dcec-4949-ae8a-991e976a79d2}")]
    [InlineData(AudioDirection.Input, @"\\?\SWD#MMDEVAPI#{0.0.0.00000000}.{abc}#{2eef81be-33fa-4800-9670-1cd474972c3f}")]
    public void Endpoint_ids_round_trip_through_the_device_interface_path(AudioDirection direction, string path)
    {
        Assert.Equal(path, AudioPolicyConfig.ToInterfacePath("{0.0.0.00000000}.{abc}", direction));
        Assert.Equal("{0.0.0.00000000}.{abc}", AudioPolicyConfig.FromInterfacePath(path));
        // Prefix and suffix are recognised in any capitalisation.
        Assert.Equal("{0.0.0.00000000}.{ABC}", AudioPolicyConfig.FromInterfacePath(path.ToUpperInvariant()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_per_app_device_reads_as_null(string? path) => Assert.Null(AudioPolicyConfig.FromInterfacePath(path));

    private static AudioSessionInfo Session(bool thisApp = false, bool systemSounds = false, bool exited = false) => new()
    {
        Key = "k",
        DeviceId = Speakers,
        ProcessId = 42,
        AppName = "Spotify",
        State = AudioSessionActivity.Active,
        IsThisApp = thisApp,
        IsSystemSounds = systemSounds,
        ProcessExited = exited,
    };

    [Fact]
    public void Route_text_is_default_the_device_name_or_a_dash()
    {
        Assert.Equal("Default", SessionRowViewModel.RouteTextFor(Session(), true, null, null));
        Assert.Equal("Headphones (Jabra)", SessionRowViewModel.RouteTextFor(Session(), true, Headphones, "Headphones (Jabra)"));
        Assert.Equal("Unknown device", SessionRowViewModel.RouteTextFor(Session(), true, Headphones, null));
        Assert.Equal("—", SessionRowViewModel.RouteTextFor(Session(), false, null, null));
        Assert.Equal("—", SessionRowViewModel.RouteTextFor(Session(systemSounds: true), true, null, null));
        Assert.Equal("—", SessionRowViewModel.RouteTextFor(Session(thisApp: true), true, null, null));
        Assert.Equal("—", SessionRowViewModel.RouteTextFor(Session(exited: true), true, null, null));
    }

    [Fact]
    public void Route_tooltip_says_what_the_app_follows_and_why_it_cant_be_set()
    {
        Assert.StartsWith("Follows the Windows default output.",
            SessionRowViewModel.RouteToolTipFor(Session(), true, null, null, "output", null));
        Assert.StartsWith("Set to \"Headphones (Jabra)\" instead of the Windows default output.",
            SessionRowViewModel.RouteToolTipFor(Session(), true, Headphones, "Headphones (Jabra)", "output", null));
        Assert.Equal("Can't read this app's input setting: Access is denied.",
            SessionRowViewModel.RouteToolTipFor(Session(), false, null, null, "input", "Access is denied"));
        Assert.Equal("System sounds always use the Windows default output.",
            SessionRowViewModel.RouteToolTipFor(Session(systemSounds: true), true, null, null, "output", null));
    }

    [Fact]
    public void A_session_row_can_be_routed_only_once_its_setting_is_known()
    {
        var row = new SessionRowViewModel(Session());
        Assert.False(row.CanRoute);

        row.SetRoute(true, Headphones, "Headphones (Jabra)", "output");
        Assert.True(row.CanRoute);
        Assert.Equal(Headphones, row.RouteEndpointId);
        Assert.Equal("Headphones (Jabra)", row.RouteText);

        var system = new SessionRowViewModel(Session(systemSounds: true));
        system.SetRoute(true, null, null, "output");
        Assert.False(system.CanRoute);
    }
}
