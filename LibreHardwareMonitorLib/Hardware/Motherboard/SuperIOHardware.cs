// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using LibreHardwareMonitor.Hardware.Motherboard.Lpc;

namespace LibreHardwareMonitor.Hardware.Motherboard;

internal sealed class SuperIOHardware : Hardware
{
    private readonly List<Sensor> _controls = [];
    private readonly List<Sensor> _fans = [];
    private readonly Motherboard _motherboard;

    private readonly UpdateDelegate _postUpdate;
    private readonly ReadValueDelegate _readControl;
    private readonly ReadValueDelegate _readFan;
    private readonly ReadValueDelegate _readTemperature;
    private readonly ReadValueDelegate _readVoltage;

    private readonly ISuperIO _superIO;
    private readonly List<Sensor> _temperatures = [];
    private readonly List<Sensor> _voltages = [];

    public SuperIOHardware(Motherboard motherboard, ISuperIO superIO, Manufacturer manufacturer, Model model, ISettings settings, int index)
        : base(ChipName.GetName(superIO.Chip), new Identifier("lpc", superIO.Chip.ToString().ToLowerInvariant(), index.ToString()), settings)
    {
        _motherboard = motherboard;
        _superIO = superIO;

        GetBoardSpecificConfiguration(superIO,
                                      manufacturer,
                                      model,
                                      motherboard.SMBios.Board?.ProductName,
                                      index,
                                      out IList<Voltage> v,
                                      out IList<Temperature> t,
                                      out IList<Fan> f,
                                      out IList<Control> c,
                                      out _readVoltage,
                                      out _readTemperature,
                                      out _readFan,
                                      out _readControl,
                                      out _postUpdate,
                                      out _);

        CreateVoltageSensors(superIO, settings, v);
        CreateTemperatureSensors(superIO, settings, t);
        CreateFanSensors(superIO, settings, f);
        CreateControlSensors(superIO, settings, c);
    }

    public override HardwareType HardwareType
    {
        get { return HardwareType.SuperIO; }
    }

    public override IHardware Parent
    {
        get { return _motherboard; }
    }

    private void CreateControlSensors(ISuperIO superIO, ISettings settings, IList<Control> c)
    {
        foreach (Control ctrl in c)
        {
            int index = ctrl.Index;
            if (index < superIO.Controls.Length)
            {
                Sensor sensor = new(ctrl.Name, index, SensorType.Control, this, settings);
                LibreHardwareMonitor.Hardware.Control control = new(sensor, settings, 0, 100);
                control.ControlModeChanged += cc =>
                {
                    switch (cc.ControlMode)
                    {
                        case ControlMode.Default:
                            superIO.SetControl(index, null);
                            break;

                        case ControlMode.Software:
                            superIO.SetControl(index, GetSoftwareValueAsByte(cc));
                            break;

                        default:
                            return;
                    }
                };

                control.SoftwareControlValueChanged += cc =>
                {
                    if (cc.ControlMode == ControlMode.Software)
                        superIO.SetControl(index, GetSoftwareValueAsByte(cc));
                };

                switch (control.ControlMode)
                {
                    case ControlMode.Undefined:
                        break;

                    case ControlMode.Default:
                        superIO.SetControl(index, null);
                        break;

                    case ControlMode.Software:
                        superIO.SetControl(index, GetSoftwareValueAsByte(control));
                        break;
                }

                sensor.Control = control;
                _controls.Add(sensor);
                ActivateSensor(sensor);
            }
        }
    }

    private static byte GetSoftwareValueAsByte(LibreHardwareMonitor.Hardware.Control control)
    {
        const float percentToByteRatio = 2.55f;
        float value = control.SoftwareValue * percentToByteRatio;
        return (byte)value;
    }

    private void CreateFanSensors(ISuperIO superIO, ISettings settings, IList<Fan> f)
    {
        foreach (Fan fan in f)
        {
            if (fan.Index < superIO.Fans.Length)
            {
                Sensor sensor = new(fan.Name, fan.Index, SensorType.Fan, this, settings);
                _fans.Add(sensor);
            }
        }
    }

    private void CreateTemperatureSensors(ISuperIO superIO, ISettings settings, IList<Temperature> t)
    {
        foreach (Temperature temperature in t)
        {
            if (temperature.Index < superIO.Temperatures.Length)
            {
                Sensor sensor = new(GetTemperatureName(superIO, temperature),
                                    temperature.Index,
                                    SensorType.Temperature,
                                    this,
                                    [new ParameterDescription("Offset [°C]", "Temperature offset.", 0)],
                                    settings);

                _temperatures.Add(sensor);
            }
        }
    }

    /// <summary>
    /// Adds the chip input to placeholder names, so unknown boards show which input a channel reads.
    /// </summary>
    private static string GetTemperatureName(ISuperIO superIO, Temperature temperature)
    {
        if (superIO is Nct677X nct &&
            temperature.Name.StartsWith("Temperature #", StringComparison.Ordinal) &&
            nct.GetTemperatureSourceName(temperature.Index) is { } source)
        {
            return $"{temperature.Name} ({source})";
        }

        return temperature.Name;
    }

    private void CreateVoltageSensors(ISuperIO superIO, ISettings settings, IList<Voltage> v)
    {
        const string formula = "Voltage = value + (value - Vf) * Ri / Rf.";
        foreach (Voltage voltage in v)
        {
            if (voltage.Index < superIO.Voltages.Length)
            {
                Sensor sensor = new(voltage.Name,
                                    voltage.Index,
                                    voltage.Hidden,
                                    SensorType.Voltage,
                                    this,
                                    [
                                        new ParameterDescription("Ri [kΩ]", "Input resistance.\n" + formula, voltage.Ri),
                                        new ParameterDescription("Rf [kΩ]", "Reference resistance.\n" + formula, voltage.Rf),
                                        new ParameterDescription("Vf [V]", "Reference voltage.\n" + formula, voltage.Vf)
                                    ],
                                    settings);

                _voltages.Add(sensor);
            }
        }
    }

