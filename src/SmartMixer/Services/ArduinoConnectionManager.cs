using System.Diagnostics;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using SmartMixer.Core;

namespace SmartMixer.Services;

public sealed class ArduinoConnectionManager : IDisposable
{
    private readonly object _sync = new();
    private SerialPort? _serialPort;
    private DateTime _lastTouchUtc = DateTime.MinValue;
    private long _receivedPackets;
    private long _sentPackets;
    private long _errors;
    private long _droppedPackets;
    private double _latencyMilliseconds;
    private int[] _rawPotentiometers = new int[HardwareConfig.ChannelCount];
    private const int PotentiometerMovementDeadbandCounts = 5;
    private const int PotentiometerSweepStepCounts = 2;
    private const int PotentiometerIdleJitterCounts = 1;
    private readonly int[] _lastEmittedPotentiometers = new int[HardwareConfig.ChannelCount];
    private readonly int[] _previousPotentiometers = new int[HardwareConfig.ChannelCount];
    private readonly int[] _potentiometerBeforePrevious = new int[HardwareConfig.ChannelCount];
    private bool _hasPotentiometerBaseline;
    private readonly StringBuilder _receiveBuffer = new();
    private bool _lastTouchState;
    private uint _lastSequence;
    private bool _hasLastSequence;
    private long _pingSequence;
    private long _pendingPingTimestamp;
    private int _versionProbePackets;
    private int _versionProbeAttempts;
    private long _connectionStartedTimestamp;
    private int _initialFrameResynchronizationUsed;

    public bool IsConnected => _serialPort?.IsOpen == true;
    public string PortName => IsConnected ? _serialPort!.PortName : "Disconnected";
    public string FirmwareVersion { get; private set; } = "Unknown";
    public string? LastError { get; private set; }
    public DateTime? LastPacketUtc { get; private set; }
    public long PacketsReceived => Interlocked.Read(ref _receivedPackets);
    public long PacketsSent => Interlocked.Read(ref _sentPackets);
    public long Errors => Interlocked.Read(ref _errors);
    public long DroppedPackets => Interlocked.Read(ref _droppedPackets);
    public double AverageLatencyMilliseconds => _latencyMilliseconds;
    public int TouchEvents { get; private set; }
    public IReadOnlyList<double> PotentiometerValues { get; private set; } = new double[HardwareConfig.ChannelCount];
    public IReadOnlyList<int> RawPotentiometerValues => _rawPotentiometers;

    public event EventHandler? StateChanged;
    public event EventHandler? TelemetryReceived;
    public event EventHandler<PotentiometerSample>? PotentiometerChanged;
    public event EventHandler? TouchDetected;

    public IReadOnlyList<string> GetAvailablePorts() => SerialPort.GetPortNames().OrderBy(p => p).ToArray();

