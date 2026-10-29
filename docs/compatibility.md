# Compatibility report — 2026-10-27

## Decision

**Keep Avalonia as the leading candidate, but do not yet approve the full port.**
The Mac-native workflow and self-contained Windows cross-publishing work. The
Windows executable passes protocol, credential, and audio integration checks in
Whisky. However, the default Windows graphics path fails in this Wine engine,
and visible Windows UI validation remains incomplete because the available
macOS UI automation times out attaching to the Wine window. No framework change
or Windows VM was introduced.

The implemented deliverable is the requested Connection/Ask prototype. Remaining
manual checks below are an explicit gate, not a claim of Windows certification.

## Environment and commands

| Component | Tested value |
|---|---|
| Host | Apple Silicon (`arm64`), macOS 27.0.1, build 26A434 |
| SDK | .NET 10.0.302 |
| UI | Avalonia 11.3.22, Fluent theme, standard window chrome |
| Windows publish | Self-contained `win-x64`, Release, no trimming/ReadyToRun/AOT |
| Whisky application | 2.3.5 |
| WhiskyWine package | 2.5.0, build 0 |
| Engine version | `wine-7.7` from Whisky's `Libraries/Wine/bin/wine64` |
| Bottle | `.whisky/intel-prototype`, fresh CLI-managed prefix, Windows version `win10` |
| Bottle modifications | Only Windows version; no DLL overrides, DXVK, D3DMetal, or extra runtime installations |
| Service used | Python synthetic HTTP fixture bound only to `127.0.0.1:18765` |
| Real daemon | Not contacted; live testing remains opt-in |

Publishing is reproducible with:

```bash
./scripts/publish-windows.sh
# Internally:
dotnet publish src/Intel.Desktop -c Release -r win-x64 --self-contained true \
  -p:PublishTrimmed=false -p:PublishReadyToRun=false -o artifacts/win-x64
```

The bottle uses the installed Whisky engine directly. It is **not registered in
the Whisky GUI's bottle list** and is isolated from the user's existing bottles.
`./scripts/run-whisky.sh --software --fixture-url http://127.0.0.1:18765`
launches the candidate software-rendered UI. Omitting `--software` reproduces the
accelerated rendering problem.

## Evidence matrix

“Automated pass” means an executable assertion or API check completed. It does
not imply perceptual audio quality or manual UI confirmation.

| Capability | Native Mac | Windows executable under Whisky |
|---|---|---|
| Build/publish | Pass | Pass: self-contained executable/ZIP built on Mac |
| Launch | Pass: visible window | Process launch pass; visual confirmation pending |
| Default rendering | Pass: inspected Connection and Ask | **Fail:** repeated D3D vertex shader compilation errors |
| Software rendering | Not needed | Starts without shader errors; **visual result untested** |
| Text input/Enter-to-ask | Manual pass | Untested: UI automation cannot attach |
| Resize | Manual pass: normal and zoomed layout | Untested |
| Authenticated HTTP | Automated pass against loopback fixture | Automated pass against same Mac loopback fixture |
| Lookup/delta/early/final events | Automated and visible UI pass | Automated pass |
| Answer cancellation | Automated pass, headless control-state checks | Automated pass |
| Keychain/Credential Manager | Automated write/read and separate-process persistence pass | Automated write/read and separate-process persistence pass after identifier fix |
| Audio streaming/cancellation | Native API checks pass | Native waveOut API checks pass |
| Audible speech quality | Untested: synthetic tone only | Untested: synthetic tone only |
| Clipboard | Manual pass: copy remote path, paste into question field | Untested |
| Browser launch | Untested: computer-use access to Orion was not approved | Untested |
| Actual Windows 10/11 | Not applicable | Untested; Wine version emulation is not Windows validation |

**Final verification: 27 tests passed, zero failed.** Locked restore also passed
after publishing both runtime targets. Machine-readable results from the final
packaged binaries are preserved in [Mac integration](evidence/mac-smoke.json),
[Mac persistence](evidence/mac-persistence.json),
[Whisky integration](evidence/whisky-smoke.json), and
[Whisky persistence](evidence/whisky-persistence.json).

The xUnit suite covers API contracts/authentication, server errors, fragmented
UTF-8/SSE, unknown/incomplete events, truncated streams, cancellation, final-answer
replacement, early speech without duplicate playback, WAV fragmentation and
rejection cases, reference routing, and headless input/control states.

## Problems found

### Windows CNG hash crash — resolved for credential identifiers

The initial smoke run crashed with `0xC0000005` inside
`System.Security.Cryptography.HashProviderDispenser.OneShotHashProvider.HashDataUsingPseudoHandle`
when `SHA256.HashData` derived a credential identifier. The endpoint is nonsecret
metadata and does not need hashing. The identifier is now a fixed application
prefix plus the normalized URL. Tokens still go through the OS credential store;
there is no plaintext fallback. The same Windows integration check passed after
this change. This is evidence of a limitation in the tested Wine/.NET combination,
not proof that every cryptographic API works there. HTTPS/TLS against a real
remote daemon has not been tested.

### Accelerated rendering — workaround available, visual gate pending

Default launch produced repeated messages including:

```text
Warning: D3D shader compilation failed ... (vs_5_0)
C:\fakepath:5:1: E5000: syntax error, unexpected NEW_IDENTIFIER
ERR: Context11.cpp ... Error compiling dynamic vertex executable
```

`--software` selects Avalonia's `Win32RenderingMode.Software`. A fresh launch with
this switch stayed running and did not produce those shader errors. The macOS
computer-use interface timed out (`-10005: timeoutReached`) twice when attaching
to the Wine-hosted app, so its pixels, typing, clipboard and browser actions could
not be verified. No accelerated-rendering DLL replacement was attempted. A native Mac browser
handoff check also could not be completed: the computer-use tool reported that
access to the default browser, Orion, was not approved. Browser opening remains
implemented and reference routing is unit-tested, but no end-to-end pass is claimed.

### Open-ended WAV length — fixed before integration testing

Parser tests caught a nullable-length comparison incorrectly treating an
open-ended WAV payload as misaligned. The guard now checks finite lengths only.
Fragmentation and truncated-frame tests pass. Native audio adapters use bounded
queues rather than waiting for each individual buffer before submitting the next.

## Remaining gate

With the fixture server running, launch the Windows build using `--software`:

1. Confirm readable Connection and Ask screens, resize down to the minimum size,
   and check focus traversal with Tab, Enter and Escape.
2. Test connection; ask a fixture question, observe incremental text, stop midway,
   then ask again and confirm the final references.
3. Enable read-aloud, confirm the quiet test tone starts before the final answer,
   and stop it. Native API checks alone do not establish audible quality.
4. Copy the remote note path and paste it into the question field. Open the
   explicitly synthetic example.com reference and confirm browser handoff.
5. Retain the separate-process credential-read evidence; optionally verify the
   live-mode save/reopen flow with a user-supplied endpoint and token.

If software rendering or a core desktop integration fails, keep expansion paused
and attach a reproducible result here. If these checks pass, proceed to the full
client plan with software rendering documented for Whisky, while retaining actual
Windows testing as an unresolved release limitation. No full-port screens were
added during this prototype.