    private static void GetBoardSpecificConfiguration
    (
        ISuperIO superIO,
        Manufacturer manufacturer,
        Model model,
        string productName,
        int superIOIndex,
        out IList<Voltage> v,
        out IList<Temperature> t,
        out IList<Fan> f,
        out IList<Control> c,
        out ReadValueDelegate readVoltage,
        out ReadValueDelegate readTemperature,
        out ReadValueDelegate readFan,
        out ReadValueDelegate readControl,
        out UpdateDelegate postUpdate,
        out Mutex mutex)
    {
        readVoltage = index => superIO.Voltages[index];
        readTemperature = index => superIO.Temperatures[index];
        readFan = index => superIO.Fans[index];
        readControl = index => superIO.Controls[index];

        postUpdate = () => { };
        mutex = null;

        v = new List<Voltage>();
        t = new List<Temperature>();
        f = new List<Fan>();
        c = new List<Control>();

        // Boards in the data file get their sensor lists from it. Only boards that need their own read functions, a mutex
        // or GPIO access, and the generic configuration of each chip and manufacturer, are defined in the code below.
        if (SuperIOBoardData.TryGet(superIO.Chip, manufacturer, model, productName, superIOIndex, out SuperIOBoardData.Entry entry))
        {
            v = new List<Voltage>(entry.Voltages);
            t = new List<Temperature>(entry.Temperatures);
            f = new List<Fan>(entry.Fans);
            c = new List<Control>(entry.Controls);
            return;
        }

        switch (superIO.Chip)
        {
            case Chip.IT8705F:
            case Chip.IT8712F:
            case Chip.IT8716F:
            case Chip.IT8718F:
            case Chip.IT8720F:
            case Chip.IT8726F:
                GetIteConfigurationsA(superIO, manufacturer, model, v, t, f, c, ref readFan, ref postUpdate, ref mutex);
                break;

            case Chip.IT8613E:
            case Chip.IT8620E:
            case Chip.IT8625E:
            case Chip.IT8628E:
            case Chip.IT8631E:
            case Chip.IT8638E:
            case Chip.IT8655E:
            case Chip.IT8665E:
            case Chip.IT8686E:
            case Chip.IT8688E:
            case Chip.IT8689E:
            case Chip.IT8721F:
            case Chip.IT8728F:
            case Chip.IT8771E:
            case Chip.IT8772E:
            case Chip.IT8696E:
                GetIteConfigurationsB(superIO, manufacturer, model, v, t, f, c);
                break;

            case Chip.IT87952E:
            case Chip.IT8792E:
            case Chip.IT8790E:
                GetIteConfigurationsC(superIO, manufacturer, model, v, t, f, c);
                break;

            case Chip.F71858:
                v.Add(new Voltage("VCC3V", 0, 150, 150));
                v.Add(new Voltage("VSB3V", 1, 150, 150));
                v.Add(new Voltage("Battery", 2, 150, 150));

                for (int i = 0; i < superIO.Temperatures.Length; i++)
                    t.Add(new Temperature("Temperature #" + (i + 1), i));

                for (int i = 0; i < superIO.Fans.Length; i++)
                    f.Add(new Fan("Fan #" + (i + 1), i));

                break;

            case Chip.F71808E:
            case Chip.F71862:
            case Chip.F71869:
            case Chip.F71869A:
            case Chip.F71882:
            case Chip.F71889AD:
            case Chip.F71889ED:
            case Chip.F71889F:
                GetFintekConfiguration(superIO, manufacturer, model, v, t, f, c);
                break;

            case Chip.W83627EHF:
                GetWinbondConfigurationEhf(manufacturer, model, v, t, f, c);
                break;

            case Chip.W83627DHG:
            case Chip.W83627DHGP:
            case Chip.W83667HG:
            case Chip.W83667HGB:
                GetWinbondConfigurationHg(manufacturer, model, v, t, f, c);
                break;

            case Chip.W83627HF:
                v.Add(new Voltage("Vcore", 0));
                v.Add(new Voltage("Voltage #2", 1, true));
                v.Add(new Voltage("Voltage #3", 2, true));
                v.Add(new Voltage("AVCC", 3, 34, 51));
                v.Add(new Voltage("Voltage #5", 4, true));
                v.Add(new Voltage("+5VSB", 5, 34, 51));
                v.Add(new Voltage("CMOS Battery", 6));
                t.Add(new Temperature("CPU", 0));
                t.Add(new Temperature("Auxiliary", 1));
                t.Add(new Temperature("System", 2));
                f.Add(new Fan("System Fan", 0));
                f.Add(new Fan("CPU Fan", 1));
                f.Add(new Fan("Auxiliary Fan", 2));
                c.Add(new Control("Fan 1", 0));
                c.Add(new Control("Fan 2", 1));
                break;

            case Chip.W83627THF:
            case Chip.W83687THF:
                v.Add(new Voltage("Vcore", 0));
                v.Add(new Voltage("Voltage #2", 1, true));
                v.Add(new Voltage("Voltage #3", 2, true));
                v.Add(new Voltage("AVCC", 3, 34, 51));
                v.Add(new Voltage("Voltage #5", 4, true));
                v.Add(new Voltage("+5VSB", 5, 34, 51));
                v.Add(new Voltage("CMOS Battery", 6));
                t.Add(new Temperature("CPU", 0));
                t.Add(new Temperature("Auxiliary", 1));
                t.Add(new Temperature("System", 2));
                f.Add(new Fan("System Fan", 0));
                f.Add(new Fan("CPU Fan", 1));
                f.Add(new Fan("Auxiliary Fan", 2));
                c.Add(new Control("System Fan", 0));
                c.Add(new Control("CPU Fan", 1));
                c.Add(new Control("Auxiliary Fan", 2));
                break;

            case Chip.NCT5585D:
                break;

            case Chip.NCT6771F:
            case Chip.NCT6776F:
                GetNuvotonConfigurationF(superIO, manufacturer, model, v, t, f, c);
                break;

            case Chip.NCT610XD:
                v.Add(new Voltage("Vcore", 0));
                v.Add(new Voltage("Voltage #0", 1, true));
                v.Add(new Voltage("AVCC", 2, 34, 34));
                v.Add(new Voltage("+3.3V", 3, 34, 34));
                v.Add(new Voltage("Voltage #1", 4, true));
                v.Add(new Voltage("Voltage #2", 5, true));
                v.Add(new Voltage("Reserved", 6, true));
                v.Add(new Voltage("+3V Standby", 7, 34, 34));
                v.Add(new Voltage("CMOS Battery", 8, 34, 34));
                v.Add(new Voltage("Voltage #10", 9, true));
                t.Add(new Temperature("CPU Core", 0));
                t.Add(new Temperature("Auxiliary", 1));
                t.Add(new Temperature("System0", 2));
                t.Add(new Temperature("System1", 3));
                t.Add(new Temperature("System2", 4));
                t.Add(new Temperature("System3", 5));

                for (int i = 0; i < superIO.Fans.Length; i++)
                    f.Add(new Fan("Fan #" + (i + 1), i));

                for (int i = 0; i < superIO.Controls.Length; i++)
                    c.Add(new Control("Fan #" + (i + 1), i));

                break;

            case Chip.NCT6779D:
            case Chip.NCT6791D:
            case Chip.NCT6792D:
            case Chip.NCT6792DA:
            case Chip.NCT6793D:
            case Chip.NCT6795D:
            case Chip.NCT6796D:
            case Chip.NCT6796DR:
            case Chip.NCT6796DS:
            case Chip.NCT6797D:
            case Chip.NCT6798D:
            case Chip.NCT6799D:
            case Chip.NCT6701D:
            case Chip.NCT6683D:
                GetNuvotonConfigurationD(superIO, manufacturer, model, superIOIndex, v, t, f, c);
                break;

            case Chip.NCT6686D:
            case Chip.NCT6687D:
                v.Add(new Voltage("+12V", 0));
                v.Add(new Voltage("+5V", 1));
                v.Add(new Voltage("Vcore", 2));
                v.Add(new Voltage("Voltage #1", 3));
                v.Add(new Voltage("DIMM", 4));
                v.Add(new Voltage("CPU I/O", 5));
                v.Add(new Voltage("CPU System Agent", 6));
                v.Add(new Voltage("Voltage #2", 7));
                v.Add(new Voltage("AVCC3", 8));
                v.Add(new Voltage("CPU Termination", 9));
                v.Add(new Voltage("VRef", 10));
                v.Add(new Voltage("VSB", 11));
                v.Add(new Voltage("AVSB", 12));
                v.Add(new Voltage("CMOS Battery", 13));

                t.Add(new Temperature("CPU", 0));
                t.Add(new Temperature("System", 1));
                t.Add(new Temperature("VRM MOS", 2));
                t.Add(new Temperature("PCH", 3));
                t.Add(new Temperature("CPU Socket", 4));
                t.Add(new Temperature("PCIe x1", 5));
                t.Add(new Temperature("M2 #1", 6));

                f.Add(new Fan("CPU Fan", 0));
                f.Add(new Fan("Pump Fan", 1));
                f.Add(new Fan("System Fan #1", 2));
                f.Add(new Fan("System Fan #2", 3));
                f.Add(new Fan("System Fan #3", 4));
                f.Add(new Fan("System Fan #4", 5));
                f.Add(new Fan("System Fan #5", 6));
                f.Add(new Fan("System Fan #6", 7));

                c.Add(new Control("CPU Fan", 0));
                c.Add(new Control("Pump Fan", 1));
                c.Add(new Control("System Fan #1", 2));
                c.Add(new Control("System Fan #2", 3));
                c.Add(new Control("System Fan #3", 4));
                c.Add(new Control("System Fan #4", 5));
                c.Add(new Control("System Fan #5", 6));
                c.Add(new Control("System Fan #6", 7));

                break;

            case Chip.NCT6687DR:

                // Universal Sensor and Control defaults
                t.Add(new Temperature("CPU Core", 0));
                t.Add(new Temperature("System", 1));
                t.Add(new Temperature("VRM MOS", 2));
                t.Add(new Temperature("Chipset", 3));

                f.Add(new Fan("CPU Fan", 0));
                f.Add(new Fan("Pump Fan #1", 1));
                f.Add(new Fan("Chipset Fan", 2));
                f.Add(new Fan("System Fan #1", 10));
                f.Add(new Fan("System Fan #2", 11));
                f.Add(new Fan("System Fan #3", 12));
                f.Add(new Fan("System Fan #4", 13));
                f.Add(new Fan("System Fan #5", 14));
                f.Add(new Fan("System Fan #6", 15));
                f.Add(new Fan("EZ-Connect Fan", 3));

                c.Add(new Control("CPU Fan", 0));
                c.Add(new Control("Pump Fan", 1));
                c.Add(new Control("Chipset Fan", 2));
                c.Add(new Control("System Fan #1", 10));
                c.Add(new Control("System Fan #2", 11));
                c.Add(new Control("System Fan #3", 12));
                c.Add(new Control("System Fan #4", 13));
                c.Add(new Control("System Fan #5", 14));
                c.Add(new Control("System Fan #6", 15));
                c.Add(new Control("EZ-Connect Fan", 3));

                v.Add(new Voltage("+12V", 0));
                v.Add(new Voltage("+5V", 1));
                v.Add(new Voltage("CPU Northbridge/SoC", 2));
                v.Add(new Voltage("DIMM", 3, 1, 1));
                v.Add(new Voltage("Vcore", 4, -1, 2));
                v.Add(new Voltage("Chipset", 5));
                v.Add(new Voltage("CPU System Agent", 6));
                //v.Add(new Voltage("Unknown_4", 7)); //"Voltage #2"
                v.Add(new Voltage("+3.3V", 8));
                v.Add(new Voltage("VREF", 9));
                v.Add(new Voltage("+1.8V", 10));
                v.Add(new Voltage("+3V Standby", 11));
                v.Add(new Voltage("AVSB", 12));
                v.Add(new Voltage("CMOS Battery", 13));

                break;

            case Chip.IPMI:
                Ipmi ipmi = (Ipmi)superIO;

                foreach (Temperature temperature in ipmi.GetTemperatures())
                    t.Add(temperature);

                foreach (Fan fan in ipmi.GetFans())
                    f.Add(fan);

                foreach (Voltage voltage in ipmi.GetVoltages())
                    v.Add(voltage);

                foreach (Control control in ipmi.GetControls())
                    c.Add(control);

                break;

            default:
                GetDefaultConfiguration(superIO, v, t, f, c);
                break;
        }
    }

