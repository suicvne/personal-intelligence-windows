# Intel Windows feasibility prototype

A C#/.NET 10 + Avalonia desktop client for the existing personal-intelligence
HTTP daemon. It targets Windows 10 22H2/Windows 11 x64, with a native Apple Silicon
Mac build for development. This is the Connection/Ask feasibility slice, not a
full replacement for the SwiftUI app.

See [the compatibility report](docs/compatibility.md) before deciding on the full
port. Real Windows hardware has not been tested. The existing daemon and SwiftUI
repositories are not modified.

## Build and run on Mac

Install the **.NET SDK 10.0.302** pinned in `global.json`. Avalonia is pinned to
**11.3.22**, and package lock files pin transitive dependencies. Python 3 is used
only for the optional fixture server and ZIP packaging.

```bash
./scripts/restore.sh
./scripts/test.sh
./scripts/run-mac.sh
```

The default is **fixture mode**: synthetic test records and a quiet 440 Hz tone.
It makes no network requests, does not load a live API token, and requires the
synthetic token `fixture-token`, which is prefilled. Select **Ask**, enter a
question, and press Enter. Escape stops the answer and audio. **Read aloud** starts
playback at the early `answer_text` event; **Read answer aloud** replays a finished
answer. Fixture answers are deterministic regardless of the question.

To test real loopback HTTP without your daemon, use two terminals:

```bash
python3 scripts/fixture-server.py
./scripts/run-mac.sh --fixture-url http://127.0.0.1:18765
```

To opt into your real daemon:

```bash
./scripts/run-mac.sh --live
```

Enter its reachable HTTP/HTTPS server URL and paste its API token. **Test
connection** checks authenticated `/api/status`. **Save securely** stores the
token in macOS Keychain or Windows Credential Manager, scoped to the normalized
server URL. Nonsecret live settings are saved under the current user's
`LocalApplicationData/IntelPrototype/settings.json`. On macOS this location is
normally `~/.local/share/IntelPrototype/settings.json` (as resolved by .NET).
**Use for this session** keeps the entered values in memory. If secure storage
fails, the app offers this same session-only option; it never writes a token to
the settings file. Fixture settings never overwrite live connection settings.

Remote note paths are displayed and copied exactly as returned by the daemon;
they are never launched as local files. Only HTTP/HTTPS references open a browser.
The extra example.com reference is explicitly synthetic, for desktop testing.

## Portable Windows build and Whisky

```bash
./scripts/publish-windows.sh
./scripts/run-whisky.sh --software
# Or, with the loopback fixture server running:
./scripts/run-whisky.sh --software --fixture-url http://127.0.0.1:18765
```

The publish command produces `artifacts/win-x64/Intel.exe` and
`artifacts/Intel-win-x64.zip`. Extract the **whole folder**; this is self-contained
and needs no separately installed .NET runtime. No installer, Native AOT,
trimming, signing, or auto-updater is included.

The Whisky script uses its installed Wine engine, not an independently installed
Wine. It creates an isolated bottle at `.whisky/intel-prototype`, sets its Windows
version to `win10`, and leaves existing Whisky bottles unchanged. This CLI-managed
bottle is not registered in Whisky's bottle list. Override `INTEL_WINE_BIN` if
Whisky's `Libraries/Wine/bin` is elsewhere.

**Use `--software` with the tested Whisky engine.** Its default accelerated
renderer logs D3D shader compilation errors. The switch affects only Windows;
the native Mac build uses its normal renderer. The software process launches
without those errors, but its Wine window could not be manually inspected by the
available UI automation. See the report's pending checks.

To build a locally ad-hoc-signed Mac app for Finder:

```bash
./scripts/package-mac.sh
open 'artifacts/Intel Prototype.app'
# Live mode through the app bundle:
open -n 'artifacts/Intel Prototype.app' --args --live
```

## Repeatable integration checks

Start `scripts/fixture-server.py` first. The smoke command does not open a window.
It checks actual HTTP, event streaming and cancellation. The optional audio flag
plays a three-second quiet test tone and then checks cancellation. The credentials
flag writes only a synthetic credential named `IntelPrototype/IntelPrototype-smoke-test`.

```bash
./scripts/run-mac.sh --smoke --fixture-url http://127.0.0.1:18765 \
  --credentials --audio --report artifacts/mac-smoke.json
./scripts/run-whisky.sh --smoke --fixture-url http://127.0.0.1:18765 \
  --credentials --audio --report 'Z:\Users\mike\git\personal-intelligence-windows\artifacts\whisky-smoke.json'
```

For another checkout, replace the Windows `Z:` report path with that checkout's
absolute path mapped through Wine. Omit `--report` to print JSON to stdout only.
Run again with **`--credentials-read`** instead of `--credentials` to verify
persistence across separate processes without rewriting the credential. Exit 0
means all requested checks passed; exit 1 means a reported check failed. A native
process crash can prevent a report from being written.

## Code and tests

- `src/Intel.Core`: DTOs, authenticated HTTP, incremental SSE and WAV parsing,
  fixture handler, and UI-independent state. Native interfaces cover secure
  storage, audio, and reference actions.
- `src/Intel.Platform`: Keychain/Credential Manager, bounded native audio queues
  using AudioQueue/waveOut, and nonsecret settings.
- `src/Intel.Desktop`: Fluent desktop window, OS reference actions, and smoke runner.
- `tests/Intel.Tests`: xUnit contract, fragmentation, error/cancellation, early
  speech, view-model, reference routing, and Avalonia headless UI tests. Fixture
  provenance is documented next to the samples.

The SSE client accepts fragmented UTF-8 and multiline events, skips unknown event
types, preserves partial text on interruption, and requires a final `answer` event
for successful completion. Speech accepts mono/stereo PCM16 WAV at 8–192 kHz,
including open-ended data sizes; truncated frames and unsupported formats fail
visibly. Audio memory is bounded to eight queued buffers. HTTP redirects are not
followed by the desktop client, so bearer credentials cannot be redirected to a
different server.

Full screen parity, persona editing, source management, daemon installation,
Windows daemon support, installers, and release signing remain outside this
prototype. The compatibility gate must be resolved before expanding the port.
