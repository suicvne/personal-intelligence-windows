# Synthetic fixtures

`ask.json`, `ask_stream.txt`, and `status.json` are copied unchanged from
`personal-intelligence-swiftui/Tests/IntelKitTests/Samples` (2026-10-27).
The daemon's test suite generated these from a fake world, not user records.

`ask_stream_ui.txt` derives from `ask_stream.txt` by appending one explicitly
synthetic example.com reference to exercise browser opening. It is used by the
interactive fixture mode and loopback fixture server; the original is retained
for contract tests. Synthetic speech is a low-amplitude 440 Hz PCM16 tone.