    private static void GetDefaultConfiguration(ISuperIO superIO, ICollection<Voltage> v, ICollection<Temperature> t, ICollection<Fan> f, ICollection<Control> c)
    {
        for (int i = 0; i < superIO.Voltages.Length; i++)
            v.Add(new Voltage("Voltage #" + (i + 1), i, true));

        for (int i = 0; i < superIO.Temperatures.Length; i++)
            t.Add(new Temperature("Temperature #" + (i + 1), i));

        for (int i = 0; i < superIO.Fans.Length; i++)
            f.Add(new Fan("Fan #" + (i + 1), i));

        for (int i = 0; i < superIO.Controls.Length; i++)
            c.Add(new Control("Fan #" + (i + 1), i));
    }

    private static void GetIteConfigurationsA
    (
        ISuperIO superIO,
        Manufacturer manufacturer,
        Model model,
        IList<Voltage> v,
        IList<Temperature> t,
        IList<Fan> f,
        ICollection<Control> c,
        ref ReadValueDelegate readFan,
        ref UpdateDelegate postUpdate,
        ref Mutex mutex)
    {
        switch (manufacturer)
        {
            case Manufacturer.ASRock:
                switch (model)
                {
                    case Model.P55_Deluxe: // IT8720F
                        GetASRockConfiguration(superIO,
                                               v,
                                               t,
                                               f,
                                               ref readFan,
                                               ref postUpdate,
                                               out mutex);

                        break;

                    default:
                        v.Add(new Voltage("Vcore", 0));
                        v.Add(new Voltage("Voltage #2", 1, true));
                        v.Add(new Voltage("Voltage #3", 2, true));
                        v.Add(new Voltage("Voltage #4", 3, true));
                        v.Add(new Voltage("Voltage #5", 4, true));
                        v.Add(new Voltage("Voltage #6", 5, true));
                        v.Add(new Voltage("Voltage #7", 6, true));
                        v.Add(new Voltage("Voltage #8", 7, true));
                        v.Add(new Voltage("CMOS Battery", 8));

                        for (int i = 0; i < superIO.Temperatures.Length; i++)
                            t.Add(new Temperature("Temperature #" + (i + 1), i));

                        for (int i = 0; i < superIO.Fans.Length; i++)
                            f.Add(new Fan("Fan #" + (i + 1), i));

                        break;
                }

                break;

            case Manufacturer.DFI:
                v.Add(new Voltage("Vcore", 0));
                v.Add(new Voltage("CPU Termination", 1, true));
                v.Add(new Voltage("+3.3V", 2, true));
                v.Add(new Voltage("+5V", 3, 6.8f, 10, 0, true));
                v.Add(new Voltage("+12V", 4, 30, 10, 0, true));
                v.Add(new Voltage("Voltage #6", 5, true));
                v.Add(new Voltage("DIMM", 6, true));
                v.Add(new Voltage("+5VSB", 7, 6.8f, 10, 0, true));
                v.Add(new Voltage("CMOS Battery", 8));

                for (int i = 0; i < superIO.Temperatures.Length; i++)
                    t.Add(new Temperature("Temperature #" + (i + 1), i));

                for (int i = 0; i < superIO.Fans.Length; i++)
                    f.Add(new Fan("Fan #" + (i + 1), i));

                for (int i = 0; i < superIO.Controls.Length; i++)
                    c.Add(new Control("Fan #" + (i + 1), i));

                break;

            case Manufacturer.Gigabyte:
                v.Add(new Voltage("Vcore", 0));
                v.Add(new Voltage("DIMM", 1, true));
                v.Add(new Voltage("+3.3V", 2, true));
                v.Add(new Voltage("+5V", 3, 6.8f, 10, 0, true));
                v.Add(new Voltage("Voltage #5", 4, true));
                v.Add(new Voltage("Voltage #6", 5, true));
                v.Add(new Voltage("Voltage #7", 6, true));
                v.Add(new Voltage("Voltage #8", 7, true));
                v.Add(new Voltage("CMOS Battery", 8));

                for (int i = 0; i < superIO.Temperatures.Length; i++)
                    t.Add(new Temperature("Temperature #" + (i + 1), i));

                for (int i = 0; i < superIO.Fans.Length; i++)
                    f.Add(new Fan("Fan #" + (i + 1), i));

                for (int i = 0; i < superIO.Controls.Length; i++)
                    c.Add(new Control("Fan #" + (i + 1), i));

                break;

            default:
                v.Add(new Voltage("Vcore", 0));
                v.Add(new Voltage("Voltage #2", 1, true));
                v.Add(new Voltage("Voltage #3", 2, true));
                v.Add(new Voltage("Voltage #4", 3, true));
                v.Add(new Voltage("Voltage #5", 4, true));
                v.Add(new Voltage("Voltage #6", 5, true));
                v.Add(new Voltage("Voltage #7", 6, true));
                v.Add(new Voltage("Voltage #8", 7, true));
                v.Add(new Voltage("CMOS Battery", 8));

                for (int i = 0; i < superIO.Temperatures.Length; i++)
                    t.Add(new Temperature("Temperature #" + (i + 1), i));

                for (int i = 0; i < superIO.Fans.Length; i++)
                    f.Add(new Fan("Fan #" + (i + 1), i));

                for (int i = 0; i < superIO.Controls.Length; i++)
                    c.Add(new Control("Fan #" + (i + 1), i));

                break;
        }
    }

