using AudioDeviceDebugger.Interop;
using AudioDeviceDebugger.Models;
using AudioDeviceDebugger.Services;

namespace AudioDeviceDebugger.Tests;

public class InventoryFormatTests
{
    [Fact]
    public void Usb_ids_of_a_composite_interface()
    {
        Assert.Equal(new UsbIds("046D", "085C", "0016", 2), InventoryFormat.ParseUsbIds(@"USB\VID_046D&PID_085C&REV_0016&MI_02"));
        Assert.Equal(new UsbIds("0B0E", "245D", null, null), InventoryFormat.ParseUsbIds(@"usb\vid_0b0e&pid_245d\5&28a11e14&0&7"));
        Assert.Null(InventoryFormat.ParseUsbIds(@"HDAUDIO\FUNC_01&VEN_10DE&DEV_009E"));
        Assert.Null(InventoryFormat.ParseUsbIds(null));
    }

    [Theory]
    [InlineData(@"USB\VID_046D&PID_085C\3542D6BF", true)]
    [InlineData(@"USB\VID_046D&PID_085C&MI_02\7&1f3cd360&0&0002", false)]
    [InlineData(@"USB\ROOT_HUB30\4&7dbe872&0&0", false)]
    public void Usb_device_nodes_are_told_from_their_interfaces(string instanceId, bool isDevice) =>
        Assert.Equal(isDevice, InventoryFormat.IsUsbDevice(instanceId));

    [Theory]
    [InlineData("{0.0.0.00000000}.{7a157996-dc76-4ee9-bafb-24ac256a29f8}", @"Render\{7a157996-dc76-4ee9-bafb-24ac256a29f8}")]
    [InlineData("{0.0.1.00000000}.{FA81CA3B-70C9-462E-AF6A-6D8CA6C6CB9A}", @"Capture\{FA81CA3B-70C9-462E-AF6A-6D8CA6C6CB9A}")]
    [InlineData("{0.0.1.00000000}.{FA81CA3B}", null)]
    [InlineData(@"SWD\MMDEVAPI\{0.0.1.00000000}.{FA81CA3B-70C9-462E-AF6A-6D8CA6C6CB9A}", null)]
    public void Endpoint_registry_key(string endpointId, string? expected) =>
        Assert.Equal(expected, InventoryFormat.EndpointRegistryKey(endpointId));

    [Fact]
    public void Connection_is_the_first_real_bus_in_the_chain()
    {
        // A Bluetooth LE Audio headset: audio proxy → LE device → Bluetooth radio → USB adapter.
        Assert.Equal("Bluetooth Low Energy (LE Audio)", InventoryFormat.ConnectionName(new[]
        {
            @"APXENUM\{2D68507B-1813-40EF-A1D6-DB4A25EF1408}&AUDIO\9&57c85c7&0&Speaker",
            @"BTHLE\DEV_0123456789AB\8&1&0", @"BTH\MS_BTHLE\7&1&0", @"USB\VID_0B05&PID_1D70\5&28a11e14&0&7",
        }));
        Assert.Equal("USB", InventoryFormat.ConnectionName(new[] { @"USB\VID_046D&PID_085C&MI_02\7&1&0", @"USB\VID_046D&PID_085C\35" }));
        Assert.Equal("Bluetooth", InventoryFormat.ConnectionName(new[] { @"BTHHFENUM\{0000111e}\8&1", @"BTHENUM\{x}" }));
        Assert.Equal("Virtual (software, no hardware)", InventoryFormat.ConnectionName(new[] { @"ROOT\MEDIA\0000" }));
        Assert.Equal("Unknown", InventoryFormat.ConnectionName(Array.Empty<string>()));
    }

    [Fact]
    public void Bluetooth_is_recognised_but_a_usb_dongle_is_not()
    {
        // Bluetooth Classic: A2DP (BTHENUM) and Hands-Free (BTHHFENUM) audio devices.
        Assert.True(InventoryFormat.IsBluetooth(new[] { @"BTHENUM\{0000110B-0000-1000-8000-00805F9B34FB}_VID&00010067_PID&2546\7&1&0" }));
        Assert.True(InventoryFormat.IsBluetooth(new[] { @"BTHHFENUM\BTHHFPAUDIO\8&A586FA2&2&97", @"BTHENUM\{0000111E}\7&1" }));
        // LE Audio: proxy → LE device → radio → USB adapter.
        Assert.True(InventoryFormat.IsBluetooth(new[]
        {
            @"APXENUM\{2D68507B-1813-40EF-A1D6-DB4A25EF1408}&AUDIO\9&57c85c7&0&Speaker",
            @"BTHLE\DEV_0123456789AB\8&1&0", @"BTH\MS_BTHLE\7&1&0", @"USB\VID_0B05&PID_1D70\5&28a11e14&0&7",
        }));
        // A Jabra Link 390 dongle is plain USB audio to Windows.
        Assert.False(InventoryFormat.IsBluetooth(new[] { @"USB\VID_0B0E&PID_2E56&MI_00\6&1&0", @"USB\VID_0B0E&PID_2E56\1" }));
        Assert.False(InventoryFormat.IsBluetooth(new[] { @"INTELAUDIO\FUNC_01&VEN_10EC\5&1&1" }));
        Assert.False(InventoryFormat.IsBluetooth(Array.Empty<string>()));
    }

