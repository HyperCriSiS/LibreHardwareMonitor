// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LibreHardwareMonitor.PawnIo;

namespace LibreHardwareMonitor.Hardware.Cpu;

internal sealed class Amd17Cpu : AmdCpu
{
    private readonly Processor _processor;
    private readonly Dictionary<SensorType, int> _sensorTypeIndex;
    private readonly RyzenSMU _smu;
    private readonly AmdFamily17 _pawnModule;

    public Amd17Cpu(int processorIndex, CpuId[][] cpuId, ISettings settings) : base(processorIndex, cpuId, settings)
    {
        _pawnModule = new AmdFamily17();

        _sensorTypeIndex = new Dictionary<SensorType, int>();
        foreach (SensorType type in Enum.GetValues(typeof(SensorType)))
        {
            _sensorTypeIndex.Add(type, 0);
        }

        _sensorTypeIndex[SensorType.Load] = _active.Count(x => x.SensorType == SensorType.Load);

        _smu = new RyzenSMU();

        // Add all numa nodes.
        // Register ..1E_2, [10:8] + 1
        _processor = new Processor(this);

        // Add all numa nodes.
        int coreId = 0;
        int lastCoreId = -1; // Invalid id.

        // Ryzen 3000's skip some core ids.
        // So start at 1 and count upwards when the read core changes.
        foreach (CpuId thread in cpuId.SelectMany(x => x).OrderBy(x => x.ExtData[0x1e, 1] & 0xFF))
        {
            // CPUID_Fn8000001E_EBX, Register ..1E_1, [7:0]
            // threads per core =  CPUID_Fn8000001E_EBX[15:8] + 1
            // CoreId: core ID =  CPUID_Fn8000001E_EBX[7:0]
            int coreIdRead = (int)(thread.ExtData[0x1e, 1] & 0xff);

            // CPUID_Fn8000001E_ECX, Node Identifiers, Register ..1E_2
            // NodesPerProcessor =  CPUID_Fn8000001E_ECX[10:8]
            // nodeID =  CPUID_Fn8000001E_ECX[7:0]
            int nodeId = (int)(thread.ExtData[0x1e, 2] & 0xff);

            if (coreIdRead != lastCoreId)
            {
                coreId++;
            }

            lastCoreId = coreIdRead;

            _processor.AppendThread(thread, nodeId, coreId);
        }

        Update();
    }

    /// <summary>
    /// Adds the DRAM timings of the memory controller on Zen 2 (Matisse) and Zen 3 (Vermeer) desktop CPUs with DDR4.
    /// The values are read from the UMC timing registers. Keys start with "RAM ", which user interfaces may treat as advanced.
    /// </summary>
    public override IDictionary<string, string> Properties
    {
        get
        {
            IDictionary<string, string> properties = base.Properties;

            if (!((_family == 0x17 && _model == 0x71) || (_family == 0x19 && _model == 0x21)))
                return properties;

            try
            {
                AddMemoryTimings(properties);
            }
            catch (Exception)
            {
                // Timings are optional, for example without the PawnIO driver.
            }

            return properties;
        }
    }

    private void AddMemoryTimings(IDictionary<string, string> properties)
    {
        // UMC channel 0 or, if it is not populated, channel 1.
        uint channel = 0;
        if (ReadUmc(channel, 0x204) is 0 or 0xFFFFFFFF)
            channel = 0x100000;

        uint config = ReadUmc(channel, 0x200);
        uint timing1 = ReadUmc(channel, 0x204);
        uint timing2 = ReadUmc(channel, 0x208);
        uint timing3 = ReadUmc(channel, 0x20C);
        uint timing4 = ReadUmc(channel, 0x210);
        uint timing5 = ReadUmc(channel, 0x214);
        uint timing6 = ReadUmc(channel, 0x218);

        uint cl = timing1 & 0x3F;
        uint ras = (timing1 >> 8) & 0x7F;
        uint rcdRead = (timing1 >> 16) & 0x3F;
        uint rcdWrite = (timing1 >> 24) & 0x3F;
        uint rc = timing2 & 0xFF;
        uint rp = (timing2 >> 16) & 0x3F;

        // Reject values that cannot be DDR4 timings, so a different register layout shows nothing instead of nonsense.
        if (cl is < 8 or > 40 || rp is < 8 or > 40 || ras < 16 || rc < ras)
            return;

        double speed = (config & 0x7F) / 3.0 * 200;
        if (speed is >= 1600 and <= 6000)
            properties["RAM speed"] = string.Format(CultureInfo.InvariantCulture, "DDR4-{0:0}", speed);

        properties["RAM tCL"] = cl.ToString(CultureInfo.InvariantCulture);
        properties["RAM tRCD read"] = rcdRead.ToString(CultureInfo.InvariantCulture);
        properties["RAM tRCD write"] = rcdWrite.ToString(CultureInfo.InvariantCulture);
        properties["RAM tRP"] = rp.ToString(CultureInfo.InvariantCulture);
        properties["RAM tRAS"] = ras.ToString(CultureInfo.InvariantCulture);
        properties["RAM tRC"] = rc.ToString(CultureInfo.InvariantCulture);
        properties["RAM tRRD S"] = (timing3 & 0x1F).ToString(CultureInfo.InvariantCulture);
        properties["RAM tRRD L"] = ((timing3 >> 8) & 0x1F).ToString(CultureInfo.InvariantCulture);
        properties["RAM tRTP"] = ((timing3 >> 24) & 0x1F).ToString(CultureInfo.InvariantCulture);
        properties["RAM tFAW"] = (timing4 & 0x7F).ToString(CultureInfo.InvariantCulture);
        properties["RAM tCWL"] = (timing5 & 0x3F).ToString(CultureInfo.InvariantCulture);
        properties["RAM tWTR S"] = ((timing5 >> 8) & 0x1F).ToString(CultureInfo.InvariantCulture);
        properties["RAM tWTR L"] = ((timing5 >> 16) & 0x7F).ToString(CultureInfo.InvariantCulture);
        properties["RAM tWR"] = (timing6 & 0x7F).ToString(CultureInfo.InvariantCulture);
        properties["RAM command rate"] = ((config >> 10) & 1) == 1 ? "2T" : "1T";
        properties["RAM gear down"] = ((config >> 11) & 1) == 1 ? "On" : "Off";

        // tRFC has one register per refresh mode. The one that differs from the reset value is in use.
        foreach (uint offset in new uint[] { 0x260, 0x264, 0x268, 0x26C })
        {
            uint rfcRegister = ReadUmc(channel, offset);
            if (rfcRegister != 0 && rfcRegister != 0x21060138)
            {
                uint rfc = rfcRegister & 0x7FF;
                if (rfc is > 50 and < 2000)
                    properties["RAM tRFC"] = rfc.ToString(CultureInfo.InvariantCulture);

                break;
            }
        }

        uint ReadUmc(uint channelOffset, uint register) => _pawnModule.ReadSmn(0x50000 + channelOffset + register);
    }

