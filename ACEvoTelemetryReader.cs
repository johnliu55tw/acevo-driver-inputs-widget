using System.IO.MemoryMappedFiles;
using System.IO;
using System.Diagnostics;

namespace ACEvo_Simple_Telemetry;

/// <summary>
/// Reads the fields used by this overlay from AC EVO's official physics and graphics mappings.
/// The game owns the mappings; this class only opens existing mappings read-only.
/// </summary>
public sealed class ACEvoTelemetryReader : IDisposable
{
    private const string PhysicsMapName = @"Local\acevo_pmf_physics";
    private const string GraphicsMapName = @"Local\acevo_pmf_graphics";
    private const long PhysicsPrefixSize = 680;
    private const long GraphicsPrefixSize = 512;

    // SPageFilePhysics offsets with 4-byte packing.
    private const long PhysicsPacketIdOffset = 0;
    private const long PhysicsGasOffset = 4;
    private const long PhysicsBrakeOffset = 8;
    private const long PhysicsTcOffset = 204;             // float intervention intensity
    private const long PhysicsAbsOffset = 252;            // float intervention intensity
    private const long PhysicsClutchOffset = 364;
    private const long PhysicsTcInActionOffset = 672;     // int32
    private const long PhysicsAbsInActionOffset = 676;    // int32

    // SPageFileGraphicEvo prefix offsets with 4-byte packing, transcribed from
    // the official declaration order. All fields used here are before the first
    // embedded substructure.
    private const long GraphicsPacketIdOffset = 0;
    private const long GraphicsStatusOffset = 4;          // int32 ACEVO_STATUS
    private const long GraphicsTcActiveOffset = 45;       // bool (1 byte)
    private const long GraphicsAbsActiveOffset = 46;      // bool (1 byte)
    private const long GraphicsGearOffset = 68;           // int16 gear_int
    private const long GraphicsSteerDegreesOffset = 156;  // int32

    private MemoryMappedFile? _physicsMapping;
    private MemoryMappedFile? _graphicsMapping;
    private MemoryMappedViewAccessor? _physicsView;
    private MemoryMappedViewAccessor? _graphicsView;
    private int _lastPhysicsPacketId = int.MinValue;
    private int _lastGraphicsPacketId = int.MinValue;
    private long _lastPhysicsPacketChangeTimestamp;
    private long _lastGraphicsPacketChangeTimestamp;

    public bool IsConnected => _physicsView is not null && _graphicsView is not null;

