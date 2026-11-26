// SPDX-License-Identifier: MPL-2.0

namespace LibreHardwareMonitor.Avalonia.Mvvm.ViewModels;

/// <summary>
/// Represents a named detail of the selected tree node.
/// </summary>
/// <param name="Name">The name of the detail.</param>
/// <param name="Value">The displayed value.</param>
public sealed record DetailViewModel(string Name, string Value);