    public override string GetReport()
    {
        StringBuilder r = new();
        r.Append(base.GetReport());
        r.Append(_smu.GetReport());
        return r.ToString();
    }

    /// <inheritdoc />
    public override void Close()
    {
        base.Close();
        _pawnModule.Close();
        _smu.Close();
    }

    public override void Update()
    {
        base.Update();

        _processor.UpdateSensors();

        foreach (NumaNode node in _processor.Nodes)
        {
            NumaNode.UpdateSensors();

            foreach (Core c in node.Cores)
            {
                c.UpdateSensors();
            }
        }

        _processor.UpdateVirtualSensor();
    }

    private class Processor
    {
        private readonly Sensor _busClock;
        private readonly Sensor _avgClock;
        private readonly Sensor _avgClockEffective;

        private readonly Sensor[] _ccdTemperatures;
        private readonly Sensor _coreTemperatureTctl;
        private readonly Sensor _coreTemperatureTctlTdie;
        private readonly Sensor _coreTemperatureTdie;
        private readonly Sensor _coreVoltage;
        private readonly Amd17Cpu _cpu;
        private readonly Sensor _packagePower;
        private readonly Dictionary<KeyValuePair<uint, RyzenSMU.SmuSensorType>, Sensor> _smuSensors = new();
        private readonly Sensor _socVoltage;

        private Sensor _ccdsAverageTemperature;
        private Sensor _ccdsMaxTemperature;
        private Sensor _coreVids;
        private float[] _pmTable = [];
        private DateTime _lastSampleTime = new(0);
        private uint _lastPwrValue;

        public Processor(Hardware hardware)
        {
            _cpu = (Amd17Cpu)hardware;

            _packagePower = new Sensor("Package", _cpu._sensorTypeIndex[SensorType.Power]++, SensorType.Power, _cpu, _cpu._settings);
            _coreTemperatureTctl = new Sensor("Core (Tctl)", _cpu._sensorTypeIndex[SensorType.Temperature]++, SensorType.Temperature, _cpu, _cpu._settings);
            _coreTemperatureTdie = new Sensor("Core (Tdie)", _cpu._sensorTypeIndex[SensorType.Temperature]++, SensorType.Temperature, _cpu, _cpu._settings);
            _coreTemperatureTctlTdie = new Sensor("Core (Tctl/Tdie)", _cpu._sensorTypeIndex[SensorType.Temperature]++, SensorType.Temperature, _cpu, _cpu._settings);
            _ccdTemperatures = new Sensor[16]; // Turin / Shimada Peak expose up to 16 CCDs (k10temp).
            _coreVoltage = new Sensor("Core (SVI2 TFN)", _cpu._sensorTypeIndex[SensorType.Voltage]++, SensorType.Voltage, _cpu, _cpu._settings);
            _socVoltage = new Sensor("SoC (SVI2 TFN)", _cpu._sensorTypeIndex[SensorType.Voltage]++, SensorType.Voltage, _cpu, _cpu._settings);
            _busClock = new Sensor("Bus Speed", _cpu._sensorTypeIndex[SensorType.Clock]++, SensorType.Clock, _cpu, _cpu._settings);
            _avgClock = new Sensor("Cores (Average)", _cpu._sensorTypeIndex[SensorType.Clock]++, SensorType.Clock, _cpu, _cpu._settings);
            _avgClockEffective = new Sensor("Cores (Average Effective)", _cpu._sensorTypeIndex[SensorType.Clock]++, SensorType.Clock, _cpu, _cpu._settings);

            _cpu.ActivateSensor(_packagePower);
            _cpu.ActivateSensor(_avgClock);
            _cpu.ActivateSensor(_avgClockEffective);

            _coreVids = new Sensor("Core VIDs", _cpu._sensorTypeIndex[SensorType.Voltage]++, SensorType.Voltage, _cpu, _cpu._settings);

            foreach (KeyValuePair<uint, RyzenSMU.SmuSensorType> sensor in _cpu._smu.GetPmTableStructure())
            {
                _smuSensors.Add(sensor, new Sensor(sensor.Value.Name, _cpu._sensorTypeIndex[sensor.Value.Type]++, sensor.Value.Type, _cpu, _cpu._settings));
            }
        }

