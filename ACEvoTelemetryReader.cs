using System.IO.MemoryMappedFiles;
using System.IO;
using System.Diagnostics;

namespace ACEvo_Simple_Telemetry;

/// <summary>
/// Reads only the fields used by this overlay from AC EVO's official graphics mapping.
/// The game owns the mapping; this class only opens an existing mapping read-only.
/// </summary>
public sealed class ACEvoTelemetryReader : IDisposable
{
    private const string GraphicsMapName = @"Local\acevo_pmf_graphics";
    private const long GraphicsPrefixSize = 512;

    // SPageFileGraphicEvo prefix offsets with 4-byte packing, transcribed from
    // the official declaration order. All fields used here are before the first
    // embedded substructure.
    private const long PacketIdOffset = 0;
    private const long StatusOffset = 4;             // int32 ACEVO_STATUS
    private const long TcActiveOffset = 45;          // bool (1 byte)
    private const long AbsActiveOffset = 46;         // bool (1 byte)
    private const long GearOffset = 68;              // int16 gear_int
    private const long GasPercentOffset = 76;        // float
    private const long BrakePercentOffset = 80;      // float
    private const long ClutchPercentOffset = 88;     // float
    private const long SteerDegreesOffset = 156;     // int32

    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _view;
    private int _lastPacketId = int.MinValue;
    private long _lastPacketChangeTimestamp;

    public bool IsConnected => _view is not null;

    public bool TryConnect()
    {
        Disconnect();

        try
        {
            _mapping = MemoryMappedFile.OpenExisting(GraphicsMapName, MemoryMappedFileRights.Read);
            _view = _mapping.CreateViewAccessor(0, GraphicsPrefixSize, MemoryMappedFileAccess.Read);
            _lastPacketId = int.MinValue;
            _lastPacketChangeTimestamp = Stopwatch.GetTimestamp();
            DebugLog.Info($"Opened read-only shared-memory mapping '{GraphicsMapName}'.");
            return true;
        }
        catch (FileNotFoundException)
        {
            Disconnect();
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            Disconnect();
            return false;
        }
        catch (IOException)
        {
            Disconnect();
            return false;
        }
    }

    public bool TryRead(out TelemetrySample sample)
    {
        sample = default;
        MemoryMappedViewAccessor? view = _view;
        if (view is null)
        {
            return false;
        }

        try
        {
            // A packet-id check avoids displaying a partially updated graphics frame.
            int packetBefore = view.ReadInt32(PacketIdOffset);
            int statusRaw = view.ReadInt32(StatusOffset);
            bool tcActive = view.ReadBoolean(TcActiveOffset);
            bool absActive = view.ReadBoolean(AbsActiveOffset);
            int gear = view.ReadInt16(GearOffset);
            float throttle = view.ReadSingle(GasPercentOffset);
            float brake = view.ReadSingle(BrakePercentOffset);
            float clutch = view.ReadSingle(ClutchPercentOffset);
            int steerDegrees = view.ReadInt32(SteerDegreesOffset);
            int packetAfter = view.ReadInt32(PacketIdOffset);

            if (packetBefore != packetAfter || !AreFinite(throttle, brake, clutch) ||
                steerDegrees is < -3600 or > 3600)
            {
                return false;
            }

            if (packetAfter != _lastPacketId)
            {
                _lastPacketId = packetAfter;
                _lastPacketChangeTimestamp = Stopwatch.GetTimestamp();
            }
            else if (Stopwatch.GetElapsedTime(_lastPacketChangeTimestamp) > TimeSpan.FromSeconds(3))
            {
                // Our handle can keep a mapping alive after the game exits. A stale packet
                // releases it so the normal reconnect loop can accurately detect the game.
                DebugLog.Warning("Graphics packet remained unchanged for 3 seconds; releasing the mapping and reconnecting.");
                Disconnect();
                return false;
            }

            DebugLog.Telemetry(packetAfter, statusRaw, tcActive, absActive, throttle, brake, clutch, gear, steerDegrees);

            sample = new TelemetrySample(
                packetAfter,
                (ACEvoStatus)statusRaw,
                Math.Clamp(throttle, 0f, 1f),
                Math.Clamp(brake, 0f, 1f),
                Math.Clamp(clutch, 0f, 1f),
                tcActive,
                absActive,
                gear,
                steerDegrees * (float)(Math.PI / 180.0));
            return true;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or UnauthorizedAccessException)
        {
            Disconnect();
            return false;
        }
    }

    private static bool AreFinite(params float[] values) => values.All(float.IsFinite);

    private void Disconnect()
    {
        _view?.Dispose();
        _mapping?.Dispose();
        _view = null;
        _mapping = null;
    }

    public void Dispose() => Disconnect();
}

public enum ACEvoStatus
{
    Off = 0,
    Replay = 1,
    Live = 2,
    Pause = 3
}

public readonly record struct TelemetrySample(
    int PacketId,
    ACEvoStatus Status,
    float Throttle,
    float Brake,
    float Clutch,
    bool TcActive,
    bool AbsActive,
    int Gear,
    float SteeringRadians);
