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
    private static int _lastPhysicsPacketId = int.MinValue;
    private static int _lastGraphicsPacketId = int.MinValue;

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
            _lastPhysicsPacketId = int.MinValue;
            _lastGraphicsPacketId = int.MinValue;
            Info("Console logging enabled. Raw telemetry snapshots are sampled at 10 Hz.");
            Info("Hybrid columns include physics pedals/intervention signals and graphics status/gear/steering/intervention signals.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Enabled = false;
        }
    }

    public static void Telemetry(
        int physicsPacketId,
        int graphicsPacketId,
        int statusRaw,
        float gasRaw,
        float brakeRaw,
        float clutchRaw,
        float tcInterventionRaw,
        float absInterventionRaw,
        int tcInActionRaw,
        int absInActionRaw,
        bool graphicsTcActiveRaw,
        bool graphicsAbsActiveRaw,
        bool tcActive,
        bool absActive,
        int gearIntRaw,
        int steerDegreesRaw)
    {
        lock (Sync)
        {
            double now = Clock.Elapsed.TotalSeconds;
            if (!Enabled ||
                (physicsPacketId == _lastPhysicsPacketId && graphicsPacketId == _lastGraphicsPacketId) ||
                now - _lastTelemetryLogTime < TelemetryLogIntervalSeconds)
            {
                return;
            }

            _lastTelemetryLogTime = now;
            _lastPhysicsPacketId = physicsPacketId;
            _lastGraphicsPacketId = graphicsPacketId;
            string message = string.Create(
                CultureInfo.InvariantCulture,
                $"physicsPacketId={physicsPacketId}, graphicsPacketId={graphicsPacketId}, statusRaw={statusRaw}, " +
                $"physicsGasRaw={gasRaw:R}, physicsBrakeRaw={brakeRaw:R}, physicsClutchRaw={clutchRaw:R}, " +
                $"physicsTcRaw={tcInterventionRaw:R}, physicsAbsRaw={absInterventionRaw:R}, " +
                $"physicsTcInActionRaw={tcInActionRaw}, physicsAbsInActionRaw={absInActionRaw}, " +
                $"graphicsTcActiveRaw={graphicsTcActiveRaw}, graphicsAbsActiveRaw={graphicsAbsActiveRaw}, " +
                $"tcActive={tcActive}, absActive={absActive}, gearIntRaw={gearIntRaw}, steerDegreesRaw={steerDegreesRaw}");
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