        public List<NumaNode> Nodes { get; } = new();

        public float[] PmTable => _pmTable;

        public void UpdateSensors()
        {
            NumaNode node = Nodes[0];
            Core core = node?.Cores[0];
            CpuId cpuId = core?.Threads.FirstOrDefault()?.Cpu;

            if (cpuId == null)
                return;

            GroupAffinity previousAffinity = ThreadAffinity.Set(cpuId.Affinity);

            // MSRC001_0299
            // TU [19:16]
            // ESU [12:8] -> Unit 15.3 micro Joule per increment (default), 1/2^ESU micro Joule
            // PU [3:0]
            _cpu._pawnModule.ReadMsr(MSR_PWR_UNIT, out uint eax, out uint _);
            int esu = (int)((eax >> 8) & 0x1F);
            double energyBaseUnit = Math.Pow(0.5, esu);


            // MSRC001_029B
            // total_energy [31:0]
            DateTime sampleTime = DateTime.UtcNow;
            _cpu._pawnModule.ReadMsr(MSR_PKG_ENERGY_STAT, out eax, out _);

            uint totalEnergy = eax;

            uint smuSvi0Tfn = 0;
            uint smuSvi0TelPlane0 = 0;
            uint smuSvi0TelPlane1 = 0;

            if (Mutexes.WaitPciBus(10))
            {
                // THM_TCON_CUR_TMP
                // CUR_TEMP [31:21]
                uint temperature = _cpu._pawnModule.ReadSmn(F17H_M01H_THM_TCON_CUR_TMP);

                // SVI0_TFN_PLANE0 [0]
                // SVI0_TFN_PLANE1 [1]
                smuSvi0Tfn = _cpu._pawnModule.ReadSmn(F17H_M01H_SVI + 0x8);

                bool supportsPerCcdTemperatures = false;

                // TODO: find a better way because these will probably keep changing in the future.

                uint sviPlane0Offset;
                uint sviPlane1Offset;
                switch (cpuId.Model)
                {
                    case 0x31: // Threadripper 3000.
                        sviPlane0Offset = F17H_M01H_SVI + 0x14;
                        sviPlane1Offset = F17H_M01H_SVI + 0x10;
                        supportsPerCcdTemperatures = true;
                        break;

                    case 0x71: // Zen 2.
                    case 0x21: // Zen 3.
                        sviPlane0Offset = F17H_M01H_SVI + 0x10;
                        sviPlane1Offset = F17H_M01H_SVI + 0xC;
                        supportsPerCcdTemperatures = true;
                        break;

                    case 0x61: //Zen 4
                    case 0x44: //Zen 5
                        sviPlane0Offset = F17H_M01H_SVI + 0x10;
                        sviPlane1Offset = F17H_M01H_SVI + 0xC;
                        supportsPerCcdTemperatures = true;
                        break;

                    default: // Zen and Zen+.
                        sviPlane0Offset = F17H_M01H_SVI + 0xC;
                        sviPlane1Offset = F17H_M01H_SVI + 0x10;
                        break;
                }

                // Zen 5 HEDT/server: family 0x1A models 0x00-0x2F (Turin / Shimada Peak).
                // Threadripper 9970X is model 0x08. Desktop Granite Ridge is 0x40-0x4F (0x44 above).
                bool isZen5Turin = cpuId.Family == 0x1A && cpuId.Model <= 0x2F;
                if (isZen5Turin)
                    supportsPerCcdTemperatures = true;

                // SVI0_PLANE0_VDDCOR [24:16]
                // SVI0_PLANE0_IDDCOR [7:0]
                smuSvi0TelPlane0 = _cpu._pawnModule.ReadSmn(sviPlane0Offset);

                // SVI0_PLANE1_VDDCOR [24:16]
                // SVI0_PLANE1_IDDCOR [7:0]
                smuSvi0TelPlane1 = _cpu._pawnModule.ReadSmn(sviPlane1Offset);

                ThreadAffinity.Set(previousAffinity);

                TimeSpan deltaTime = sampleTime - _lastSampleTime;
                if (_lastSampleTime.Ticks == 0)
                {
                    deltaTime = new(0);
                    _lastSampleTime = sampleTime;
                    _lastPwrValue = totalEnergy;
                }

                _lastSampleTime = sampleTime;

                // ticks diff
                // power consumption
                // power.Value = (float) ((double)pu * 0.125);
                // energyBaseUnit = micro Joule per increment, from [ESU]

                long pwr;
                if (_lastPwrValue <= totalEnergy)
                    pwr = totalEnergy - _lastPwrValue;
                else
                    pwr = (0xffffffff - _lastPwrValue) + totalEnergy;

                // update for next sample
                _lastPwrValue = totalEnergy;

                if (deltaTime.Ticks > 0)
                {
                    double energy = energyBaseUnit * pwr;
                    energy /= deltaTime.TotalSeconds;

                    if (!double.IsNaN(energy))
                        _packagePower.Value = (float)energy;
                }

                // current temp Bit [31:21]
                // Newer Zen parts can signal the 49 C adjustment through TJ_SEL[17:16] as well as RANGE_SEL[19].
                bool tempOffsetFlag = (temperature & F17H_TEMP_RANGE_SEL_MASK) != 0
                                      || (temperature & F17H_TEMP_TJ_SEL_MASK) == F17H_TEMP_TJ_SEL_MASK;
                temperature = (temperature >> 21) * 125;

                float offset = 0.0f;

                // Offset table: https://github.com/torvalds/linux/blob/master/drivers/hwmon/k10temp.c#L78
                if (string.IsNullOrWhiteSpace(cpuId.Name))
                    offset = 0;
                else if (cpuId.Name.Contains("1600X") || cpuId.Name.Contains("1700X") || cpuId.Name.Contains("1800X"))
                    offset = -20.0f;
                else if (cpuId.Name.Contains("Threadripper 19") || cpuId.Name.Contains("Threadripper 29"))
                    offset = -27.0f;
                else if (cpuId.Name.Contains("2700X"))
                    offset = -10.0f;

                float t = temperature * 0.001f;
                if (tempOffsetFlag)
                    t += -49.0f;

                if (offset < 0)
                {
                    _coreTemperatureTctl.Value = t;
                    _coreTemperatureTdie.Value = t + offset;

                    _cpu.ActivateSensor(_coreTemperatureTctl);
                    _cpu.ActivateSensor(_coreTemperatureTdie);
                }
                else
                {
                    // Zen 2 doesn't have an offset so Tdie and Tctl are the same.
                    _coreTemperatureTctlTdie.Value = t;
                    _cpu.ActivateSensor(_coreTemperatureTctlTdie);
                }

                // Tested only on R5 3600 & Threadripper 3960X, 5900X, 7900X.
                // Zen 5 Turin CCD map: linux/drivers/hwmon/k10temp.c (offset 0x1F0 from 0x59800).
                if (supportsPerCcdTemperatures)
                {
                    uint ccdBase;
                    uint ccdCount;
                    if (isZen5Turin)
                    {
                        ccdBase = F1AH_M00H_CCD1_TEMP;
                        ccdCount = 16;
                    }
                    else if (cpuId.Model is 0x61 or 0x44) // Raphael or GraniteRidge
                    {
                        ccdBase = F17H_M61H_CCD1_TEMP;
                        ccdCount = 8;
                    }
                    else
                    {
                        ccdBase = F17H_M70H_CCD1_TEMP;
                        ccdCount = 8;
                    }

                    for (uint i = 0; i < ccdCount; i++)
                    {
                        uint ccdRawTemp = _cpu._pawnModule.ReadSmn(ccdBase + i * 0x4);
                        bool ccdPresent = isZen5Turin
                            ? (ccdRawTemp & F17H_CCD_TEMP_VALID) != 0
                            : (ccdRawTemp & 0xFFF) > 0;
                        ccdRawTemp &= 0xFFF;
                        float ccdTemp = ((ccdRawTemp * 125) - 305000) * 0.001f;
                        if (ccdPresent && ccdTemp < 125) // Zen 2 reports 95 degrees C max, but it might exceed that.
                        {
                            if (_ccdTemperatures[i] == null)
                            {
                                _cpu.ActivateSensor(_ccdTemperatures[i] = new Sensor($"CCD{i + 1} (Tdie)",
                                                                                     _cpu._sensorTypeIndex[SensorType.Temperature]++,
                                                                                     SensorType.Temperature,
                                                                                     _cpu,
                                                                                     _cpu._settings));
                            }

                            _ccdTemperatures[i].Value = ccdTemp;
                        }
                    }

                    Sensor[] activeCcds = _ccdTemperatures.Where(x => x != null).ToArray();
                    if (activeCcds.Length > 1)
                    {
                        // No need to get the max / average ccds temp if there is only one CCD.

                        if (_ccdsMaxTemperature == null)
                        {
                            _cpu.ActivateSensor(_ccdsMaxTemperature = new Sensor("CCDs Max (Tdie)",
                                                                                 _cpu._sensorTypeIndex[SensorType.Temperature]++,
                                                                                 SensorType.Temperature,
                                                                                 _cpu,
                                                                                 _cpu._settings));
                        }

                        if (_ccdsAverageTemperature == null)
                        {
                            _cpu.ActivateSensor(_ccdsAverageTemperature = new Sensor("CCDs Average (Tdie)",
                                                                                     _cpu._sensorTypeIndex[SensorType.Temperature]++,
                                                                                     SensorType.Temperature,
                                                                                     _cpu,
                                                                                     _cpu._settings));
                        }

                        _ccdsMaxTemperature.Value = activeCcds.Max(x => x.Value);
                        _ccdsAverageTemperature.Value = activeCcds.Average(x => x.Value);
                    }
                }

                Mutexes.ReleasePciBus();
            }

            // voltage
            const double vidStep = 0.00625;
            double vcc;
            uint svi0PlaneXVddCor;

            if (cpuId.Model is 0x61 or 0x44) // Readout not working for Ryzen 7000/9000.
                smuSvi0Tfn |= 0x01 | 0x02;

            // Core (0x01).
            if ((smuSvi0Tfn & 0x01) == 0)
            {
                svi0PlaneXVddCor = (smuSvi0TelPlane0 >> 16) & 0xff;
                vcc = 1.550 - (vidStep * svi0PlaneXVddCor);
                _coreVoltage.Value = (float)vcc;

                _cpu.ActivateSensor(_coreVoltage);
            }

            // SoC (0x02), not every Zen cpu has this voltage.
            if (cpuId.Model is 0x11 or 0x21 or 0x71 or 0x31 || (smuSvi0Tfn & 0x02) == 0)
            {
                svi0PlaneXVddCor = (smuSvi0TelPlane1 >> 16) & 0xff;
                vcc = 1.550 - (vidStep * svi0PlaneXVddCor);
                _socVoltage.Value = (float)vcc;

                _cpu.ActivateSensor(_socVoltage);
            }

            double timeStampCounterMultiplier = GetTimeStampCounterMultiplier();
            if (timeStampCounterMultiplier > 0)
            {
                _busClock.Value = (float)(_cpu.TimeStampCounterFrequency / timeStampCounterMultiplier);
                _cpu.ActivateSensor(_busClock);
            }

            _pmTable = _cpu._smu.IsPmTableLayoutDefined() ? _cpu._smu.GetPmTable() : [];

            foreach (KeyValuePair<KeyValuePair<uint, RyzenSMU.SmuSensorType>, Sensor> sensor in _smuSensors)
            {
                if (_pmTable.Length > sensor.Key.Key)
                {
                    sensor.Value.Value = _pmTable[sensor.Key.Key] * sensor.Key.Value.Scale;
                    if (sensor.Value.Value != 0)
                        _cpu.ActivateSensor(sensor.Value);
                }
            }
        }