    private static void GetASRockConfiguration
    (
        ISuperIO superIO,
        IList<Voltage> v,
        IList<Temperature> t,
        IList<Fan> f,
        ref ReadValueDelegate readFan,
        ref UpdateDelegate postUpdate,
        out Mutex mutex)
    {
        v.Add(new Voltage("Vcore", 0));
        v.Add(new Voltage("+3.3V", 2));
        v.Add(new Voltage("+12V", 4, 30, 10));
        v.Add(new Voltage("+5V", 5, 6.8f, 10));
        v.Add(new Voltage("CMOS Battery", 8));
        t.Add(new Temperature("CPU", 0));
        t.Add(new Temperature("Motherboard", 1));
        f.Add(new Fan("CPU Fan", 0));
        f.Add(new Fan("Chassis Fan #1", 1));

        // this mutex is also used by the official ASRock tool
        mutex = new Mutex(false, "ASRockOCMark");

        bool exclusiveAccess = false;
        try
        {
            exclusiveAccess = mutex.WaitOne(10, false);
        }
        catch (AbandonedMutexException)
        { }
        catch (InvalidOperationException)
        { }

        // only read additional fans if we get exclusive access
        if (exclusiveAccess)
        {
            f.Add(new Fan("Chassis Fan #2", 2));
            f.Add(new Fan("Chassis Fan #3", 3));
            f.Add(new Fan("Power Fan", 4));

            readFan = index =>
            {
                if (index < 2)
                {
                    return superIO.Fans[index];
                }

                // get GPIO 80-87
                byte? gpio = superIO.ReadGpio(7);
                if (!gpio.HasValue)
                    return null;

                // read the last 3 fans based on GPIO 83-85
                int[] masks = [0x05, 0x03, 0x06];
                return ((gpio.Value >> 3) & 0x07) == masks[index - 2] ? superIO.Fans[2] : null;
            };

            int fanIndex = 0;

            postUpdate = () =>
            {
                // get GPIO 80-87
                byte? gpio = superIO.ReadGpio(7);
                if (!gpio.HasValue)
                    return;

                // prepare the GPIO 83-85 for the next update
                int[] masks = [0x05, 0x03, 0x06];
                superIO.WriteGpio(7, (byte)((gpio.Value & 0xC7) | (masks[fanIndex] << 3)));
                fanIndex = (fanIndex + 1) % 3;
            };
        }
    }

