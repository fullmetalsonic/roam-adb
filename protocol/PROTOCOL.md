# RoamADB Gateway Protocol — Relay Spike v1

## Scope

This protocol proves Gateway registration, phone authentication, and a transparent ADB TCP relay. The Gateway defaults to loopback. The explicit `--tailnet` mode binds only to the exact IPv4 address returned by the local Tailscale CLI; wildcard listeners remain forbidden.

## Transport

- TLS 1.2 or TLS 1.3
- UTF-8 JSON objects delimited by LF
- maximum encoded message size: 65,536 bytes
- protocol version: `1`

The Android app pins the SHA-256 fingerprint supplied out of band by the Gateway registration screen. Tailnet reachability is not treated as authentication: certificate pinning and the registered phone challenge-response still apply. A public Internet listener is not implemented in this build.

## Registration

1. Gateway creates a six-digit code with a two-minute expiry.
2. Phone connects through pinned TLS.
3. Phone sends `register` with the code, device ID, display name, and ECDSA P-256 SubjectPublicKeyInfo.
4. Gateway consumes the code once and stores the public key.
5. Reuse, expiry, malformed keys, and excessive attempts are rejected.

The Windows GUI can encode the same out-of-band values as a QR URI:

```text
roamadb://register?v=1&host=<tailnet-ip>&port=47156&fingerprint=<sha256-hex>&code=<6-digits>&mode=existing-vpn-adb-only&expires=<unix-seconds>
```

The Android parser accepts only the exact scheme, version, field set, Tailscale IPv4 range, SHA-256 fingerprint, six-digit code, supported mode, and short future expiry. The QR contains no private key or Tailscale credential.

## Authentication

1. Phone sends `hello` with its registered device ID.
2. Gateway returns a 32-byte random `challenge` nonce.
3. Phone signs the raw nonce using ECDSA P-256 with SHA-256.
4. Gateway verifies the DER-encoded signature against the registered public key.
5. Only then does Gateway return `authenticated`.

## Relay publication

After authentication, the phone can publish one relay on the authenticated TLS session.

1. Phone sends `publish_relay` with `relayKind` equal to `connect` or `pairing`.
2. Gateway opens a loopback-only listener. Defaults are `127.0.0.1:47157` for `connect` and `127.0.0.1:47158` for `pairing`.
3. Gateway returns `relay_published` with the actual `relayPort`.
4. The PC's standard `adb.exe` connects to that loopback port.
5. Gateway sends `relay_start` to the phone.
6. The phone connects only to its own `127.0.0.1:<wireless-adb-port>` and returns `relay_ready`.
7. Immediately after the LF ending `relay_ready`, the TLS session changes to transparent raw-byte mode.
8. The relay ends when either TCP endpoint closes. Reconnection uses a new authenticated session.

The Gateway never receives the phone's local adbd port and cannot request an arbitrary phone-side host. The Android implementation fixes the target address to the phone's loopback interface.

### PC commands

```powershell
# A PC already trusted by Android uses the persistent ADB TLS connect relay.
adb connect 127.0.0.1:47157

# First-time Wireless debugging pairing uses the temporary pairing relay.
adb pair 127.0.0.1:47158 <code-shown-by-Android>
```

The Android UI can publish either relay. The user manually enters Android's temporary pairing port for `pairing`, or saves the normal Wireless debugging connect port for `connect`. Both targets remain fixed to Android loopback. First-time real-device pairing remains a field-validation item.

## Raw-mode rules

- Raw mode is byte transparent; RoamADB does not parse, terminate, or reimplement the ADB protocol.
- ADB Wi-Fi pairing, PC RSA key ownership, and ADB TLS authentication remain between the PC's official `adb.exe` and Android `adbd`.
- JSON is forbidden after `relay_ready` on that TLS session.
- One authenticated session carries one TCP relay. This avoids multiplexing ambiguities in the spike and is sufficient for one long-lived ADB transport.
- Maximum JSON message size remains 65,536 bytes; raw traffic is streamed with bounded copy buffers and is not retained.

## Security boundary

- Registration codes are stored as SHA-256 digests, never as plaintext.
- A code is not a permanent credential.
- The Gateway certificate fingerprint and phone public key are separate trust anchors.
- No password login is exposed by this protocol.
- Unknown message types, oversized frames, stale challenges, and invalid signatures fail closed.
- Local relay listeners do not exist until an authenticated phone publishes them.
- Tailnet mode requires no router forwarding and does not alter Windows Firewall automatically.
- The local ADB relay ports and ADB port 5555 must never be exposed through the router or a wildcard listener.
- ADB payloads, commands, files, and screen data are not logged by the relay.