        public void UpdateVirtualSensor()
        {
            if (Nodes == null || Nodes.Count == 0)
                return;

            // Core clocks need two counter samples. Do not record the initial
            // placeholder zeros, or a partial average, in Min/Max and history.
            if (Nodes.All(node => node.Cores.Count > 0 && node.Cores.All(core => core.HasCoreClock)))
                _avgClock.Value = (float)Math.Round(Nodes.Average(x => x.CoreClock), 0);
            else
                _avgClock.Value = null;

            double clock = Nodes.Average(x => x.EffectiveClock);
            _avgClockEffective.Value = (float)Math.Round(clock, 0);

            double voltage = Nodes.Average(x => x.CoreVoltage);
            if (voltage > 0)
            {
                _coreVids.Value = (float)Math.Round(voltage, 4);
                _cpu.ActivateSensor(_coreVids);
            }
        }

        private double GetTimeStampCounterMultiplier()
        {
            _cpu._pawnModule.ReadMsr(MSR_PSTATE_0, out uint eax, out _);

            if (_cpu._family == 0x1a)
            {
                //zen 5
                uint cpuFid = eax & 0xfff;
                return (cpuFid * 5) / 100.0;
            }
            else
            {
                uint cpuDfsId = (eax >> 8) & 0x3f;
                uint cpuFid = eax & 0xff;
                return 2.0 * cpuFid / cpuDfsId;
            }
        }