    private static void GetIteConfigurationsB(ISuperIO superIO, Manufacturer manufacturer, Model model, IList<Voltage> v, IList<Temperature> t, IList<Fan> f, IList<Control> c)
    {
        switch (manufacturer)
        {
            case Manufacturer.ASUS:
                v.Add(new Voltage("Vcore", 0));
                v.Add(new Voltage("Voltage #2", 1, true));
                v.Add(new Voltage("Voltage #3", 2, true));
                v.Add(new Voltage("Voltage #4", 3, true));
                v.Add(new Voltage("Voltage #5", 4, true));
                v.Add(new Voltage("Voltage #6", 5, true));
                v.Add(new Voltage("Voltage #7", 6, true));
                v.Add(new Voltage("Voltage #8", 7, true));
                v.Add(new Voltage("CMOS Battery", 8));

                for (int i = 0; i < superIO.Temperatures.Length; i++)
                    t.Add(new Temperature("Temperature #" + (i + 1), i));

                for (int i = 0; i < superIO.Fans.Length; i++)
                    f.Add(new Fan("Fan #" + (i + 1), i));

                for (int i = 0; i < superIO.Controls.Length; i++)
                    c.Add(new Control("Fan #" + (i + 1), i));

                break;

            case Manufacturer.ECS:
                v.Add(new Voltage("Voltage #1", 0, true));
                v.Add(new Voltage("Voltage #2", 1, true));
                v.Add(new Voltage("Voltage #3", 2, true));
                v.Add(new Voltage("AVCC", 3, 10, 10, 0, true));
                v.Add(new Voltage("Voltage #5", 4, true));
                v.Add(new Voltage("Voltage #6", 5, true));
                v.Add(new Voltage("Voltage #7", 6, true));
                v.Add(new Voltage("+3V Standby", 7, 10, 10, 0, true));
                v.Add(new Voltage("CMOS Battery", 8, 10, 10));

                for (int i = 0; i < superIO.Temperatures.Length; i++)
                    t.Add(new Temperature("Temperature #" + (i + 1), i));

                for (int i = 0; i < superIO.Fans.Length; i++)
                    f.Add(new Fan("Fan #" + (i + 1), i));

                for (int i = 0; i < superIO.Controls.Length; i++)
                    c.Add(new Control("Fan #" + (i + 1), i));

                break;

            default:
                v.Add(new Voltage("Voltage #1", 0, true));
                v.Add(new Voltage("Voltage #2", 1, true));
                v.Add(new Voltage("Voltage #3", 2, true));
                v.Add(new Voltage("Voltage #4", 3, true));
                v.Add(new Voltage("Voltage #5", 4, true));
                v.Add(new Voltage("Voltage #6", 5, true));
                v.Add(new Voltage("Voltage #7", 6, true));
                v.Add(new Voltage("+3V Standby", 7, 10, 10, 0, true));
                v.Add(new Voltage("CMOS Battery", 8, 10, 10));

                for (int i = 0; i < superIO.Temperatures.Length; i++)
                    t.Add(new Temperature("Temperature #" + (i + 1), i));

                for (int i = 0; i < superIO.Fans.Length; i++)
                    f.Add(new Fan("Fan #" + (i + 1), i));

                for (int i = 0; i < superIO.Controls.Length; i++)
                    c.Add(new Control("Fan #" + (i + 1), i));

                break;
        }
    }

