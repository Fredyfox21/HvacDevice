# HvacDevice

Raspberry Pi firmware for a remote HVAC monitoring and control system. The Pi reads temperature and humidity from a physical sensor, shows it on a local LCD, reports it to Azure, and can switch a relay (the HVAC) on or off — either from the device itself or based on commands coming back from the cloud.

## How it fits together

```
 [BME280 sensor] --I2C--> [Raspberry Pi]
                              |  |  |
                LCD <---------+  |  +---> Azure IoT Hub (telemetry)
                              |
                GPIO relay <--+  --> HTTP POST --> Azure Function (hvacmeplease)
                (HVAC on/off)                            |
                                                          v
                                                   (returns desired HVAC state)

                                          Azure Communication Services --> email alert
```

This repo only contains the **device-side code that runs on the Raspberry Pi**. A separate, companion service (an Azure Function + web frontend, not part of this repo) is what actually stores the readings, renders the dashboard, and lets a user view temperature/humidity history, flip the HVAC on/off remotely, and change the target temperature limit. This device polls/pushes to that service; it doesn't host it.

## Use case

A low-cost way to keep an eye on a room's climate (e.g. a space with sensitive equipment, a rental, a workspace) without being physically present: it measures conditions continuously, exposes them on a web dashboard, and lets someone remotely turn the HVAC on/off or adjust the desired temperature from anywhere, with optional email alerts if things get out of range.

## Projects in this repo

| Project | Entry point | What it does |
|---|---|---|
| `Firstproject` (`Firstproject.csproj`, root `Program.cs`) | `Program.cs` | Minimal sensor test/demo: reads temperature, pressure, humidity, and altitude from the BME280 in a loop and prints them to the console. No Azure, no LCD, no relay control. Useful for verifying sensor wiring in isolation. |
| `firstprog` (`firstprog/firstprog.csproj`, `firstprog/Program.cs`) | `firstprog/Program.cs` | The real device firmware — see below. |

### `firstprog` — the main firmware

Presents an interactive console menu (`RDTMS` — Room/Device Temperature Monitoring System) over SSH/serial on the Pi with these commands:

- **`S`** — read the BME280 and print + display current temperature, pressure, humidity, and altitude on the 2004 (20x4) I2C LCD.
- **`H`** — toggle the HVAC relay (GPIO pin 18) on/off and update the LCD with the current state.
- **`T`** — start continuously transmitting telemetry: reads the sensor every ~3s, sends the reading to Azure IoT Hub via `DeviceClient`, and also POSTs temperature/humidity to an Azure Function endpoint (`hvacmeplease.azurewebsites.net`), which responds with the HVAC state the cloud/dashboard wants — the device then updates the LCD/relay to match. This is the loop that keeps the physical HVAC in sync with whatever the user set on the website.
- **`I`** — placeholder for changing the telemetry transmit interval (not yet implemented).
- **`X`** — close the program and shut off the relay pin.

Hardware used:
- **BME280** — temperature/humidity/pressure/altitude sensor, over I2C.
- **2004 LCD** (via a PCF8574 I2C GPIO expander) — local readout of sensor values and HVAC state.
- **GPIO relay** on pin 18 — switches the HVAC; pin 17 is used as a status/heartbeat LED in some code paths.

Cloud integration:
- **Azure IoT Hub** (`Microsoft.Azure.Devices.Client`) — device-to-cloud telemetry (temperature/humidity as JSON).
- **Azure Function** (`hvacmeplease.azurewebsites.net`) — receives temperature/humidity via query string, returns the desired HVAC on/off state, which the device applies locally. This is the write path the website uses to command the device.
- **Azure Communication Services** (`Azure.Communication.Email`) — sends an email alert (`messageemail()`) when conditions warrant a warning (e.g. "Watch out it's spicy today"). Not currently wired into the main loop — available as a standalone function.

## Configuration

Both cloud connection strings are read from environment variables — **do not hardcode credentials in source**:

| Variable | Used for |
|---|---|
| `IOT_HUB_CONNECTION_STRING` | Azure IoT Hub device connection string (`DeviceClient.CreateFromConnectionString`) |
| `ACS_CONNECTION_STRING` | Azure Communication Services connection string (`EmailClient`, used by `messageemail()`) |

Set them on the Pi before running, e.g.:

```bash
export IOT_HUB_CONNECTION_STRING="HostName=...;DeviceId=...;SharedAccessKey=..."
export ACS_CONNECTION_STRING="endpoint=https://...;accesskey=..."
```

The program throws on startup if `IOT_HUB_CONNECTION_STRING` is missing, and `messageemail()` throws if `ACS_CONNECTION_STRING` is missing when it's called.

> **Security note:** earlier versions of this repo had both connection strings hardcoded directly in `firstprog/Program.cs` and committed to git history on a public GitHub repo. If those specific keys haven't been rotated yet, treat them as compromised and regenerate them in the Azure portal (IoT Hub → Shared access policies; Communication Services → Keys) — removing them from the working tree doesn't remove them from git history.

## Running

```bash
cd firstprog
dotnet run
```

Requires network access from the Pi to Azure, I2C enabled on the Pi (`raspi-config`), and the sensor/LCD/relay wired to the pins referenced above.

## Known gaps

- The `I` menu option (change telemetry interval) is a stub.
- The webhook `sendHTTP` call sends a hardcoded placeholder payload (`Name`/`Action`/`Amount`) instead of the actual temperature/humidity in the POST body — the values are currently only passed via query string.
- `messageemail()` isn't called from anywhere in the main loop yet, so email alerts aren't automatic.
- No retry/backoff around network calls (IoT Hub send, HTTP POST) — a dropped connection just fails silently to console.