        public sealed class PerCoreBaseIndices
        {
            public int Frequency { get; set; }
            public int Voltage { get; set; }
            public int Temperature { get; set; }
            public int Power { get; set; }
        }

        public static PerCoreBaseIndices GetPerCoreBaseIndices(uint pmTableVersion)
        {
            return pmTableVersion switch
            {
                0x00621202 => new PerCoreBaseIndices { Frequency = 349, Voltage = 317, Temperature = 333, Power = 301 },
                _ => null,
            };
        }

        public void AppendThread(CpuId thread, int numaId, int coreId)
        {
            NumaNode node = null;
            foreach (NumaNode n in Nodes)
            {
                if (n.NodeId == numaId)
                {
                    node = n;
                    break;
                }
            }

            if (node == null)
            {
                node = new NumaNode(_cpu, numaId);
                Nodes.Add(node);
            }

            if (thread != null)
                node.AppendThread(thread, coreId);
        }
    }

    private class NumaNode
    {
        private readonly Amd17Cpu _cpu;

        public NumaNode(Amd17Cpu cpu, int id)
        {
            Cores = new List<Core>();
            NodeId = id;
            _cpu = cpu;
        }

        public List<Core> Cores { get; }

        public int NodeId { get; }

        public double CoreClock
        {
            get
            {
                if (Cores == null)
                    return 0;

                return Cores.Average(x => x.CoreClock);
            }
        }

        public double EffectiveClock
        {
            get
            {
                if (Cores == null)
                    return 0;

                return Cores.Average(x => x.EffectiveClock);
            }
        }

        public double CoreVoltage
        {
            get
            {
                if (Cores == null)
                    return 0;

                return Cores.Average(x => x.CoreVoltage);
            }
        }

        public void AppendThread(CpuId thread, int coreId)
        {
            Core core = null;
            foreach (Core c in Cores)
            {
                if (c.CoreId == coreId)
                    core = c;
            }

            if (core == null)
            {
                core = new Core(_cpu, coreId);
                Cores.Add(core);
            }

            if (thread != null)
                core.AppendThread(thread);
        }

        public static void UpdateSensors()
        { }
    }

