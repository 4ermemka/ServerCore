# Persistent network identities implementation plan

Goal: connect, synchronize patches and reconnect the same player across restarts with file-backed identities and manager-facing lifecycle events.

Retain ITransport, TCP framing, TrackableNode and the current Unity scene bindings. Separate transient connection IDs from persistent client credentials. Client writes a GUID and random reconnect credential before hello; server records its hash. Protocol v2 explicitly rejects older peers. Events are dispatched through Update. A new connection synchronizes a snapshot before sending local changes. Duplicate live identities and invalid credentials are rejected. Corrupt persistence fails without replacing the identity. Encryption and game authorization remain application concerns.

Work is performed in the existing checkout because both root and Shared contain required uncommitted application changes. Preserve these changes and do not commit unrelated work.

- [x] Reproduce handshake, reconnect and disconnected-send failures with real TCP tests.
- [x] Add identity stores, atomic persistence and handshake/lifecycle contracts.
- [x] Update GameServer routing and GameClient session/readiness/snapshot handling.
- [x] Correct TCP lifecycle and share packet reading.
- [x] Integrate persistence paths, events and serialized lifecycle into DmNetworkController.
- [x] Test persistence, protocol rejection, reconnect, patch delivery, snapshot ordering and shutdown; review changes.
- [x] Rename context file and update documentation and references.

Verification: 146/146 Unity EditMode tests passed (includes tests entering PlayMode); 21 networking tests. Separate-process TCP probe passed initial registration, client restart, server restart and patch delivery with the same persisted ID. Final source rebuild: 0 warnings/errors. Independent code review findings addressed and covered by regression tests. Initial headless RenderTexture failures disappeared when the full suite ran with graphics enabled. Changes remain uncommitted in the existing root/Shared workspaces.
