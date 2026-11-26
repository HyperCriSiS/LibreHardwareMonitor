// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Security;
using Microsoft.Win32;
using OperatingSystem = LibreHardwareMonitor.Software.OperatingSystem;

namespace LibreHardwareMonitor.Hardware.Network;

internal sealed class Network : Hardware
{
    private const string NetworkAdapterClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";

    private readonly Sensor _dataDownloaded;
    private readonly Sensor _dataUploaded;
    private readonly Sensor _downloadSpeed;
    private readonly Sensor _networkUtilization;
    private readonly Sensor _uploadSpeed;
    private long _bytesDownloaded;
    private long _bytesUploaded;
    private long _lastTick;

    public Network(NetworkInterface networkInterface, ISettings settings)
        : base(networkInterface.Name, new Identifier("nic", networkInterface.Id), settings)
    {
        NetworkInterface = networkInterface;
        _dataUploaded = new Sensor("Data Uploaded", 2, SensorType.Data, this, settings);
        ActivateSensor(_dataUploaded);
        _dataDownloaded = new Sensor("Data Downloaded", 3, SensorType.Data, this, settings);
        ActivateSensor(_dataDownloaded);
        _uploadSpeed = new Sensor("Upload Speed", 7, SensorType.Throughput, this, settings);
        ActivateSensor(_uploadSpeed);
        _downloadSpeed = new Sensor("Download Speed", 8, SensorType.Throughput, this, settings);
        ActivateSensor(_downloadSpeed);
        _networkUtilization = new Sensor("Network Utilization", 1, SensorType.Load, this, settings);
        ActivateSensor(_networkUtilization);
        _bytesUploaded = NetworkInterface.GetIPStatistics().BytesSent;
        _bytesDownloaded = NetworkInterface.GetIPStatistics().BytesReceived;
        _lastTick = Stopwatch.GetTimestamp();
    }

    public override HardwareType HardwareType
    {
        get { return HardwareType.Network; }
    }

    internal NetworkInterface NetworkInterface { get; private set; }

    public override IDictionary<string, string> Properties
    {
        get
        {
            SortedDictionary<string, string> properties = new();

            AddProperty("Adapter", NetworkInterface.Description);

            // Virtual adapters can report types without a name.
            if (Enum.IsDefined(typeof(NetworkInterfaceType), NetworkInterface.NetworkInterfaceType))
                AddProperty("Interface type", NetworkInterface.NetworkInterfaceType.ToString());

            if (!OperatingSystem.IsUnix)
                AddDriverProperties();

            return properties;

            void AddProperty(string name, string value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    properties.Add(name, value);
            }

            void AddDriverProperties()
            {
                try
                {
                    using RegistryKey classKey = Registry.LocalMachine.OpenSubKey(NetworkAdapterClassKey);
                    if (classKey == null)
                        return;

                    foreach (string name in classKey.GetSubKeyNames())
                    {
                        // Adapters use numbered keys. Other keys, for example "Properties", are not accessible.
                        if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                            continue;

                        using RegistryKey key = classKey.OpenSubKey(name);
                        if (!NetworkInterface.Id.Equals(key?.GetValue("NetCfgInstanceId") as string, StringComparison.OrdinalIgnoreCase))
                            continue;

                        AddProperty("Driver description", key.GetValue("DriverDesc") as string);
                        AddProperty("Driver provider", key.GetValue("ProviderName") as string);
                        AddProperty("Driver version", key.GetValue("DriverVersion") as string);

                        if (DateTime.TryParseExact(key.GetValue("DriverDate") as string, "M-d-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                            AddProperty("Driver date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                        AddProperty("Matching device ID", key.GetValue("MatchingDeviceId") as string);
                        break;
                    }
                }
                catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException)
                {
                    // The driver information is optional.
                }
            }
        }
    }

    public override void Update()
    {
        try
        {
            if (NetworkInterface == null)
                return;

            long newTick = Stopwatch.GetTimestamp();
            double dt = new TimeSpan(newTick - _lastTick).TotalSeconds;

            IPv4InterfaceStatistics interfaceStats = NetworkInterface.GetIPv4Statistics();

            // Report out the number of bytes that this interface has up/downloaded. Note that these
            // values can reset back at zero (eg: after waking from sleep).
            _dataUploaded.Value = interfaceStats.BytesSent;
            _dataDownloaded.Value = interfaceStats.BytesReceived;

            // Detect a reset in interface stats if the new total is less than what was previously
            // seen. While setting the previous values to zero doesn't encapsulate the value the
            // instant before the reset, it is the best approximation we have.
            if (interfaceStats.BytesSent < _bytesUploaded || interfaceStats.BytesReceived < _bytesDownloaded)
            {
                _bytesUploaded = 0;
                _bytesDownloaded = 0;
            }

            long dBytesUploaded = interfaceStats.BytesSent - _bytesUploaded;
            long dBytesDownloaded = interfaceStats.BytesReceived - _bytesDownloaded;

            // Upload and download speeds are reported as the number of bytes transfered over the
            // time difference since the last report. In this way, the values represent the average
            // number of bytes up/downloaded in a second.
            _uploadSpeed.Value = (float)(dBytesUploaded / dt);
            _downloadSpeed.Value = (float)(dBytesDownloaded / dt);

            // Network speed is in bits per second, so when calculating the load on the NIC we first
            // grab the total number of bits up/downloaded
            long dbits = (dBytesUploaded + dBytesDownloaded) * 8;

            // Converts the ratio of total bits transferred over time over theoretical max bits
            // transfer rate into a percentage load
            double load = (dbits / dt / NetworkInterface.Speed) * 100;

            // Finally clamp the value between 0% and 100% to avoid reporting nonsensical numbers
            _networkUtilization.Value = (float)Math.Min(Math.Max(load, 0), 100);

            // Store the recorded values and time, so they can be used in the next update
            _bytesUploaded = interfaceStats.BytesSent;
            _bytesDownloaded = interfaceStats.BytesReceived;
            _lastTick = newTick;
        }
        catch (NetworkInformationException networkInformationException) when (unchecked(networkInformationException.HResult == (int)0x80004005))
        {
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.Id.Equals(NetworkInterface?.Id))
                {
                    NetworkInterface = networkInterface;
                    break;
                }
            }
        }
    }
}