    private class CpuThread
    {
        private DateTime _sampleTime = new(0);
        private DateTime _lastSampleTime = new(0);
        private ulong _mperf = 0;
        private ulong _aperf = 0;
        private ulong _mperfLast = 0;
        private ulong _aperfLast = 0;
        private ulong _mperfDelta = 0;
        private ulong _aperfDelta = 0;

        private CpuId _cpuId;
        private Amd17Cpu _cpu;
        public CpuId Cpu { get { return _cpuId; } }

        public TimeSpan SampleDuration { get; private set; } = TimeSpan.Zero;
        public double EffectiveClock { get; private set; } = 0;

        public ulong MperfDelta { get { return _mperfDelta; } }
        public ulong AperfDelta { get { return _aperfDelta; } }

        public CpuThread(Amd17Cpu cpu, CpuId cpuId)
        {
            _cpu = cpu;
            _cpuId = cpuId;
        }

        public void ReadPerformanceCounter()
        {
            ThreadAffinity.Set(Cpu.Affinity);

            _sampleTime = DateTime.UtcNow;

            // performance counter
            // MSRC000_00E7, P0 state counter
            _cpu._pawnModule.ReadMsr(MSR_MPERF_RO, out ulong edxeax);
            _mperf = edxeax;
            // MSRC000_00E8, C0 state counter
            _cpu._pawnModule.ReadMsr(MSR_APERF_RO, out edxeax);
            _aperf = edxeax;
        }

        public void UpdateMeasurements()
        {
            if (_mperf < _mperfLast || _aperf < _aperfLast)
            {
                // current measurment is invalid when _mperf or _aperf overflow
                _lastSampleTime = new(0);
            }

            if (_lastSampleTime.Ticks == 0)
            {
                _lastSampleTime = _sampleTime;
                _mperfLast = _mperf;
                _aperfLast = _aperf;

                _mperfDelta = 0;
                _aperfDelta = 0;
                return;
            }

            SampleDuration = _sampleTime - _lastSampleTime;
            _lastSampleTime = _sampleTime;

            _mperfDelta = _mperf - _mperfLast;
            _aperfDelta = _aperf - _aperfLast;
            _mperfLast = _mperf;
            _aperfLast = _aperf;

            if (_mperfDelta > 20000e6)
                _mperfDelta = 0;
            if (_aperfDelta > 20000e6)
                _aperfDelta = 0;

            if (_aperfDelta == 0 || _mperfDelta == 0)
            {
                //overflow possible, numbers are > 20 GHz
                _lastSampleTime = new(0);
                return;
            }

            //effective clock
            double freq = (double)_aperfDelta / (SampleDuration.TotalMilliseconds * 1000.0);
            EffectiveClock = Math.Round(freq);
        }

        public bool HasValidCounters()
        {
            return _mperfDelta > 0 && _aperfDelta > 0 && SampleDuration.Ticks > 0;
        }
    }

    private class Core
    {
        private readonly Sensor _clock;
        private readonly Sensor _clockEffective;
        private readonly Amd17Cpu _cpu;
        private readonly Sensor _multiplier;
        private readonly Sensor _power;
        private readonly Sensor _temperature;
        private readonly Sensor _vcore;
        private ISensor _busSpeed;
        private DateTime _lastSampleTime = new(0);
        private uint _lastPwrValue = 0;
        private float _pmTableVoltage;

        public double CoreClock { get; set; } = 0;
        public bool HasCoreClock => _clock.Value.HasValue;
        public double EffectiveClock { get; set; } = 0;
        public double CoreVoltage => _pmTableVoltage is > 0.1f and < 3.0f ? _pmTableVoltage : 0;

        public Core(Amd17Cpu cpu, int id)
        {
            _cpu = cpu;
            CoreId = id;
            _clock = new Sensor("Core #" + CoreId, _cpu._sensorTypeIndex[SensorType.Clock]++, SensorType.Clock, cpu, cpu._settings);
            _clockEffective = new Sensor("Core #" + CoreId + " (Effective)", _cpu._sensorTypeIndex[SensorType.Clock]++, SensorType.Clock, cpu, cpu._settings);
            _multiplier = new Sensor("Core #" + CoreId, cpu._sensorTypeIndex[SensorType.Factor]++, SensorType.Factor, cpu, cpu._settings);
            _power = new Sensor("Core #" + CoreId + " (SMU)", cpu._sensorTypeIndex[SensorType.Power]++, SensorType.Power, cpu, cpu._settings);
            _vcore = new Sensor("Core #" + CoreId + " VID", cpu._sensorTypeIndex[SensorType.Voltage]++, SensorType.Voltage, cpu, cpu._settings);
            _temperature = new Sensor("Core #" + CoreId, cpu._sensorTypeIndex[SensorType.Temperature]++, SensorType.Temperature, cpu, cpu._settings);

            cpu.ActivateSensor(_clock);
            cpu.ActivateSensor(_clockEffective);
            cpu.ActivateSensor(_multiplier);
            cpu.ActivateSensor(_power);
            cpu.ActivateSensor(_vcore);
        }

        public int CoreId { get; }

        public List<CpuThread> Threads { get; } = new List<CpuThread>();

        public void AppendThread(CpuId cpuId)
        {
            CpuThread t = new CpuThread(_cpu, cpuId);
            Threads.Add(t);
        }

