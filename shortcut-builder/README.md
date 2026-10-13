# Shortcut builder (experimental branch)

The included shortcut is **only a signing smoke test**, not a working Instagram downloader.

GitHub Actions on macOS validates and signs `shortcut-builder/unsigned/*.shortcut` and uploads the signed output.

Next: derive the actual Scriptable action parameters from an exported working shortcut and build a reliable handoff to Photos. Do not install the smoke test expecting it to download Instagram media.

The existing Lughole app on main is untouched.