    [Theory]
    [InlineData(0x0110, "USB 1.1")]
    [InlineData(0x0200, "USB 2.0")]
    [InlineData(0x0320, "USB 3.2")]
    public void Usb_version(int bcd, string expected) => Assert.Equal(expected, InventoryFormat.UsbVersionText(bcd));

    [Fact]
    public void Usb_speed_and_its_notes()
    {
        var full = new UsbConnectionSpeed(1, 0x0110, false, false);
        Assert.Equal("Full speed (12 Mbit/s)", InventoryFormat.UsbSpeedText(full));
        Assert.Contains("plenty for audio", InventoryFormat.UsbSpeedNote(full));

        var heldBack = new UsbConnectionSpeed(2, 0x0320, false, SuperSpeedCapable: true);
        Assert.Equal("High speed (480 Mbit/s)", InventoryFormat.UsbSpeedText(heldBack));
        Assert.Contains("connected slower", InventoryFormat.UsbSpeedNote(heldBack));

        var plus = new UsbConnectionSpeed(3, 0x0320, OperatingAtSuperSpeedPlus: true, SuperSpeedCapable: true);
        Assert.Equal("SuperSpeed+ (10 Gbit/s or more)", InventoryFormat.UsbSpeedText(plus));
        Assert.Null(InventoryFormat.UsbSpeedNote(plus));
    }

    [Theory]
    [InlineData(0u, true, "Working")]
    [InlineData(28u, true, "No driver installed (code 28)")]
    [InlineData(99u, true, "Problem code 99")]
    [InlineData(0u, false, "Not connected")]
    public void Status(uint code, bool present, string expected) =>
        Assert.Equal(expected, InventoryFormat.ProblemText(code, present));

    [Fact]
    public void Driver_dates_are_shown_as_their_utc_day() =>
        Assert.Equal("2026-03-05", InventoryFormat.DateText(new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc)));

    [Theory]
    [InlineData(5u, "Stream effect (SFX)")]
    [InlineData(13u, "Stream effect (SFX)")]
    [InlineData(15u, "Endpoint effect (EFX)")]
    [InlineData(2u, "Post-mix effect (GFX)")]
    [InlineData(0u, null)]  // PKEY_FX_Association
    [InlineData(4u, null)]  // PKEY_FX_FriendlyName
    public void Effect_slots(uint propertyId, string? expected) => Assert.Equal(expected, InventoryFormat.FxSlotLabel(propertyId));

    [Fact]
    public void Guids_from_registry_values()
    {
        var a = new Guid("c18e2f7e-933d-4965-b7d1-1eef228d2af3");
        var b = new Guid("fc1cfc9b-b9d6-4cfa-b5e0-4bb2166878b2");

        Assert.Equal(new[] { a }, InventoryFormat.ParseGuids("{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}"));
        Assert.Equal(new[] { a, b }, InventoryFormat.ParseGuids(new[] { a.ToString("B"), "not a guid", Guid.Empty.ToString("B"), b.ToString("B") }));
        Assert.Empty(InventoryFormat.ParseGuids(42));
        Assert.Equal("Default, Speech", InventoryFormat.ModesText(new[] { a, b }));
    }

    [Fact]
    public void Inventory_as_text()
    {
        var inventory = new DeviceInventory("Speakers (Realtek USB Audio)", new[]
        {
            new InventorySection("Driver", new[] { new InventoryRow("Provider", "Realtek"), new InventoryRow("Version", "6.4.0.2") }),
            new InventorySection("USB", new[] { new InventoryRow("Speed", "Full speed (12 Mbit/s)", "tooltip is left out") }),
        });

        Assert.Equal(string.Join(Environment.NewLine,
            "Speakers (Realtek USB Audio)", "", "Driver", "  Provider: Realtek", "  Version:  6.4.0.2", "", "USB",
            "  Speed: Full speed (12 Mbit/s)", ""), inventory.ToText());
    }
}