        /// <summary>
        /// Reads the P0 frequency in MHz from the P-state definition, or 0 if it cannot be read.
        /// </summary>
        private double GetP0Clock(uint family)
        {
            if (!_cpu._pawnModule.ReadMsr(MSR_PSTATE_0, out uint eax, out _) || eax == 0)
                return 0;

            if (family == 0x1A)
                return (eax & 0xfff) * 5.0;

            uint cpuDfsId = (eax >> 8) & 0x3f;
            uint cpuFid = eax & 0xff;
            return cpuDfsId > 0 ? cpuFid / (double)cpuDfsId * 200.0 : 0;
        }

        public void UpdateSensors()
        {
            if (Threads.Count == 0)
                return;

            CpuThread thread = Threads[0];
            GroupAffinity previousAffinity = ThreadAffinity.Set(thread.Cpu.Affinity);

            // MSRC001_0299
            // TU [19:16]
            // ESU [12:8] -> Unit 15.3 micro Joule per increment (default), 1/2^ESU micro Joule
            // PU [3:0]
            _cpu._pawnModule.ReadMsr(MSR_PWR_UNIT, out uint eax, out uint _);
            int esu = (int)((eax >> 8) & 0x1F);
            double energyBaseUnit = Math.Pow(0.5, esu);

            // MSRC001_029A
            // total_energy [31:0]
            DateTime sampleTime = DateTime.UtcNow;
            _cpu._pawnModule.ReadMsr(MSR_CORE_ENERGY_STAT, out eax, out _);
            uint totalEnergy = eax;

            // MSRC001_0293
            // CurHwPstate [24:22]
            // CurCpuVid [21:14]
            // CurCpuDfsId [13:8]
            // CurCpuFid [7:0] zen1..4
            // CurCpuFid [11:0] zen5
            _cpu._pawnModule.ReadMsr(MSR_HARDWARE_PSTATE_STATUS, out eax, out _);
            uint msrPstate = eax;
            int curCpuVid = (int)((eax >> 14) & 0xff);

            foreach (var t in Threads)
            {
                t.ReadPerformanceCounter();
            }

            // MSRC001_0063[P - state Status](PStateStat)
            // Ring0.ReadMsr(MSR_PSTATE_STATUS, out eax, out _);
            // int curPstateStaus = (int)(eax & 0x7);

            // MSRC001_0064 + x
            // PstateEn[63], 1 == enabled
            // IddDiv [31:30]
            // IddValue [29:22]
            // CpuVid [21:14]
            // CpuDfsId [13:8]
            // CpuFid [7:0] zen1..4
            // CpuFid [11:0] zen5
            // Ring0.ReadMsr(MSR_PSTATE_0 + curPstateStaus, out eax, out uint edx);
            // uint curPstate = eax;
            // int PstateEn = (int)(edx >> 31);

            ThreadAffinity.Set(previousAffinity);

            // Update clock counter and cffective clock calculation
            Threads.ForEach(t => t.UpdateMeasurements());
            EffectiveClock = Threads.Average(x => x.EffectiveClock);
            _clockEffective.Value = (float)EffectiveClock;

            if (thread.HasValidCounters())
            {
                double coreClock = 0;
                double busClock = 100.0; //bus speed in MHz
                _busSpeed ??= _cpu.Sensors.FirstOrDefault(x => x.Name == "Bus Speed");
                if (_busSpeed?.Value.HasValue == true && _busSpeed.Value > 0)
                    busClock = (double)_busSpeed.Value;

                if (thread.Cpu.Family == 0x1A)
                {
                    // zen5 (0x1A)
                    // 57896-B0-PUB_3.00.pdf, CoreCOF
                    // CoreCOF is Core current operating frequency in MHz.CoreCOF = Core::X86::Msr::PStateDef[CpuFid[11:0]] * 5MHz
                    // CpuFid[11:0]: core frequency ID.Read - write.Reset: XXXh.Specifies the core frequency multiplier.The core
                    // COF is a function of CpuFid and CpuDid, and defined by CoreCOF.
                    int curCpuFid = (int)(msrPstate & 0xfff);
                    coreClock = curCpuFid * 5;

                    // multiplier, clock speed with 100Mhz as Multiplier Reference
                    _multiplier.Value = curCpuFid > 0 ? (float)((curCpuFid * 5) / busClock) : null;
                }
                else
                {
                    // clock zen 0x17 and 0x19
                    // 55570-B1-3.16_PUB_NRV.pdf, CoreCOF
                    // CoreCOF is (Core::X86::Msr::PStateDef[CpuFid[7:0]] / Core::X86::Msr::PStateDef[CpuDfsId]) * 200
                    // CpuFid[7:0]: core frequency ID.Read - write.Reset: XXh.Specifies the core frequency multiplier.The core
                    // COF is a function of CpuFid and CpuDid, and defined by CoreCOF.
                    int curCpuDfsId = (int)((msrPstate >> 8) & 0x3f);
                    int curCpuFid = (int)(msrPstate & 0xff);

                    // A divisor of 0 means the MSR could not be read, for example without administrator rights.
                    coreClock = curCpuDfsId > 0 ? curCpuFid / (double)curCpuDfsId * (busClock * 2) : 0;

                    // multiplier
                    _multiplier.Value = curCpuDfsId > 0 ? (float)(curCpuFid / (double)curCpuDfsId * 2.0) : null;
                }

                if (coreClock > 0)
                {
                    //clock values valid when AperfDelta < MperfDelta (ratio is < 1.0)
                    if (thread.AperfDelta < thread.MperfDelta)
                        coreClock = ((double)thread.AperfDelta / (double)thread.MperfDelta) * coreClock;
                }
                else
                {
                    // The current P-state reads 0 on some systems. MPERF counts at the P0 frequency, so P0 scaled by
                    // APERF / MPERF gives the clock while the core is active.
                    double p0Clock = GetP0Clock(thread.Cpu.Family);
                    if (p0Clock > 0)
                    {
                        coreClock = p0Clock * thread.AperfDelta / thread.MperfDelta;
                        _multiplier.Value = (float)(coreClock / busClock);
                    }
                }

                CoreClock = Math.Round(coreClock);
                _clock.Value = CoreClock > 0 ? (float)CoreClock : null;
            }

            // Vcore voltage, unknown if the P-state status could not be read.
            // Zen 4 and Zen 5 encode the VID for SVI3: 0.245 V plus 5 mV per step. Older Zen use SVI2: 1.55 V minus 6.25 mV per step.
            bool isSvi3 = _cpu._family == 0x1A || _cpu._family == 0x19 && _cpu._model >= 0x60;
            double vcc = isSvi3 ? 0.245 + (0.005 * curCpuVid) : 1.550 - (0.00625 * curCpuVid);
            _vcore.Value = msrPstate != 0 ? (float)vcc : null;

            // core power consumption
            //current delta time
            TimeSpan deltaTime = sampleTime - _lastSampleTime;
            if (_lastSampleTime.Ticks == 0)
            {
                deltaTime = new(0);
                _lastSampleTime = sampleTime;
                _lastPwrValue = totalEnergy;
            }
            _lastSampleTime = sampleTime;

            if (deltaTime.Ticks > 0)
            {
                // power.Value = (float) ((double)pu * 0.125);
                // energyBaseUnit = micro Joule per increment, from [ESU]
                // ticks diff
                long pwr;
                if (_lastPwrValue <= totalEnergy)
                    pwr = totalEnergy - _lastPwrValue;
                else
                    pwr = (0xffffffff - _lastPwrValue) + totalEnergy;

                // update for next sample
                _lastPwrValue = totalEnergy;

                double energy = energyBaseUnit * pwr;
                energy /= deltaTime.TotalSeconds;

                if (!double.IsNaN(energy))
                    _power.Value = (float)energy;
            }

            UpdatePmTableSensors();

            if (_pmTableVoltage is > 0.1f and < 3.0f)
            {
                _vcore.Value = _pmTableVoltage;
            }
        }