    private static void GetIteConfigurationsC(ISuperIO superIO, Manufacturer manufacturer, Model model, IList<Voltage> v, IList<Temperature> t, IList<Fan> f, IList<Control> c)
    {
        v.Add(new Voltage("Voltage #1", 0, true));
        v.Add(new Voltage("Voltage #2", 1, true));
        v.Add(new Voltage("Voltage #3", 2, true));
        v.Add(new Voltage("Voltage #4", 3, true));
        v.Add(new Voltage("Voltage #5", 4, true));
        v.Add(new Voltage("Voltage #6", 5, true));
        v.Add(new Voltage("Voltage #7", 6, true));
        v.Add(new Voltage("+3V Standby", 7, 10, 10, 0, true));
        v.Add(new Voltage("CMOS Battery", 8, 10, 10));

        for (int i = 0; i < superIO.Temperatures.Length; i++)
            t.Add(new Temperature("Temperature #" + (i + 1), i));

        for (int i = 0; i < superIO.Fans.Length; i++)
            f.Add(new Fan("Fan #" + (i + 1), i));

        for (int i = 0; i < superIO.Controls.Length; i++)
            c.Add(new Control("Fan #" + (i + 1), i));
    }

    private static void GetFintekConfiguration(ISuperIO superIO, Manufacturer manufacturer, Model model, IList<Voltage> v, IList<Temperature> t, IList<Fan> f, IList<Control> c)
    {
        switch (manufacturer)
        {
            case Manufacturer.EVGA:
                v.Add(new Voltage("VCC3V", 0, 150, 150));
                v.Add(new Voltage("Vcore", 1));
                v.Add(new Voltage("Voltage #3", 2, true));
                v.Add(new Voltage("Voltage #4", 3, true));
                v.Add(new Voltage("Voltage #5", 4, true));
                v.Add(new Voltage("Voltage #6", 5, true));
                v.Add(new Voltage("Voltage #7", 6, true));
                v.Add(new Voltage("VSB3V", 7, 150, 150));
                v.Add(new Voltage("CMOS Battery", 8, 150, 150));

                for (int i = 0; i < superIO.Temperatures.Length; i++)
                    t.Add(new Temperature("Temperature #" + (i + 1), i));

                for (int i = 0; i < superIO.Fans.Length; i++)
                    f.Add(new Fan("Fan #" + (i + 1), i));

                break;

            case Manufacturer.MSI:
                v.Add(new Voltage("VCC3V", 0, 150, 150));
                v.Add(new Voltage("Vcore", 1));
                v.Add(new Voltage("Voltage #3", 2, true));
                v.Add(new Voltage("Voltage #4", 3, true));
                v.Add(new Voltage("Voltage #5", 4, true));
                v.Add(new Voltage("Voltage #6", 5, true));

                if (superIO.Chip != Chip.F71808E)
                    v.Add(new Voltage("Voltage #7", 6, true));

                v.Add(new Voltage("VSB3V", 7, 150, 150));
                v.Add(new Voltage("CMOS Battery", 8, 150, 150));

                for (int i = 0; i < superIO.Temperatures.Length; i++)
                    t.Add(new Temperature("Temperature #" + (i + 1), i));

                for (int i = 0; i < superIO.Fans.Length; i++)
                    f.Add(new Fan("Fan #" + (i + 1), i));

                for (int i = 0; i < superIO.Controls.Length; i++)
                    c.Add(new Control("Fan Control #" + (i + 1), i));

                break;

            default:
                v.Add(new Voltage("VCC3V", 0, 150, 150));
                v.Add(new Voltage("Vcore", 1));
                v.Add(new Voltage("Voltage #3", 2, true));
                v.Add(new Voltage("Voltage #4", 3, true));
                v.Add(new Voltage("Voltage #5", 4, true));
                v.Add(new Voltage("Voltage #6", 5, true));
                if (superIO.Chip != Chip.F71808E)
                    v.Add(new Voltage("Voltage #7", 6, true));

                v.Add(new Voltage("VSB3V", 7, 150, 150));
                v.Add(new Voltage("CMOS Battery", 8, 150, 150));

                for (int i = 0; i < superIO.Temperatures.Length; i++)
                    t.Add(new Temperature("Temperature #" + (i + 1), i));

                for (int i = 0; i < superIO.Fans.Length; i++)
                    f.Add(new Fan("Fan #" + (i + 1), i));

                for (int i = 0; i < superIO.Controls.Length; i++)
                    c.Add(new Control("Fan #" + (i + 1), i));

                break;
        }
    }

