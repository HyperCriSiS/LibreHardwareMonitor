// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OperatingSystem = LibreHardwareMonitor.Software.OperatingSystem;

namespace LibreHardwareMonitor.Hardware.Cpu;

/// <summary>
/// Reads the scheduling class that Windows assigns to each logical processor. On CPUs with preferred
/// cores (AMD CPPC2, Intel Turbo Boost Max 3.0) a higher class marks a core that reaches higher clocks.
/// </summary>
internal static class CpuSchedulingClass
{
    private const int CpuSetInformation = 0;

    // Offsets in SYSTEM_CPU_SET_INFORMATION.
    private const int SizeOffset = 0;
    private const int TypeOffset = 4;
    private const int GroupOffset = 12;
    private const int LogicalProcessorIndexOffset = 14;
    private const int SchedulingClassOffset = 20;

    /// <summary>
    /// Gets the scheduling class of each logical processor, keyed by processor group and index within the group.
    /// </summary>
    /// <returns>The scheduling classes, or an empty dictionary if they are not available.</returns>
    public static IReadOnlyDictionary<(int Group, int Index), byte> Read()
    {
        Dictionary<(int, int), byte> classes = new();

        if (OperatingSystem.IsUnix)
            return classes;

        try
        {
            GetSystemCpuSetInformation(IntPtr.Zero, 0, out int length, IntPtr.Zero, 0);
            if (length <= 0)
                return classes;

            IntPtr buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetSystemCpuSetInformation(buffer, length, out length, IntPtr.Zero, 0))
                    return classes;

                int offset = 0;
                while (offset + SchedulingClassOffset < length)
                {
                    int size = Marshal.ReadInt32(buffer, offset + SizeOffset);
                    if (size <= SchedulingClassOffset)
                        break;

                    if (Marshal.ReadInt32(buffer, offset + TypeOffset) == CpuSetInformation)
                    {
                        int group = Marshal.ReadInt16(buffer, offset + GroupOffset);
                        int index = Marshal.ReadByte(buffer, offset + LogicalProcessorIndexOffset);
                        classes[(group, index)] = Marshal.ReadByte(buffer, offset + SchedulingClassOffset);
                    }

                    offset += size;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        {
            // Windows versions before 10 do not provide CPU sets.
        }

        return classes;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemCpuSetInformation(IntPtr information, int bufferLength, out int returnedLength, IntPtr process, int flags);
}
