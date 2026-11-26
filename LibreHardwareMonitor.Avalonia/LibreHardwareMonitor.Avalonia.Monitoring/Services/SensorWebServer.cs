// SPDX-License-Identifier: MPL-2.0

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibreHardwareMonitor.Avalonia.Monitoring.Models;
using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.Avalonia.Monitoring.Services;

/// <summary>
/// Serves the latest sensor readings as data.json in the format of the classic LibreHardwareMonitor web server, so
/// existing integrations keep working. The server only reads; it offers no way to change hardware or settings.
/// </summary>
public sealed class SensorWebServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // NaN readings are written as "NaN" like the classic server does.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private readonly HttpListener _listener = new();
    private SensorTreeSnapshot _snapshot;

    /// <summary>
    /// Starts listening.
    /// </summary>
    /// <param name="port">The TCP port.</param>
    /// <param name="localOnly">True to accept only requests from this computer.</param>
    public SensorWebServer(int port, bool localOnly)
    {
        _listener.Prefixes.Add(localOnly ? $"http://localhost:{port}/" : $"http://+:{port}/");
        _listener.Start();
        _ = Task.Run(ListenAsync);
    }

    /// <summary>
    /// Gets the address of data.json for display.
    /// </summary>
    public string DataAddress => _listener.Prefixes.First().Replace("+", Environment.MachineName) + "data.json";

    /// <summary>
    /// Replaces the readings that requests receive.
    /// </summary>
    /// <param name="snapshot">The latest snapshot.</param>
    public void Update(SensorTreeSnapshot snapshot)
    {
        Volatile.Write(ref _snapshot, snapshot);
    }

    /// <summary>
    /// Stops listening.
    /// </summary>
    public void Dispose()
    {
        _listener.Close();
    }

    /// <summary>
    /// Answers requests until the listener is closed.
    /// </summary>
    private async Task ListenAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (!_listener.IsListening)
            {
                return;
            }
            catch (HttpListenerException)
            {
                continue;
            }

            try
            {
                Respond(context);
            }
            catch (Exception)
            {
                // A client that disconnects early must not stop the server.
            }
            finally
            {
                context.Response.Close();
            }
        }
    }

    /// <summary>
    /// Writes data.json, or 404 for any other path and 405 for methods other than GET.
    /// </summary>
    private void Respond(HttpListenerContext context)
    {
        HttpListenerResponse response = context.Response;

        if (context.Request.HttpMethod != "GET")
        {
            response.StatusCode = 405;
            return;
        }

        if (!string.Equals(context.Request.Url?.AbsolutePath, "/data.json", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = 404;
            return;
        }

        byte[] buffer = Encoding.UTF8.GetBytes(CreateJson(Volatile.Read(ref _snapshot)));
        response.ContentType = "application/json; charset=utf-8";
        response.AddHeader("Cache-Control", "no-cache");
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
    }

    /// <summary>
    /// Creates data.json for a snapshot.
    /// </summary>
    /// <param name="snapshot">The snapshot, or null before the first reading.</param>
    /// <returns>The JSON text.</returns>
    public static string CreateJson(SensorTreeSnapshot snapshot)
    {
        int nodeIndex = 0;
        Version version = typeof(SensorWebServer).Assembly.GetName().Version ?? new Version();

        Dictionary<string, object> json = new()
        {
            ["id"] = nodeIndex++,
            ["Version"] = $"{version.Major}.{version.Minor}.{version.Build}",
            ["Text"] = "Sensor",
            ["Min"] = "Min",
            ["Value"] = "Value",
            ["Max"] = "Max",
            ["ImageURL"] = string.Empty
        };

        List<object> children = new();
        foreach (SensorTreeNodeSnapshot root in snapshot?.Roots ?? [])
        {
            children.Add(CreateNode(root, ref nodeIndex));
        }

        json["Children"] = children;
        return JsonSerializer.Serialize(json, JsonOptions);
    }

    /// <summary>
    /// Creates the JSON object of a node and its children.
    /// </summary>
    private static Dictionary<string, object> CreateNode(SensorTreeNodeSnapshot node, ref int nodeIndex)
    {
        Dictionary<string, object> json = new()
        {
            ["id"] = nodeIndex++,
            ["Text"] = node.Kind == SensorTreeNodeKind.Category && node.SensorType is { } type ? GetCategoryName(type) : node.Name,
            ["Min"] = string.Empty,
            ["Value"] = string.Empty,
            ["Max"] = string.Empty
        };

        switch (node.Kind)
        {
            case SensorTreeNodeKind.Sensor:
                json["SensorId"] = node.Identifier;
                json["Type"] = node.SensorType?.ToString() ?? string.Empty;
                json["Min"] = Format(node.SensorType, node.Min);
                json["Value"] = Format(node.SensorType, node.Value);
                json["Max"] = Format(node.SensorType, node.Max);
                json["RawMin"] = node.Min;
                json["RawValue"] = node.Value;
                json["RawMax"] = node.Max;
                json["ImageURL"] = "images/transparent.png";
                break;

            case SensorTreeNodeKind.Hardware:
                json["HardwareId"] = node.Identifier;
                json["ImageURL"] = string.Empty;
                break;

            default:
                json["ImageURL"] = string.Empty;
                break;
        }

        List<object> children = new();
        foreach (SensorTreeNodeSnapshot child in node.Children)
        {
            children.Add(CreateNode(child, ref nodeIndex));
        }

        json["Children"] = children;
        return json;
    }

    /// <summary>
    /// Gets the English category name the classic server uses.
    /// </summary>
    private static string GetCategoryName(SensorType type)
    {
        return type switch
        {
            SensorType.Voltage => "Voltages",
            SensorType.Current => "Currents",
            SensorType.Energy => "Capacities",
            SensorType.Clock => "Clocks",
            SensorType.Load => "Load",
            SensorType.Temperature => "Temperatures",
            SensorType.Fan => "Fans",
            SensorType.Flow => "Flows",
            SensorType.Control => "Controls",
            SensorType.Level => "Levels",
            SensorType.Power => "Powers",
            SensorType.Data => "Data",
            SensorType.Factor => "Factors",
            SensorType.Frequency => "Frequencies",
            SensorType.Throughput => "Throughput",
            SensorType.TimeSpan => "Times",
            SensorType.Timing => "Timings",
            SensorType.Noise => "Noise Levels",
            SensorType.Conductivity => "Conductivities",
            SensorType.Humidity => "Humidity Levels",
            _ => type.ToString()
        };
    }

    /// <summary>
    /// Formats a value with its unit like the classic server, always with a decimal point so clients can parse it.
    /// </summary>
    private static string Format(SensorType? type, float? value)
    {
        if (value is not { } v)
            return "-";

        string format = type switch
        {
            SensorType.Voltage => "{0:F3} V",
            SensorType.Current => "{0:F3} A",
            SensorType.Clock => "{0:F1} MHz",
            SensorType.Load => "{0:F1} %",
            SensorType.Temperature => "{0:F1} °C",
            SensorType.Fan => "{0:F0} RPM",
            SensorType.Flow => "{0:F1} L/h",
            SensorType.Control => "{0:F1} %",
            SensorType.Level => "{0:F1} %",
            SensorType.Power => "{0:F1} W",
            SensorType.Data => "{0:F1} GB",
            SensorType.Factor => "{0:F3}",
            SensorType.Frequency => "{0:F1} Hz",
            SensorType.Throughput => "{0:F1} B/s",
            SensorType.TimeSpan => "{0:F0} s",
            SensorType.Timing => "{0:F3} ns",
            SensorType.Energy => "{0:F0} mWh",
            SensorType.Noise => "{0:F0} dBA",
            SensorType.Conductivity => "{0:F1} µS/cm",
            SensorType.Humidity => "{0:F0} %",
            _ => "{0:F2}"
        };

        return string.Format(CultureInfo.InvariantCulture, format, v);
    }
}
