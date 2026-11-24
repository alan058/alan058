# ADR-0003: Retry only when repeating the call cannot cause harm

* **Status:** Accepted

## Context

SQL Server calls fail occasionally for reasons that go away on their own: a
deadlock, a failover, a network blip. Retrying hides these from the user. But
retrying a *write* can be dangerous. If the connection drops after SQL Server
committed a new order but before the reply arrived, a retry creates the order
twice.

## Decision

`SqlExecutor` retries up to `MaxRetries` (default 3) times, with exponential
back-off and jitter, **only** when:

| Error | Retried | Why |
|---|---|---|
| 1205 deadlock victim | Always | SQL Server rolled the whole transaction back, so nothing happened |
| Connection errors (4060, 233, 10053, 10054, 10060, Azure 40xxx) | Only while the connection is still being **opened** | Nothing has been sent yet |
| Connection errors after the command was sent | No | The write may have committed |
| Command timeout (-2) | No | The command may still be running; retrying adds load to a server that is already slow. Timeouts are a signal to investigate, not to hide |

Anything not retried is translated to an application exception and reported.

## Consequences

* No duplicate orders or double stock movements from retries.
* Users may occasionally see "the database is busy, try again" when a write
  failed mid-flight. That is the honest answer, because only they can check
  whether it went through.
* `SqlErrorTranslator.IsSafeToRetry` holds the rule in one place, and the
  reasoning is in its comments and here.