        private void UpdatePmTableSensors()
        {
            Processor.PerCoreBaseIndices indices = Processor.GetPerCoreBaseIndices(_cpu._smu.PmTableVersion);
            if (indices is null)
            {
                return;
            }

            float[] pmTable = _cpu._processor.PmTable;
            if (pmTable.Length == 0)
            {
                return;
            }

            int coreIndex = CoreId - 1;

            int voltageIndex = indices.Voltage + coreIndex;
            if ((uint)voltageIndex < (uint)pmTable.Length)
            {
                _pmTableVoltage = pmTable[voltageIndex];
            }

            int temperatureIndex = indices.Temperature + coreIndex;
            if ((uint)temperatureIndex < (uint)pmTable.Length)
            {
                float temperature = pmTable[temperatureIndex];
                if (temperature is > 0 and < 125)
                {
                    _temperature.Value = temperature;
                    _cpu.ActivateSensor(_temperature);
                }
            }
        }
    }

    // ReSharper disable InconsistentNaming
    private const uint COFVID_STATUS = 0xC0010071;
    private const uint F17H_M01H_SVI = 0x0005A000;
    private const uint F17H_M01H_THM_TCON_CUR_TMP = 0x00059800;
    private const uint F17H_M70H_CCD1_TEMP = 0x00059954;
    private const uint F17H_M61H_CCD1_TEMP = 0x00059b08;
    private const uint F1AH_M00H_CCD1_TEMP = 0x000599F0; // 0x59800 + 0x1F0 (k10temp Turin / Shimada Peak)
    private const uint F17H_CCD_TEMP_VALID = 0x800;
    private const uint F17H_TEMP_RANGE_SEL_MASK = 0x80000;
    private const uint F17H_TEMP_TJ_SEL_MASK = 0x30000;
    private const uint FAMILY_17H_PCI_CONTROL_REGISTER = 0x60;
    private const uint HWCR = 0xC0010015;
    private const uint MSR_CORE_ENERGY_STAT = 0xC001029A;
    private const uint MSR_HARDWARE_PSTATE_STATUS = 0xC0010293;
    private const uint MSR_PKG_ENERGY_STAT = 0xC001029B;
    private const uint MSR_PSTATE_STATUS = 0xC0010063;
    private const uint MSR_PSTATE_0 = 0xC0010064;
    private const uint MSR_PWR_UNIT = 0xC0010299;
    private const uint MSR_MPERF_RO = 0xC000_00E7;
    private const uint MSR_APERF_RO = 0xC000_00E8;
    private const uint PERF_CTL_0 = 0xC0010000;
    private const uint PERF_CTR_0 = 0xC0010004;
    // ReSharper restore InconsistentNaming
}
