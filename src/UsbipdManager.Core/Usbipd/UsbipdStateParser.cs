using System.Text.Json;
using UsbipdManager.Core.Models;

namespace UsbipdManager.Core.Usbipd;

/// <summary>Parses the JSON printed by <c>usbipd state</c>.</summary>
public static class UsbipdStateParser
{
    private const string UnknownDescription = "Unknown USB device";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <exception cref="FormatException">The text is not valid <c>usbipd state</c> JSON.</exception>
    public static IReadOnlyList<UsbDevice> Parse(string json)
    {
        StateDto? state;
        try
        {
            state = JsonSerializer.Deserialize<StateDto>(json.Trim().TrimStart('﻿'), Options);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Invalid usbipd state JSON: {ex.Message}", ex);
        }

        if (state?.Devices is null)
        {
            return [];
        }

        var devices = new List<UsbDevice>(state.Devices.Count);
        foreach (var dto in state.Devices)
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.InstanceId))
            {
                continue;
            }

            var instanceId = dto.InstanceId.Trim();
            var clientIp = NullIfBlank(dto.ClientIPAddress);
            var deviceState = clientIp is not null ? UsbDeviceState.Attached
                : NullIfBlank(dto.PersistedGuid) is not null ? UsbDeviceState.Shared
                : UsbDeviceState.NotShared;

            devices.Add(new UsbDevice(
                BusId: NullIfBlank(dto.BusId),
                InstanceId: instanceId,
                DeviceKey: DeviceKeys.FromInstanceId(instanceId),
                Description: NullIfBlank(dto.Description) ?? UnknownDescription,
                VidPid: DeviceKeys.VidPidFromInstanceId(instanceId),
                State: deviceState,
                ClientIpAddress: clientIp));
        }

        return devices;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class StateDto
    {
        public List<DeviceDto?>? Devices { get; set; }
    }

    private sealed class DeviceDto
    {
        public string? BusId { get; set; }

        public string? ClientIPAddress { get; set; }

        public string? Description { get; set; }

        public string? InstanceId { get; set; }

        public string? PersistedGuid { get; set; }
    }
}
