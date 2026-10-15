# Lughole (prototype)

Stream PC loopback audio to an iPhone over UDP 58091; stream an Xbox/XInput controller separately over UDP 58092.

## Current state
This repository is **not yet a verified, installable iPhone application**. The iOS audio decoder/player is experimental and needs build and device testing. A signed IPA cannot be generated without an Apple signing setup.

## Windows XInput sender
Requires Windows, .NET 10 SDK, an XInput-compatible controller, and an iPhone on the same reachable local network.

```powershell
dotnet run --project ControllerServer -- 192.168.1.50
```

Replace the example address with your iPhone's current Wi-Fi IP. Optional second argument: controller index 0–3.

The original field layout adds up to **12 bytes**, not 14. This implementation sends 14 bytes for consistency with the requested receiver: the first 12 carry buttons/triggers/sticks in little-endian order; the final two are zero reserved bytes. Sending occurs on state change or at 500ms heartbeat intervals; controller disconnect sends a neutral state.

## iOS receiver
The MAUI app listens for controller messages on 58092 and shows telemetry. The audio receiver remains separate on 58091 and is started by pressing its button. Local network access must be allowed on iOS. Build and signing require a macOS runner/host, Xcode, the matching .NET iOS workload, and a valid Apple signing identity/provisioning profile for physical-device sideloading.

The controller receiver currently **displays** state; it does not inject that state as a virtual iOS gamepad or control other iOS apps. UDP is unencrypted and unauthenticated; use only on a trusted LAN.

Generated `bin/` and `obj/` directories were committed previously and should be cleaned from version control separately.
