using System.Windows.Controls;
using AudioDeviceDebugger.Services;
using AudioDeviceDebugger.Views;

namespace AudioDeviceDebugger.Tests;

public class AdminOnlyTests
{
    [Fact]
    public void WithAdminHint_AppendsTheHintToTheControlsOwnText()
    {
        Assert.Equal("Do it\n\n" + AppElevation.AdminRequiredHint, AdminOnly.WithAdminHint("Do it"));
        Assert.Equal(AppElevation.AdminRequiredHint, AdminOnly.WithAdminHint(null));
        Assert.Equal(AppElevation.AdminRequiredHint, AdminOnly.WithAdminHint(""));
        Assert.Equal(AppElevation.AdminRequiredHint, AdminOnly.WithAdminHint(new object())); // not text, e.g. a rich tooltip
    }

    // Runs either way: CI runners are elevated, a developer's test run usually isn't.
    [Fact]
    public void IsRequired_DisablesTheControlOnlyWithoutAdminRights()
    {
        bool isEnabled = true, showOnDisabled = false;
        RunOnSta(() =>
        {
            var button = new Button();
            AdminOnly.SetIsRequired(button, true);
            isEnabled = button.IsEnabled;
            showOnDisabled = ToolTipService.GetShowOnDisabled(button);
        });

        Assert.Equal(AppElevation.IsElevated, isEnabled);
        Assert.Equal(!AppElevation.IsElevated, showOnDisabled);
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}
