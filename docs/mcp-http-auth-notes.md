# `txc-mcp` HTTP transport — auth design notes (v1, design-only)

**Status: design-only. No HTTP transport code ships in v1.**
This document records the design decisions that will apply when the MCP
HTTP/SSE transport is eventually added, so the current stdio
implementation does not paint us into a corner. It is intentionally
short on step-by-step how-to (that belongs in whichever PR lands the
transport) and heavy on invariants + forbidden patterns.

Baseline spec: [MCP 2025-06-18 specification](https://modelcontextprotocol.io/specification).

---

## 1. Role: resource server only

When `txc-mcp` exposes an HTTP/SSE transport, it will be an **OAuth 2.1
resource server** — **never** an authorization server.

- Authorization server = **Entra ID** (the user's tenant).
- Resource server = `txc-mcp`.
- `txc-mcp` does **not** expose `/authorize`, `/token`, `/device_code`,
  `/introspect`, or any other AS endpoint. If a client needs a token,
  it negotiates directly with Entra; `txc-mcp` only validates.

## 2. Discovery

Expose [RFC 9728 Protected Resource Metadata](https://datatracker.ietf.org/doc/html/rfc9728)
at a well-known URL:

```
GET /.well-known/oauth-protected-resource
```

Response MUST include:

- `resource` — canonical URI for this MCP server (e.g.
  `https://<host>/mcp`). Clients use this as the `resource` parameter
  ([RFC 8707](https://datatracker.ietf.org/doc/html/rfc8707)) when
  requesting audience-bound tokens.
- `authorization_servers` — list of Entra tenant issuers accepted.
- `bearer_methods_supported` — `["header"]` only. Tokens never in
  URIs or cookies.
- `scopes_supported` — the fine-grained scopes the server recognizes
  (TBD; current provider scope is `{resource}//.default` for
  Dataverse, but HTTP may slice finer).

## 3. Token validation

Every inbound request on the HTTP transport:

1. Must carry `Authorization: Bearer <jwt>`.
2. JWT MUST be validated:
   - `iss` matches one of the advertised `authorization_servers`,
   - `aud` equals the canonical `resource` URI (RFC 8707 audience
     binding; reject bearer tokens minted for other resources even
     if signed by the same tenant),
   - `exp` / `nbf` in window,
   - signature via JWKS fetched from the issuer's metadata.
3. On failure, respond `401 Unauthorized` with:

   ```
   WWW-Authenticate: Bearer resource_metadata="https://<host>/.well-known/oauth-protected-resource"
   ```

   so the client can discover the correct authorization server and
   retry with a properly audience-bound token.

## 4. Per-session state

- Each session is identified by `Mcp-Session-Id` (server-minted,
  cryptographically random; never the client's bearer jti).
- Client MUST include `MCP-Protocol-Version: 2025-06-18` on every
  request; reject with `400` if missing or unsupported.
- Session cache holds `(sessionId, profile) -> resolved credentials`
  (see §5). On `404 Session Not Found` — e.g. server restart, idle
  eviction — purge the cache entry immediately to prevent stale
  credential reuse across session re-issues.

## 5. Credential resolution with a per-call `profile` argument

The stdio transport already accepts an optional `profile` tool argument
on every `ProfiledCliCommand`-derived tool. HTTP keeps the same shape:

```
resolve(sessionId, profile) -> Credential
```

- `sessionId` identifies the user bound to the inbound bearer token.
- `profile` (optional) selects which stored `Profile` to use for this
  call.

This matters because a single MCP session may target multiple
downstream environments (e.g. customer-a-dev and customer-b-prod)
without re-authenticating to `txc-mcp`. The axis already exists in
v1; HTTP just makes `sessionId` non-constant.

## 6. Forbidden patterns (hard stops)

All of these are explicitly banned by the MCP spec and must be
rejected at code-review time, not debugged in prod:

| Anti-pattern | Why it's forbidden |
|---|---|
| **Token passthrough** — forwarding the client's inbound bearer directly to Dataverse or any downstream API. | Client bearer is audience-bound to `txc-mcp` (RFC 8707). Dataverse will reject it, and even if it didn't, it would let any leaf upstream bypass `txc-mcp`'s authorization entirely. Use on-behalf-of (OBO) flow or a separate service account. |
| **Tokens in URIs** — `?access_token=...`, path segments, etc. | Proxies and access logs leak URIs. `Authorization` header only. |
| **Non-HTTPS redirects** outside loopback. | Prevents MITM on OAuth redirects. Loopback `http://127.0.0.1:<port>/...` is allowed for local dev. |
| **Missing PKCE** on any OAuth flow. | Required by OAuth 2.1 for all public clients; we enforce for confidential clients too. |
| **Missing audience check** on inbound tokens. | Without it, any Entra-signed token for the same tenant would work — violates least privilege and opens lateral-movement vectors. |
| **Long-lived session cache of federation tokens** (ADO WIF, GitHub OIDC). | Those tokens are short-lived and non-refreshable. Cache MUST be keyed on `(tenantId, upn, profileId)` and re-acquired per call. |

## 7. Transport-security defaults

- Bind to `127.0.0.1` only by default. Explicit `--bind 0.0.0.0` (+ a
  `--yes-i-know-this-is-a-remote-bind` acknowledgement flag) to
  expose on LAN.
- Validate `Origin` on every request; reject mismatches with `403`.
- HSTS (`Strict-Transport-Security: max-age=31536000`) on all
  TLS-terminated responses.
- CORS: disabled by default. Browser-based MCP clients are an
  explicit opt-in per deployment.

## 8. Log redaction invariants carry over

Everything `JsonStderrLogger` + `LogRedactionFilter` already redact on
stdio (`Bearer <token>`, `Authorization:`, bare JWTs, connection-string
secret keys, URL query-param secrets) also applies when HTTP logs flow
through `McpLogForwarder`. Do not introduce a second logging sink that
bypasses the redaction filter.

## 9. What this design deliberately does NOT do (v1 and beyond)

- No custom token store on the server side. All tokens live in MSAL
  token cache (user machine) or the Entra AS (remote).
- No "refresh token rotation" on behalf of the client. Client handles
  its own refreshes; we only validate.
- No built-in rate limiter. Ship behind a reverse proxy that handles
  throttling.
- No OAuth device flow support. Device flow is for interactive
  end-user login; MCP HTTP is M2M/service-to-service.

---

## References

- [MCP 2025-06-18 spec](https://modelcontextprotocol.io/specification)
- [RFC 8707 — Resource Indicators for OAuth 2.0](https://datatracker.ietf.org/doc/html/rfc8707)
- [RFC 9728 — Protected Resource Metadata](https://datatracker.ietf.org/doc/html/rfc9728)
- [OAuth 2.1 draft](https://datatracker.ietf.org/doc/html/draft-ietf-oauth-v2-1)
- `temp/mcp-auth-research.md` — research notes that informed this design.
- `src/TALXIS.CLI.MCP/README.md` — stdio transport auth contract (already live).
