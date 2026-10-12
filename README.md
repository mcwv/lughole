# Lughole

Listen to your Windows PC's audio on your iPhone over home Wi-Fi.

- `server/`: a small Windows console app. It captures whatever the PC is playing and streams it as Opus over UDP.
- `app/`: the iPhone app (.NET MAUI). It connects to the PC and plays the stream.

Neither one needs a Mac. GitHub Actions builds both in the cloud.

## Getting the builds

1. Open the repo on GitHub and go to **Actions**.
2. **Build Windows server**: open the latest green run and download **Lughole-Server-exe** from Artifacts.
3. **Build iPhone app**: open the latest green run and download **Lughole-ipa** from Artifacts.

Either workflow can also be started by hand: pick it in Actions, then **Run workflow**.

## Putting the app on your iPhone

The `.ipa` is unsigned, so a sideloading tool signs it with your Apple ID:

1. Install [Sideloadly](https://sideloadly.io) on the PC and plug the iPhone in with a cable.
2. Drag `Lughole.ipa` into Sideloadly, enter your Apple ID, and click Start.
3. On the iPhone, go to Settings → General → VPN & Device Management and trust your Apple ID.
4. If this is your first sideloaded app, turn on Settings → Privacy & Security → Developer Mode and restart.

With a free Apple ID the app expires after 7 days. Re-run Sideloadly to refresh it.

## Using it

1. On the PC, run `Lughole.Server.exe`. The first time, Windows Firewall asks for permission. Allow it on **Private** networks.
2. The server prints the PC's address, something like `192.168.1.10`.
3. On the iPhone, open Lughole, type that address, and tap **Listen**. Allow **Local Network** access when iOS asks.
4. Play something on the PC.

Audio keeps playing when the phone is locked. Tap **Stop** to disconnect.

## Troubleshooting

- **"Waiting for … is Lughole.Server running?"**: the phone can't reach the PC.
  - Make sure both are on the same Wi-Fi.
  - Check that the address matches what the server printed.
  - Check that the firewall allows `Lughole.Server.exe`. It needs to receive on UDP port 58091.
- **"Connected, but nothing's playing on the PC."**: that's normal while the PC is silent. Windows sends nothing until a sound plays.
- **No sound and no Local Network prompt**: on the iPhone, go to Settings → Privacy & Security → Local Network and switch Lughole on.

To use a different port, run `Lughole.Server.exe 50000`. The app uses 58091 for now.

## How it works

- Every second, the phone sends the PC a tiny `LUGHOLE` hello packet.
- The PC streams 20 ms Opus frames (48 kHz stereo, 128 kbps) to every phone it has heard from in the last 5 seconds.
- The phone buffers about 60 ms before it starts playing. It drops frames when more than about 200 ms build up, so delay stays low.