    public void Connect(string portName, int baudRate = HardwareConfig.DefaultBaudRate)
    {
        Disconnect();
        var serial = new SerialPort(portName, baudRate)
        {
            NewLine = "\n",
            ReadTimeout = 500,
            WriteTimeout = 500,
            DtrEnable = true,
            RtsEnable = true
        };
        lock (_sync)
        {
            _receiveBuffer.Clear();
            _lastSequence = 0;
            _hasLastSequence = false;
            _hasPotentiometerBaseline = false;
        }
        FirmwareVersion = "Unknown";
        LastPacketUtc = null;
        Interlocked.Exchange(ref _versionProbePackets, 0);
        Interlocked.Exchange(ref _versionProbeAttempts, 0);
        Interlocked.Exchange(ref _initialFrameResynchronizationUsed, 0);
        Interlocked.Exchange(ref _connectionStartedTimestamp, Stopwatch.GetTimestamp());
        try
        {
            serial.Open();
            lock (_sync)
            {
                _serialPort = serial;
            }
            serial.DataReceived += Serial_DataReceived;
            RequestFirmwareVersion();
            LastError = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (UnauthorizedAccessException)
        {
            serial.DataReceived -= Serial_DataReceived;
            serial.Dispose();
            Interlocked.Increment(ref _errors);
            LastError = $"Access to {portName} was denied. Close other serial monitors and try again.";
            StateChanged?.Invoke(this, EventArgs.Empty);
            throw;
        }
        catch (IOException)
        {
            lock (_sync)
            {
                if (ReferenceEquals(_serialPort, serial))
                    _serialPort = null;
            }
            serial.DataReceived -= Serial_DataReceived;
            serial.Dispose();
            Interlocked.Increment(ref _errors);
            LastError = $"Could not open {portName}. Check the cable and reconnect the Arduino.";
            StateChanged?.Invoke(this, EventArgs.Empty);
            throw;
        }
    }

    public void Disconnect()
    {
        SerialPort? port;
        lock (_sync)
        {
            port = _serialPort;
            _serialPort = null;
        }
        if (port is not null)
        {
            port.DataReceived -= Serial_DataReceived;
            if (port.IsOpen) port.Close();
            port.Dispose();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SendLedState(byte mask, MixerMode mode)
    {
        var modeValue = mode == MixerMode.Firefox ? 1 : 0;
        SendLine($"L,{mask},{modeValue}");
    }

    public void TestLed(int channel, int milliseconds = 500)
    {
        if (channel is < 1 or > HardwareConfig.ChannelCount)
            throw new ArgumentOutOfRangeException(nameof(channel));
        SendLine($"T,{channel},{milliseconds}");
    }

    public void SendPing()
    {
        var sequence = Interlocked.Increment(ref _pingSequence) & 0xFFFF;
        Interlocked.Exchange(ref _pendingPingTimestamp, Stopwatch.GetTimestamp());
        SendLine($"P,{sequence}");
    }

    private void SendLine(string line)
    {
        lock (_sync)
        {
            if (_serialPort?.IsOpen != true)
                throw new InvalidOperationException("The Arduino is not connected.");
            try
            {
                _serialPort.WriteLine(line);
                Interlocked.Increment(ref _sentPackets);
            }
            catch (IOException ex)
            {
                Interlocked.Increment(ref _errors);
                LastError = ex.Message;
                StateChanged?.Invoke(this, EventArgs.Empty);
                throw;
            }
        }
    }

    private void Serial_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var port = (SerialPort)sender;
        try
        {
            var incoming = port.ReadExisting();
            List<string> lines = [];
            lock (_sync)
            {
                _receiveBuffer.Append(incoming);
                while (true)
                {
                    var newline = _receiveBuffer.ToString().IndexOf('\n');
                    if (newline < 0) break;
                    lines.Add(_receiveBuffer.ToString(0, newline).TrimEnd('\r'));
                    _receiveBuffer.Remove(0, newline + 1);
                }
                if (_receiveBuffer.Length > 512)
                {
                    _receiveBuffer.Clear();
                    Interlocked.Increment(ref _droppedPackets);
                }
            }

            foreach (var line in lines)
            {
                if (line.StartsWith("I,", StringComparison.Ordinal) && line.Length <= 64)
                {
                    FirmwareVersion = line[2..];
                    StateChanged?.Invoke(this, EventArgs.Empty);
                    continue;
                }
                if (line.StartsWith("A,", StringComparison.Ordinal)
                    && long.TryParse(line.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var pingSequence)
                    && pingSequence == (Interlocked.Read(ref _pingSequence) & 0xFFFF)
                    && Interlocked.Exchange(ref _pendingPingTimestamp, 0) is var sentAt && sentAt > 0)
                {
                    var latency = Stopwatch.GetElapsedTime(sentAt).TotalMilliseconds;
                    _latencyMilliseconds = _latencyMilliseconds == 0 ? latency : (_latencyMilliseconds * 0.8) + (latency * 0.2);
                    continue;
                }
                if (!SerialProtocol.TryParseDataPacket(line, out var sequence, out var rawValues, out var touch))
                {
                    if (TryResynchronizeInitialFrame())
                    {
                        _hasLastSequence = false;
                        continue;
                    }
                    Interlocked.Increment(ref _droppedPackets);
                    continue;
                }
                if (_hasLastSequence)
                {
                    var delta = unchecked(sequence - _lastSequence);
                    if (delta == 0 || delta >= 0x80000000U)
                    {
                        if (TryResynchronizeInitialFrame())
                        {
                            _hasLastSequence = false;
                            continue;
                        }
                        Interlocked.Increment(ref _droppedPackets);
                        continue;
                    }
                    if (delta > 1 && !TryResynchronizeInitialFrame())
                        Interlocked.Add(ref _droppedPackets, delta - 1);
                }
                _lastSequence = sequence;
                _hasLastSequence = true;

                var now = DateTime.UtcNow;
                LastPacketUtc = now;
                Interlocked.Increment(ref _receivedPackets);
                if (FirmwareVersion == "Unknown"
                    && Interlocked.Increment(ref _versionProbePackets) >= 30
                    && Volatile.Read(ref _versionProbeAttempts) < 6)
                {
                    RequestFirmwareVersion();
                }
                var normalized = new double[HardwareConfig.ChannelCount];
                for (var i = 0; i < rawValues.Length; i++)
                    normalized[i] = Math.Clamp(rawValues[i] / 1023d, 0, 1);
                PotentiometerValues = normalized;
                _rawPotentiometers = rawValues;
                var movementSamples = new List<PotentiometerSample>(rawValues.Length);
                UpdatePotentiometerMovements(rawValues, normalized, movementSamples);
                foreach (var sample in movementSamples)
                    PotentiometerChanged?.Invoke(this, sample);
                if (touch && !_lastTouchState && now - _lastTouchUtc >= TimeSpan.FromMilliseconds(HardwareConfig.TouchDebounceMilliseconds))
                {
                    _lastTouchUtc = now;
                    TouchEvents++;
                    TouchDetected?.Invoke(this, EventArgs.Empty);
                }
                _lastTouchState = touch;
                TelemetryReceived?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (InvalidOperationException)
        {
            Interlocked.Increment(ref _errors);
            _ = Task.Run(Disconnect);
        }
        catch (IOException ex)
        {
            Interlocked.Increment(ref _errors);
            LastError = ex.Message;
            StateChanged?.Invoke(this, EventArgs.Empty);
            _ = Task.Run(Disconnect);
        }
    }

    private void RequestFirmwareVersion()
    {
        SendLine("V");
        Interlocked.Increment(ref _versionProbeAttempts);
        Interlocked.Exchange(ref _versionProbePackets, 0);
    }

    /// <summary>
    /// Collects readings that indicate a potentiometer was physically moved.
    /// Telemetry streams continuously, so ADC jitter and electrical noise (for
    /// example from the status LEDs sharing the board) must not look like
    /// movement: a sample only counts when the channel moved beyond the
    /// deadband and either swept in a consistent direction across the last two
    /// packets or settled at the new position. The first packet after a
    /// connection only establishes the baseline and never emits movement.
    /// </summary>
    private void UpdatePotentiometerMovements(int[] rawValues, double[] normalized, List<PotentiometerSample> movementSamples)
    {
        if (!_hasPotentiometerBaseline)
        {
            Array.Copy(rawValues, _lastEmittedPotentiometers, HardwareConfig.ChannelCount);
            Array.Copy(rawValues, _previousPotentiometers, HardwareConfig.ChannelCount);
            Array.Copy(rawValues, _potentiometerBeforePrevious, HardwareConfig.ChannelCount);
            _hasPotentiometerBaseline = true;
            return;
        }

        for (var i = 0; i < rawValues.Length; i++)
        {
            var raw = rawValues[i];
            var emitted = _lastEmittedPotentiometers[i];
            var newerStep = raw - _previousPotentiometers[i];
            var olderStep = _previousPotentiometers[i] - _potentiometerBeforePrevious[i];
            var isSweeping = newerStep >= PotentiometerSweepStepCounts
                ? olderStep >= PotentiometerSweepStepCounts
                : newerStep <= -PotentiometerSweepStepCounts && olderStep <= -PotentiometerSweepStepCounts;
            var isSettled = Math.Abs(newerStep) <= PotentiometerIdleJitterCounts
                && Math.Abs(olderStep) <= PotentiometerIdleJitterCounts;
            if (isSweeping || isSettled)
            {
                if (Math.Abs(raw - emitted) >= PotentiometerMovementDeadbandCounts)
                {
                    _lastEmittedPotentiometers[i] = raw;
                    movementSamples.Add(new PotentiometerSample(i + 1, raw, normalized[i] * 100));
                }
            }
        }

        Array.Copy(_previousPotentiometers, _potentiometerBeforePrevious, HardwareConfig.ChannelCount);
        Array.Copy(rawValues, _previousPotentiometers, HardwareConfig.ChannelCount);
    }

    private bool TryResynchronizeInitialFrame()
    {
        var started = Interlocked.Read(ref _connectionStartedTimestamp);
        return started != 0
            && Stopwatch.GetElapsedTime(started) <= TimeSpan.FromMilliseconds(250)
            && Interlocked.Exchange(ref _initialFrameResynchronizationUsed, 1) == 0;
    }

    public void Dispose() => Disconnect();
}
