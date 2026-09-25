using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ACEvo_Simple_Telemetry;

public static class DebugLog
{
    private const double TelemetryLogIntervalSeconds = 0.1;
    private const uint AttachParentProcess = 0xFFFFFFFF;
    private const int ErrorAccessDenied = 5;

    private static readonly object Sync = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static double _lastTelemetryLogTime = double.NegativeInfinity;
    private static int _lastTelemetryPacketId = int.MinValue;

    public static bool Enabled { get; private set; }

    public static void Initialize(string[] commandLineArgs)
    {
        if (!commandLineArgs.Any(argument =>
                string.Equals(argument, "--console-log", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        bool hasConsole = AttachConsole(AttachParentProcess);
        if (!hasConsole && Marshal.GetLastWin32Error() == ErrorAccessDenied)
        {
            // The process is already attached to a console (for example under a debugger).
            hasConsole = true;
        }

        if (!hasConsole)
        {
            hasConsole = AllocConsole();
        }

        if (!hasConsole)
        {
            return;
        }

        try
        {
            StreamWriter output = new(Console.OpenStandardOutput(), new UTF8Encoding(false))
            {
                AutoFlush = true
            };
            Console.SetOut(output);
            Console.SetError(output);
            Enabled = true;
            _lastTelemetryLogTime = double.NegativeInfinity;
            _lastTelemetryPacketId = int.MinValue;
            Info("Console logging enabled. Raw telemetry snapshots are sampled at 10 Hz.");
            Info("Graphics columns: packetId, statusRaw, tcActiveRaw, absActiveRaw, gasPercentRaw, brakePercentRaw, clutchPercentRaw, gearIntRaw, steerDegreesRaw");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Enabled = false;
        }
    }

    public static void Telemetry(
        int packetId,
        int statusRaw,
        bool tcActiveRaw,
        bool absActiveRaw,
        float gasRaw,
        float brakeRaw,
        float clutchRaw,
        int gearIntRaw,
        int steerDegreesRaw)
    {
        lock (Sync)
        {
            double now = Clock.Elapsed.TotalSeconds;
            if (!Enabled || packetId == _lastTelemetryPacketId ||
                now - _lastTelemetryLogTime < TelemetryLogIntervalSeconds)
            {
                return;
            }

            _lastTelemetryLogTime = now;
            _lastTelemetryPacketId = packetId;
            string message = string.Create(
                CultureInfo.InvariantCulture,
                $"packetId={packetId}, statusRaw={statusRaw}, tcActiveRaw={tcActiveRaw}, absActiveRaw={absActiveRaw}, " +
                $"gasPercentRaw={gasRaw:R}, brakePercentRaw={brakeRaw:R}, " +
                $"clutchPercentRaw={clutchRaw:R}, gearIntRaw={gearIntRaw}, steerDegreesRaw={steerDegreesRaw}");
            WriteLine("RAW", message);
        }
    }

    public static void Info(string message)
    {
        lock (Sync)
        {
            WriteLine("INFO", message);
        }
    }

    public static void Warning(string message)
    {
        lock (Sync)
        {
            WriteLine("WARN", message);
        }
    }

    public static void Shutdown()
    {
        lock (Sync)
        {
            WriteLine("INFO", "Console logging stopped.");
            Enabled = false;
        }
    }

    private static void WriteLine(string level, string message)
    {
        if (!Enabled)
        {
            return;
        }

        Console.WriteLine($"{DateTimeOffset.Now:O} [{level}] {message}");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();
}
