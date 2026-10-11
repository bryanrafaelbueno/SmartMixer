# SmartMixer serial protocol v1

The Arduino and the Windows application talk over a plain serial link: **8N1 at
115200 baud**. Every frame is ASCII, comma-separated and terminated with `LF`;
`CRLF` is accepted. The receiver buffers fragmented lines and rejects malformed
or out-of-range fields. Telemetry is sent at approximately **33 Hz**.

Because opening a USB serial port can begin mid-frame, the host allows a single
sequence/frame resynchronization during the first **250 ms** after connecting.
After that window, malformed, duplicated or missing frames count as dropped
packets.

## Arduino → Windows

Firmware identification, sent once after startup and whenever it is requested
with `V`:

```text
I,1.0.0
```

Telemetry:

```text
D,<sequence>,<p1>,<p2>,<p3>,<p4>,<p5>,<p6>,<p7>,<touch>
```

| Field | Meaning |
|---|---|
| `sequence` | unsigned 32-bit counter that wraps at `4294967295` |
| `p1..p7` | raw ADC readings `0..1023` for potentiometers 1–7 |
| `touch` | `0` released, `1` pressed after the 40 ms firmware debounce |

The host additionally requires a rising edge and enforces a 300 ms debounce, so
a held finger toggles once.

Ping acknowledgement, echoing the 16-bit sequence that was sent:

```text
A,<sequence>
```

The host sends a ping every three seconds and estimates the round-trip serial
latency from the matching acknowledgement.

## Windows → Arduino

| Command | Format | Description |
|---|---|---|
| Version request | `V` | The Arduino answers with the `I,<version>` frame above. The app retries while telemetry is arriving if the boot banner was missed during a USB reconnect. |
| LED and mode | `L,<mask>,<mode>` | `mask` is `0..127`; bit 0 maps P1/LED1 through bit 6 maps P7/LED7. `mode` is `0` for Applications (assigned LEDs on steady) and `1` for Firefox (assigned LEDs pulse). |
| LED test | `T,<channel>,<duration_ms>` | Lights one channel LED. Channel is `1..7`; duration is `50..5000` ms. |
| Ping | `P,<sequence>` | The firmware returns the same `A,<sequence>`. |

Unknown commands, extra fields, invalid numbers and out-of-range values are
ignored by the firmware.

## Host-side input handling

Telemetry streams continuously, so electrical noise and ADC jitter must not look
like knob movement. The Windows application only forwards a potentiometer sample
when the channel moved beyond a small deadband **and** either swept in a
consistent direction across the last two packets or settled at the new position.
The first telemetry packet after a connection only establishes the baseline and
never emits movement.

## Timing parameters

| Parameter | Value | Where |
|---|---|---|
| Telemetry interval | 30 ms (~33 Hz) | firmware, `REPORT_INTERVAL_MS` |
| Touch debounce | 40 ms | firmware, `TOUCH_DEBOUNCE_MS` |
| Host touch debounce | 300 ms | `Core/HardwareConfig.cs` |
| Ping interval | 3 s | `Services/MixerRuntime.cs` |
| Command line capacity | 64 bytes | firmware, `LINE_CAPACITY` |
| Initial resync window | 250 ms | `Services/ArduinoConnectionManager.cs` |

## Default Pro Micro pin map

| Function | Arduino pin |
|---|---|
| Potentiometers 1–7 | A0, A1, A2, A3, A6, A7, A8 |
| LEDs 1–7 | D2, D3, D5, D7, D14, D15, D16 |
| Touch sensor | D9 (active-high by default; set `TOUCH_ACTIVE_LOW` for active-low modules) |

The pin mapping is centralized in the firmware (`firmware/SmartMixerProMicro/SmartMixerProMicro.ino`)
and in `src/SmartMixer/Core/HardwareConfig.cs`. Keep both in sync when changing
the wiring.
