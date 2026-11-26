// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) 2024 demorfi<demorfi@gmail.com>
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System.Collections.Generic;
using System.Globalization;
using HidSharp;

namespace LibreHardwareMonitor.Hardware.Psu.Msi;

internal sealed class MsiPsu : Hardware
{
    private readonly HidDevice _device;
    private readonly List<PsuSensor> _sensors = [];
    private readonly bool _isPcie5Layout;

    public MsiPsu(HidDevice device, ISettings settings, int index, bool isPcie5Layout = false)
        : base("MSI PSU", new Identifier("psu", "msi", index.ToString()), settings)
    {
        _device = device;
        _isPcie5Layout = isPcie5Layout;
        using HidStream stream = device.Open();
        UsbApi.FirmwareInfo fwInfo = UsbApi.FwInfo(stream);
        Name = $"{fwInfo.Vendor} {fwInfo.Product}";

        AddSensors(settings);
    }

    public override HardwareType HardwareType => HardwareType.Psu;

    public override void Update()
    {
        using HidStream stream = _device.Open();
        float[] info = _isPcie5Layout ? UsbApi.InfoListPcie5(stream) : UsbApi.InfoList(stream);
        _sensors.ForEach(s => s.Update(info));
    }

    private void AddSensors(ISettings settings)
    {
        int sensorIndex = 0;

        if (_isPcie5Layout)
        {
            AddPcie5Sensors(settings);
            return;
        }

        _sensors.Add(new PsuSensor("Case", sensorIndex++, SensorType.Fan, this, settings, UsbApi.IndexInfo.FAN_RPM));
        _sensors.Add(new PsuSensor("Case", sensorIndex++, SensorType.Temperature, this, settings, UsbApi.IndexInfo.TEMP));

        _sensors.Add(new PsuSensor("+12V", sensorIndex++, SensorType.Voltage, this, settings, UsbApi.IndexInfo.VOLTS_12));
        _sensors.Add(new PsuSensor("+12V", sensorIndex++, SensorType.Current, this, settings, UsbApi.IndexInfo.AMPS_12));

        _sensors.Add(new PsuSensor("+5V", sensorIndex++, SensorType.Voltage, this, settings, UsbApi.IndexInfo.VOLTS_5));
        _sensors.Add(new PsuSensor("+5V", sensorIndex++, SensorType.Current, this, settings, UsbApi.IndexInfo.AMPS_5));

        _sensors.Add(new PsuSensor("+3.3V", sensorIndex++, SensorType.Voltage, this, settings, UsbApi.IndexInfo.VOLTS_3V3));
        _sensors.Add(new PsuSensor("+3.3V", sensorIndex++, SensorType.Current, this, settings, UsbApi.IndexInfo.AMPS_3V3));

        _sensors.Add(new PsuSensor("PSU Efficiency", sensorIndex++, SensorType.Level, this, settings, UsbApi.IndexInfo.EFFICIENCY));
        _sensors.Add(new PsuSensor("PSU Out", sensorIndex++, SensorType.Power, this, settings, UsbApi.IndexInfo.PSU_OUT));
        _sensors.Add(new PsuSensor("Total Runtime", sensorIndex++, SensorType.TimeSpan, this, settings, UsbApi.IndexInfo.RUNTIME, true));
    }

    /// <summary>
    /// Adds the sensors of the PCIe 5 layout, which reports currents but no voltages.
    /// </summary>
    private void AddPcie5Sensors(ISettings settings)
    {
        int sensorIndex = 0;

        _sensors.Add(new PsuSensor("Case", sensorIndex++, SensorType.Fan, this, settings, UsbApi.IndexInfo.FAN_RPM));
        _sensors.Add(new PsuSensor("Case", sensorIndex++, SensorType.Temperature, this, settings, UsbApi.IndexInfo.TEMP));
        _sensors.Add(new PsuSensor("+12V", sensorIndex++, SensorType.Current, this, settings, UsbApi.IndexInfo.AMPS_12));
        _sensors.Add(new PsuSensor("+5V", sensorIndex++, SensorType.Current, this, settings, UsbApi.IndexInfo.AMPS_5));
        _sensors.Add(new PsuSensor("+3.3V", sensorIndex++, SensorType.Current, this, settings, UsbApi.IndexInfo.AMPS_3V3));
        _sensors.Add(new PsuSensor("PSU Efficiency", sensorIndex++, SensorType.Level, this, settings, UsbApi.IndexInfo.EFFICIENCY));
        _sensors.Add(new PsuSensor("PSU Out", sensorIndex++, SensorType.Power, this, settings, UsbApi.IndexInfo.PSU_OUT));

        for (int i = 0; i < UsbApi.PinCount; i++)
        {
            string name = $"12V-2x6 #{(i / 6) + 1} Pin {(i % 6) + 1}";
            _sensors.Add(new PsuSensor(name, sensorIndex++, SensorType.Current, this, settings, UsbApi.IndexInfo.AMPS_PIN_1 + i));
        }
    }

    private class PsuSensor : Sensor
    {
        private readonly UsbApi.IndexInfo _indexInfo;

        public PsuSensor(string name, int index, SensorType type, MsiPsu hardware, ISettings settings, UsbApi.IndexInfo indexInfo, bool noHistory = false)
            : base(name, index, false, type, hardware, null, settings, noHistory)
        {
            _indexInfo = indexInfo;
            hardware.ActivateSensor(this);
        }

        public void Update(float[] info)
        {
            Value = info[(int)_indexInfo];
        }
    }
}