    public bool TryConnect()
    {
        Disconnect();

        try
        {
            _physicsMapping = MemoryMappedFile.OpenExisting(PhysicsMapName, MemoryMappedFileRights.Read);
            _physicsView = _physicsMapping.CreateViewAccessor(0, PhysicsPrefixSize, MemoryMappedFileAccess.Read);
            _graphicsMapping = MemoryMappedFile.OpenExisting(GraphicsMapName, MemoryMappedFileRights.Read);
            _graphicsView = _graphicsMapping.CreateViewAccessor(0, GraphicsPrefixSize, MemoryMappedFileAccess.Read);
            _lastPhysicsPacketId = int.MinValue;
            _lastGraphicsPacketId = int.MinValue;
            _lastPhysicsPacketChangeTimestamp = Stopwatch.GetTimestamp();
            _lastGraphicsPacketChangeTimestamp = Stopwatch.GetTimestamp();
            DebugLog.Info($"Opened read-only shared-memory mappings '{PhysicsMapName}' and '{GraphicsMapName}'.");
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
        MemoryMappedViewAccessor? physicsView = _physicsView;
        MemoryMappedViewAccessor? graphicsView = _graphicsView;
        if (physicsView is null || graphicsView is null)
        {
            return false;
        }

        try
        {
            // Each mapping is produced independently, so guard each snapshot with
            // its own packet id instead of assuming the two blocks are synchronized.
            int physicsPacketBefore = physicsView.ReadInt32(PhysicsPacketIdOffset);
            float throttle = physicsView.ReadSingle(PhysicsGasOffset);
            float brake = physicsView.ReadSingle(PhysicsBrakeOffset);
            float clutch = physicsView.ReadSingle(PhysicsClutchOffset);
            float tcIntervention = physicsView.ReadSingle(PhysicsTcOffset);
            float absIntervention = physicsView.ReadSingle(PhysicsAbsOffset);
            int tcInAction = physicsView.ReadInt32(PhysicsTcInActionOffset);
            int absInAction = physicsView.ReadInt32(PhysicsAbsInActionOffset);
            int physicsPacketAfter = physicsView.ReadInt32(PhysicsPacketIdOffset);

            int graphicsPacketBefore = graphicsView.ReadInt32(GraphicsPacketIdOffset);
            int statusRaw = graphicsView.ReadInt32(GraphicsStatusOffset);
            bool graphicsTcActive = graphicsView.ReadBoolean(GraphicsTcActiveOffset);
            bool graphicsAbsActive = graphicsView.ReadBoolean(GraphicsAbsActiveOffset);
            int gear = graphicsView.ReadInt16(GraphicsGearOffset);
            int steerDegrees = graphicsView.ReadInt32(GraphicsSteerDegreesOffset);
            int graphicsPacketAfter = graphicsView.ReadInt32(GraphicsPacketIdOffset);

            if (physicsPacketBefore != physicsPacketAfter ||
                graphicsPacketBefore != graphicsPacketAfter ||
                !AreFinite(throttle, brake, clutch, tcIntervention, absIntervention) ||
                steerDegrees is < -3600 or > 3600)
            {
                return false;
            }

            if (physicsPacketAfter != _lastPhysicsPacketId)
            {
                _lastPhysicsPacketId = physicsPacketAfter;
                _lastPhysicsPacketChangeTimestamp = Stopwatch.GetTimestamp();
            }
            // Physics is expected to stop while paused; only treat it as stale
            // while the graphics block says the simulation is live.
            else if ((ACEvoStatus)statusRaw == ACEvoStatus.Live &&
                     Stopwatch.GetElapsedTime(_lastPhysicsPacketChangeTimestamp) > TimeSpan.FromSeconds(3))
            {
                DebugLog.Warning("Physics packet remained unchanged for 3 seconds; releasing the mappings and reconnecting.");
                Disconnect();
                return false;
            }

            if (graphicsPacketAfter != _lastGraphicsPacketId)
            {
                _lastGraphicsPacketId = graphicsPacketAfter;
                _lastGraphicsPacketChangeTimestamp = Stopwatch.GetTimestamp();
            }
            else if (Stopwatch.GetElapsedTime(_lastGraphicsPacketChangeTimestamp) > TimeSpan.FromSeconds(3))
            {
                DebugLog.Warning("Graphics packet remained unchanged for 3 seconds; releasing the mappings and reconnecting.");
                Disconnect();
                return false;
            }

            bool tcActive = tcInAction != 0 || tcIntervention > 0f || graphicsTcActive;
            bool absActive = absInAction != 0 || absIntervention > 0f || graphicsAbsActive;

            DebugLog.Telemetry(
                physicsPacketAfter,
                graphicsPacketAfter,
                statusRaw,
                throttle,
                brake,
                clutch,
                tcIntervention,
                absIntervention,
                tcInAction,
                absInAction,
                graphicsTcActive,
                graphicsAbsActive,
                tcActive,
                absActive,
                gear,
                steerDegrees);

            sample = new TelemetrySample(
                physicsPacketAfter,
                graphicsPacketAfter,
                (ACEvoStatus)statusRaw,
                Math.Clamp(throttle, 0f, 1f),
                Math.Clamp(brake, 0f, 1f),
                1f - Math.Clamp(clutch, 0f, 1f),
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
        _physicsView?.Dispose();
        _graphicsView?.Dispose();
        _physicsMapping?.Dispose();
        _graphicsMapping?.Dispose();
        _physicsView = null;
        _graphicsView = null;
        _physicsMapping = null;
        _graphicsMapping = null;
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
    int PhysicsPacketId,
    int GraphicsPacketId,
    ACEvoStatus Status,
    float Throttle,
    float Brake,
    float Clutch,
    bool TcActive,
    bool AbsActive,
    int Gear,
    float SteeringRadians);
