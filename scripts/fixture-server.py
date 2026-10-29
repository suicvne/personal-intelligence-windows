#!/usr/bin/env python3
"""Loopback-only synthetic daemon. No dependencies; no user data or real tokens."""
import argparse
import json
import math
from pathlib import Path
import struct
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

FIXTURES = Path(__file__).resolve().parents[1] / 'tests/Intel.Tests/Fixtures'
RATE = 24000
WAVE = (b'RIFF' + struct.pack('<I', 0xffffffff) + b'WAVEfmt ' +
        struct.pack('<IHHIIHH', 16, 1, 1, RATE, RATE * 2, 2, 16) + b'data' + struct.pack('<I', 0xffffffff) +
        b''.join(struct.pack('<h', int(math.sin(2 * math.pi * 440 * i / RATE) * 1800)) for i in range(RATE * 3)))

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        self.respond()
    def do_POST(self):
        self.rfile.read(int(self.headers.get('Content-Length', '0')))
        self.respond()
    def respond(self):
        if self.headers.get('Authorization') != 'Bearer fixture-token':
            self.send_response(401); self.send_header('Content-Type', 'application/json'); self.end_headers()
            self.wfile.write(b'{"error":"Expected fixture-token"}'); return
        if self.path == '/api/status':
            body, mime, size = (FIXTURES / 'status.json').read_bytes(), 'application/json', 4096
        elif self.path == '/api/ask/stream':
            body, mime, size = (FIXTURES / 'ask_stream_ui.txt').read_bytes(), 'text/event-stream', 80
        elif self.path == '/api/speak/stream':
            body, mime, size = WAVE, 'audio/wav', 2400
        else:
            self.send_error(404); return
        self.send_response(200); self.send_header('Content-Type', mime); self.end_headers()
        try:
            for offset in range(0, len(body), size):
                self.wfile.write(body[offset:offset + size]); self.wfile.flush(); time.sleep(.045)
        except (BrokenPipeError, ConnectionResetError):
            pass
    def log_message(self, format, *args):
        pass

if __name__ == '__main__':
    parser = argparse.ArgumentParser(); parser.add_argument('--port', type=int, default=18765)
    args = parser.parse_args()
    server = ThreadingHTTPServer(('127.0.0.1', args.port), Handler)
    print(f'Fixture daemon: http://127.0.0.1:{args.port} (token: fixture-token)', flush=True)
    try: server.serve_forever()
    except KeyboardInterrupt: server.server_close()
