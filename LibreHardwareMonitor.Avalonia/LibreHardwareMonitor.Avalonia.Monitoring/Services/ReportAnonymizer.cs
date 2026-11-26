// SPDX-License-Identifier: MPL-2.0

using System.Text.RegularExpressions;

namespace LibreHardwareMonitor.Avalonia.Monitoring.Services;

/// <summary>
/// Removes personal details from a hardware report before it is shared, for example in a bug report.
/// </summary>
public static partial class ReportAnonymizer
{
    /// <summary>
    /// The text that replaces removed values.
    /// </summary>
    public const string Removed = "(removed)";

    /// <summary>
    /// Replaces serial numbers, asset tags, UUIDs, MAC addresses, the computer name and the user name.
    /// </summary>
    /// <param name="report">The report.</param>
    /// <param name="machineName">The computer name to remove.</param>
    /// <param name="userName">The user name to remove.</param>
    /// <returns>The report without these details.</returns>
    public static string Anonymize(string report, string machineName, string userName)
    {
        if (string.IsNullOrEmpty(report))
            return report ?? string.Empty;

        // "Label: value" lines whose label names an identifying value.
        report = IdentifyingLine().Replace(report, match => match.Groups["label"].Value + Removed);
        report = MacAddress().Replace(report, Removed);

        // Names shorter than three characters would also hit unrelated text.
        foreach (string name in new[] { machineName, userName })
        {
            if (name is { Length: >= 3 })
                report = Regex.Replace(report, Regex.Escape(name), Removed, RegexOptions.IgnoreCase);
        }

        return report;
    }

    [GeneratedRegex(@"(?<label>^[^\r\n:]*(?:Serial|Asset Tag|UUID)[^\r\n:]*:[ \t]*)[^\r\n]+", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex IdentifyingLine();

    [GeneratedRegex(@"\b[0-9A-F]{2}(?:[:-][0-9A-F]{2}){5}\b", RegexOptions.IgnoreCase)]
    private static partial Regex MacAddress();
}