    private static void GetNuvotonConfigurationF(ISuperIO superIO, Manufacturer manufacturer, Model model, IList<Voltage> v, IList<Temperature> t, IList<Fan> f, IList<Control> c)
    {
        v.Add(new Voltage("Vcore", 0));
        v.Add(new Voltage("Voltage #2", 1, true));
        v.Add(new Voltage("AVCC", 2, 34, 34));
        v.Add(new Voltage("+3.3V", 3, 34, 34));
        v.Add(new Voltage("Voltage #5", 4, true));
        v.Add(new Voltage("Voltage #6", 5, true));
        v.Add(new Voltage("Voltage #7", 6, true));
        v.Add(new Voltage("+3V Standby", 7, 34, 34));
        v.Add(new Voltage("CMOS Battery", 8, 34, 34));
        t.Add(new Temperature("CPU Core", 0));
        t.Add(new Temperature("Temperature #1", 1));
        t.Add(new Temperature("Temperature #2", 2));
        t.Add(new Temperature("Temperature #3", 3));

        for (int i = 0; i < superIO.Fans.Length; i++)
            f.Add(new Fan("Fan #" + (i + 1), i));

        for (int i = 0; i < superIO.Controls.Length; i++)
            c.Add(new Control("Fan #" + (i + 1), i));
    }

    private static void GetNuvotonConfigurationD(ISuperIO superIO, Manufacturer manufacturer, Model model, int index, IList<Voltage> v, IList<Temperature> t, IList<Fan> f, IList<Control> c)
    {
        switch (manufacturer)
        {
            case Manufacturer.ASRock:
                v.Add(new Voltage("Vcore", 0, 10, 10));
                v.Add(new Voltage("Voltage #2", 1, true));
                v.Add(new Voltage("AVCC", 2, 34, 34));
                v.Add(new Voltage("+3.3V", 3, 34, 34));
                v.Add(new Voltage("Voltage #5", 4, true));
                v.Add(new Voltage("Voltage #6", 5, true));
                v.Add(new Voltage("Voltage #7", 6, true));
                v.Add(new Voltage("+3V Standby", 7, 34, 34));
                v.Add(new Voltage("CMOS Battery", 8, 34, 34));
                v.Add(new Voltage("CPU Termination", 9));
                v.Add(new Voltage("Voltage #11", 10, true));
                v.Add(new Voltage("Voltage #12", 11, true));
                v.Add(new Voltage("Voltage #13", 12, true));
                v.Add(new Voltage("Voltage #14", 13, true));
                v.Add(new Voltage("Voltage #15", 14, true));
                t.Add(new Temperature("CPU Core", 0));
                t.Add(new Temperature("Temperature #1", 1));
                t.Add(new Temperature("Temperature #2", 2));
                t.Add(new Temperature("Temperature #3", 3));
                t.Add(new Temperature("Temperature #4", 4));
                t.Add(new Temperature("Temperature #5", 5));
                t.Add(new Temperature("Temperature #6", 6));

                for (int i = 0; i < superIO.Fans.Length; i++)
                    f.Add(new Fan("Fan #" + (i + 1), i));

                for (int i = 0; i < superIO.Controls.Length; i++)
                    c.Add(new Control("Fan #" + (i + 1), i));

                break;

            default:
                v.Add(new Voltage("Vcore", 0));
                v.Add(new Voltage("Voltage #2", 1, true));
                v.Add(new Voltage("AVCC", 2, 34, 34));
                v.Add(new Voltage("+3.3V", 3, 34, 34));
                v.Add(new Voltage("Voltage #5", 4, true));
                v.Add(new Voltage("Voltage #6", 5, true));
                v.Add(new Voltage("Voltage #7", 6, true));
                v.Add(new Voltage("+3V Standby", 7, 34, 34));
                v.Add(new Voltage("CMOS Battery", 8, 34, 34));
                v.Add(new Voltage("CPU Termination", 9));
                v.Add(new Voltage("Voltage #11", 10, true));
                v.Add(new Voltage("Voltage #12", 11, true));
                v.Add(new Voltage("Voltage #13", 12, true));
                v.Add(new Voltage("Voltage #14", 13, true));
                v.Add(new Voltage("Voltage #15", 14, true));
                t.Add(new Temperature("CPU Core", 0));
                t.Add(new Temperature("Temperature #1", 1));
                t.Add(new Temperature("Temperature #2", 2));
                t.Add(new Temperature("Temperature #3", 3));
                t.Add(new Temperature("Temperature #4", 4));
                t.Add(new Temperature("Temperature #5", 5));
                t.Add(new Temperature("Temperature #6", 6));

                for (int i = 0; i < superIO.Fans.Length; i++)
                    f.Add(new Fan("Fan #" + (i + 1), i));

                for (int i = 0; i < superIO.Controls.Length; i++)
                    c.Add(new Control("Fan #" + (i + 1), i));

                break;
        }
    }

