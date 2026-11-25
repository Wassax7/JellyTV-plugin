# Custom notification webhook regression tests

Run with the .NET 10 SDK:

```sh
dotnet run --project tests/JellyTV.Tests
```

This dependency-free executable test suite starts the real MVC route at an ephemeral
loopback address and posts custom notification arrays over HTTP. It uses an isolated
temporary plugin store, fake Jellyfin identity/user lookups and an in-memory
`HttpMessageHandler` for the relay. No external notifications are sent. Any failed
assertion exits nonzero; the host and temporary store are disposed on completion.

Checks include recipient overlap/aliases/shared tokens, distinct messages and order,
existing broadcast forms, unknown audiences, normalization/truncation, counters,
100/101/205-token limits, per-request scope, authentication and input validation.
It does not validate Jellyfin's authentication implementation, the deployed relay
or APNs delivery.
