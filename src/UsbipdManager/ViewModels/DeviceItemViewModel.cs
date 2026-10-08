using UsbipdManager.Core.Models;
using UsbipdManager.Mvvm;

namespace UsbipdManager.ViewModels;

public enum DeviceBadgeKind
{
    NotShared,
    Shared,
    Attached,
    Disconnected,
}

/// <summary>One row of the device list. Updated in place on refresh so keyboard focus stays on the row.</summary>
public sealed class DeviceItemViewModel : ObservableObject
{
    private DeviceEntry _entry;
    private bool _isPending;
    private bool _canInteract;

    public DeviceItemViewModel(DeviceEntry entry, MainViewModel owner)
    {
        _entry = entry;
        ToggleCommand = new AsyncCommand(() => owner.ToggleManagedAsync(this), () => CanToggle);
        ForgetCommand = new AsyncCommand(() => owner.ForgetAsync(this), () => CanInteract && !IsPending);
    }

    public DeviceEntry Entry => _entry;

    public string Key => _entry.DeviceKey;

    public string Description => string.IsNullOrWhiteSpace(_entry.Description) ? "Unknown device" : _entry.Description;

    public string BusIdText => _entry.BusId is { Length: > 0 } busId ? $"Bus {busId}" : "Bus -";

    public string VidPidText => _entry.VidPid ?? "----:----";

    public bool IsManaged => _entry.IsManaged;

    public bool IsConnected => _entry.IsConnected;

    public bool IsDisconnected => !_entry.IsConnected;

    public bool IsInputLike => _entry.IsInputLike;

    public DeviceBadgeKind BadgeKind => !_entry.IsConnected
        ? DeviceBadgeKind.Disconnected
        : _entry.State switch
        {
            UsbDeviceState.Attached => DeviceBadgeKind.Attached,
            UsbDeviceState.Shared => DeviceBadgeKind.Shared,
            _ => DeviceBadgeKind.NotShared,
        };

    public string StateText => BadgeKind switch
    {
        DeviceBadgeKind.Disconnected => "Disconnected",
        DeviceBadgeKind.Attached => "Attached to WSL",
        DeviceBadgeKind.Shared => "Shared",
        _ => "Not shared",
    };

    public string ToggleAutomationName => $"Manage {Description}";

    public string ForgetAutomationName => $"Forget {Description}";

    public string ToggleToolTip => !CanInteract
        ? "Unavailable while an operation runs or when usbipd / WSL2 is not ready"
        : IsManaged
            ? "Managed: switched with the Windows / WSL2 buttons. Turn off to release it."
            : "Turn on to share this device and switch it with the Windows / WSL2 buttons.";

    public bool IsPending
    {
        get => _isPending;
        set
        {
            if (SetProperty(ref _isPending, value))
            {
                RaiseInteraction();
            }
        }
    }

    /// <summary>Set by the owner: the machine is ready and no user operation is running.</summary>
    public bool CanInteract
    {
        get => _canInteract;
        set
        {
            if (SetProperty(ref _canInteract, value))
            {
                RaiseInteraction();
            }
        }
    }

    public bool CanToggle => CanInteract && !IsPending && IsConnected;

    public AsyncCommand ToggleCommand { get; }

    public AsyncCommand ForgetCommand { get; }

    public void Update(DeviceEntry entry)
    {
        _entry = entry;
        // Raise everything: the toggle must re-read IsManaged even when the value did not change (reverts a cancelled click).
        OnPropertyChanged(string.Empty);
        RaiseInteraction();
    }

    /// <summary>Pushes the real managed state back to the switch (after a declined confirmation or a failure).</summary>
    public void RevertToggle() => OnPropertyChanged(nameof(IsManaged));

    private void RaiseInteraction()
    {
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(ToggleToolTip));
        ToggleCommand.RaiseCanExecuteChanged();
        ForgetCommand.RaiseCanExecuteChanged();
    }
}
