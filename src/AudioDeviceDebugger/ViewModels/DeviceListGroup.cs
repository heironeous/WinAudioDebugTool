using System.Collections;
using System.ComponentModel;
using System.Windows.Data;
using AudioDeviceDebugger.Services;

namespace AudioDeviceDebugger.ViewModels;

/// <summary>
/// One group of the device list (a section or a physical device), the value the list is grouped by. MainViewModel
/// keeps one instance per key across refreshes, so a collapsed group stays collapsed when the rows are rebuilt.
/// </summary>
public sealed class DeviceListGroup : INotifyPropertyChanged
{
    public DeviceListGroup(DeviceGroupInfo info)
    {
        Key = info.Key;
        _title = info.Title;
        Order = info.Order;
        ToolTip = info.ToolTip;
    }

    public string Key { get; }

    private string _title;
    public string Title
    {
        get => _title;
        private set
        {
            if (_title == value) return;
            _title = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        }
    }

    public int Order { get; private set; }
    public string? ToolTip { get; private set; }

    private bool _isExpanded = true;

    /// <summary>Bound to the group heading's toggle; a collapsed group shows only its heading.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    /// <summary>Takes the latest title and position (a device's name can change when another endpoint of it appears).</summary>
    public void Update(DeviceGroupInfo info)
    {
        Title = info.Title;
        Order = info.Order;
        ToolTip = info.ToolTip;
    }

    public override string ToString() => Title;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Sorts the CollectionView's groups by <see cref="Order"/>, then title.</summary>
    public static IComparer Comparer { get; } = new GroupComparer();

    private sealed class GroupComparer : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if ((x as CollectionViewGroup)?.Name is not DeviceListGroup a || (y as CollectionViewGroup)?.Name is not DeviceListGroup b)
            {
                return 0;
            }
            int order = a.Order.CompareTo(b.Order);
            return order != 0 ? order : StringComparer.CurrentCultureIgnoreCase.Compare(a.Title, b.Title);
        }
    }
}