    private static void GetWinbondConfigurationEhf(Manufacturer manufacturer, Model model, IList<Voltage> v, IList<Temperature> t, IList<Fan> f, IList<Control> c)
    {
        v.Add(new Voltage("Vcore", 0));
        v.Add(new Voltage("Voltage #2", 1, true));
        v.Add(new Voltage("AVCC", 2, 34, 34));
        v.Add(new Voltage("+3.3V", 3, 34, 34));
        v.Add(new Voltage("Voltage #5", 4, true));
        v.Add(new Voltage("Voltage #6", 5, true));
        v.Add(new Voltage("Voltage #7", 6, true));
        v.Add(new Voltage("+3V Standby", 7, 34, 34));
        v.Add(new Voltage("CMOS Battery", 8, 34, 34));
        v.Add(new Voltage("Voltage #10", 9, true));
        t.Add(new Temperature("CPU", 0));
        t.Add(new Temperature("Auxiliary", 1));
        t.Add(new Temperature("System", 2));
        f.Add(new Fan("System Fan", 0));
        f.Add(new Fan("CPU Fan", 1));
        f.Add(new Fan("Auxiliary Fan", 2));
        f.Add(new Fan("CPU Fan #2", 3));
        f.Add(new Fan("Auxiliary Fan #2", 4));
        c.Add(new Control("System Fan", 0));
        c.Add(new Control("CPU Fan", 1));
        c.Add(new Control("Auxiliary Fan", 2));
    }

    private static void GetWinbondConfigurationHg(Manufacturer manufacturer, Model model, IList<Voltage> v, IList<Temperature> t, IList<Fan> f, IList<Control> c)
    {
        v.Add(new Voltage("Vcore", 0));
        v.Add(new Voltage("Voltage #2", 1, true));
        v.Add(new Voltage("AVCC", 2, 34, 34));
        v.Add(new Voltage("+3.3V", 3, 34, 34));
        v.Add(new Voltage("Voltage #5", 4, true));
        v.Add(new Voltage("Voltage #6", 5, true));
        v.Add(new Voltage("Voltage #7", 6, true));
        v.Add(new Voltage("+3V Standby", 7, 34, 34));
        v.Add(new Voltage("CMOS Battery", 8, 34, 34));
        t.Add(new Temperature("CPU", 0));
        t.Add(new Temperature("Auxiliary", 1));
        t.Add(new Temperature("System", 2));
        f.Add(new Fan("System Fan", 0));
        f.Add(new Fan("CPU Fan", 1));
        f.Add(new Fan("Auxiliary Fan", 2));
        f.Add(new Fan("CPU Fan #2", 3));
        f.Add(new Fan("Auxiliary Fan #2", 4));
        c.Add(new Control("System Fan", 0));
        c.Add(new Control("CPU Fan", 1));
        c.Add(new Control("Auxiliary Fan", 2));
    }

    public override string GetReport()
    {
        StringBuilder sb = new();
        sb.AppendLine(_superIO.GetReport());
        sb.AppendLine();
        sb.AppendLine("Temperature debug (SuperIO read):");

        try
        {
            // If there are named sensors created, iterate them; otherwise use superIO.Temperatures
            if (_temperatures != null && _temperatures.Count > 0)
            {
                foreach (Sensor s in _temperatures)
                {
                    int idx = s.Index;
                    float? v = _readTemperature != null ? _readTemperature(idx) : null;
                    sb.Append("  Index ").Append(idx).Append(" \"").Append(s.Name).Append("\": ");
                    sb.AppendLine(v.HasValue ? v.Value.ToString("F2", CultureInfo.InvariantCulture) : "null");
                }
            }
            else
            {
                // Fallback: dump raw superIO.Temperatures if available
                var temps = (_superIO?.Temperatures);
                if (temps != null)
                {
                    for (int i = 0; i < temps.Length; i++)
                    {
                        sb.Append("  Raw Index ").Append(i).Append(": ");
                        sb.AppendLine(temps[i].HasValue ? temps[i].Value.ToString("F2", CultureInfo.InvariantCulture) : "null");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine("  Exception while reading temperatures: " + ex.Message);
        }

        return sb.ToString();
    }

    public override void Update()
    {
        _superIO.Update();

        foreach (Sensor sensor in _voltages)
        {
            float? value = _readVoltage(sensor.Index);
            if (value.HasValue)
            {
                sensor.Value = value + ((value - sensor.Parameters[2].Value) * sensor.Parameters[0].Value / sensor.Parameters[1].Value);
                ActivateSensor(sensor);
            }
        }

        foreach (Sensor sensor in _temperatures)
        {
            float? value = _readTemperature(sensor.Index);
            if (value.HasValue)
            {
                sensor.Value = value + sensor.Parameters[0].Value;
                ActivateSensor(sensor);
            }
            else
            {
                if (_motherboard.Model == Model.X570_MS7C35) // Add Temp Value for CPU MOS TEMPERATURE & PCH TEMPERATURE
                {
                    float? voltage = null;

                    if (sensor.Index == 24)
                    {
                        voltage = _readVoltage(6);
                    }
                    else if (sensor.Index == 25)
                    {
                        voltage = _readVoltage(11);
                    }

                    if (!voltage.HasValue || voltage.Value <= 0)
                        continue;

                    double R = (10000.0 * voltage.Value / (2.048 - 1.0));            //Convert voltage measured to resistance value
                    double T = ((298.15 * 3435.0) / ((298.15 * Math.Log(R / 10000.0)) + 3435.0));  // Use R value in steinhart and hart equation, calculate temperature value in kelvin
                    float Tc = (float)(T - 273.15);                            // Converting kelvin to celsius

                    sensor.Value = Tc + sensor.Parameters[0].Value;
                    ActivateSensor(sensor);
                }
            }
        }

        foreach (Sensor sensor in _fans)
        {
            float? value = _readFan(sensor.Index);
            if (value.HasValue)
            {
                sensor.Value = value;
                ActivateSensor(sensor);
            }
        }

        foreach (Sensor sensor in _controls)
        {
            sensor.Value = _readControl(sensor.Index);
        }

        _postUpdate();
    }

    public override void Close()
    {
        foreach (Sensor sensor in _controls)
        {
            // restore all controls back to default
            _superIO.SetControl(sensor.Index, null);
        }

        base.Close();
    }

    private delegate float? ReadValueDelegate(int index);

    private delegate void UpdateDelegate();
}
