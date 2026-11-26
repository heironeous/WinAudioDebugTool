using AudioDeviceDebugger.Models;
using AudioDeviceDebugger.Services;

namespace AudioDeviceDebugger.Tests;

public class DeviceGroupingTests
{
    private static readonly Guid Headset = new("3750bb69-0000-0000-0000-000000000001");
    private static readonly Guid Monitor = new("44a85b0b-0000-0000-0000-000000000002");

    private static int _next;

    private static DeviceInfo Device(string name, Guid? container, string? interfaceName = null,
        DeviceConnectionState state = DeviceConnectionState.Active, int? formFactor = null,
        AudioDirection direction = AudioDirection.Output) => new()
    {
        Id = $"dev-{++_next}",
        FriendlyName = name,
        Direction = direction,
        State = state,
        ContainerId = container,
        InterfaceName = interfaceName,
        FormFactor = formFactor,
    };

    [Fact]
    public void A_headsets_endpoints_share_one_group_named_after_the_device()
    {
        var speaker = Device("Headset Earphone (Jabra Link 380)", Headset, "Jabra Link 380");
        var mic = Device("Headset Microphone (Jabra Link 380)", Headset, "Jabra Link 380", direction: AudioDirection.Input);

        var groups = DeviceGrouping.ByDevice(new[] { speaker, mic });

        Assert.Same(groups[speaker.Id], groups[mic.Id]);
        Assert.Equal("Jabra Link 380", groups[speaker.Id].Title);
    }

    [Fact]
    public void Built_in_devices_are_under_This_PC_which_comes_first()
    {
        var speakers = Device("Speakers (Realtek(R) Audio)", DeviceGrouping.ComputerContainerId, "Realtek(R) Audio");
        var mic = Device("Microphone Array (Intel Smart Sound Technology)", DeviceGrouping.ComputerContainerId, "Intel SST",
            direction: AudioDirection.Input);
        var headset = Device("Headset Earphone (Jabra Link 390)", Headset, "Jabra Link 390");

        var groups = DeviceGrouping.ByDevice(new[] { headset, speakers, mic });

        Assert.Equal("This PC", groups[speakers.Id].Title);
        Assert.Same(groups[speakers.Id], groups[mic.Id]);
        Assert.True(groups[speakers.Id].Order < groups[headset.Id].Order);
    }

    [Fact]
    public void Groups_without_an_active_endpoint_come_last()
    {
        var gone = Device("Speakers (Jabra Link 370)", Headset, "Jabra Link 370", DeviceConnectionState.NotPresent);
        var here = Device("ASUS VP28U (NVIDIA High Definition Audio)", Monitor, "NVIDIA High Definition Audio", formFactor: 9);
        var thisPcGone = Device("Speakers (Realtek USB Audio)", DeviceGrouping.ComputerContainerId, state: DeviceConnectionState.NotPresent);

        var groups = DeviceGrouping.ByDevice(new[] { gone, here, thisPcGone });

        Assert.True(groups[here.Id].Order < groups[thisPcGone.Id].Order);
        Assert.True(groups[thisPcGone.Id].Order < groups[gone.Id].Order);
    }

    [Fact]
    public void Endpoints_without_a_container_are_grouped_as_Other() =>
        Assert.Equal("Other", DeviceGrouping.ByDevice(new[] { Device("Speakers (Virtual)", null) }).Single().Value.Title);

    [Theory]
    [InlineData("ASUS VP28U (NVIDIA High Definition Audio)", 9, "NVIDIA High Definition Audio", "ASUS VP28U")]
    [InlineData("Digital Output (High Definition Audio Device)", 9, "High Definition Audio Device", "High Definition Audio Device")]
    [InlineData("NVIDIA Output (NVIDIA High Definition Audio)", 9, "NVIDIA High Definition Audio", "NVIDIA High Definition Audio")]
    [InlineData("Headphones (2- Jabra Evolve3 75)", 3, "2- Jabra Evolve3 75", "Jabra Evolve3 75")]
    [InlineData("Speakers (Realtek(R) Audio)", 1, null, "Realtek(R) Audio")]
    [InlineData("Line In", 2, null, null)]
    public void Device_name_from_one_endpoint(string friendlyName, int formFactor, string? interfaceName, string? expected) =>
        Assert.Equal(expected, DeviceGrouping.DeviceNameOf(Device(friendlyName, Headset, interfaceName, formFactor: formFactor)));

    [Fact]
    public void The_most_common_name_of_the_active_endpoints_wins()
    {
        var members = new[]
        {
            Device("Speakers (Old Name)", Headset, "Old Name", DeviceConnectionState.NotPresent),
            Device("Speakers (Old Name)", Headset, "Old Name", DeviceConnectionState.NotPresent),
            Device("Speakers (Jabra Evolve3 75)", Headset, "Jabra Evolve3 75"),
        };

        Assert.Equal("Jabra Evolve3 75", DeviceGrouping.TitleOf(members));
    }

    [Fact]
    public void Without_any_name_the_first_endpoint_names_the_group() =>
        Assert.Equal("Line In", DeviceGrouping.TitleOf(new[] { Device("Line In", Headset) }));

    [Theory]
    [InlineData("Speakers (Jabra Link 390)", "Speakers", "Jabra Link 390")]
    [InlineData("Digital Audio (HDMI) (Intel Display Audio)", "Digital Audio (HDMI)", "Intel Display Audio")]
    [InlineData("Line In", "Line In", null)]
    public void Endpoint_names_split_at_the_last_bracket(string name, string endpoint, string? adapter) =>
        Assert.Equal((endpoint, adapter), DeviceGrouping.SplitName(name));

    [Fact]
    public void Sections_put_outputs_first()
    {
        var outputs = DeviceGrouping.SectionOf(AudioDirection.Output);
        var inputs = DeviceGrouping.SectionOf(AudioDirection.Input);

        Assert.Equal("OUTPUTS", outputs.Title);
        Assert.True(outputs.Order < inputs.Order);
        Assert.NotEqual(outputs.Key, inputs.Key);
    }
}
