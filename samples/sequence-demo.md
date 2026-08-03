# Sequence demo

A short document with prose and several mermaid sequence diagrams, used for manual checks and by
the `build/verify` smoke test.

## A request/response exchange

```mermaid
sequenceDiagram
    participant C as Client
    participant S as Server
    participant D as Database
    C->>S: GET /report
    S->>D: SELECT rows
    D-->>S: result set
    S-->>C: 200 OK
```

## Notes, a self-message, and every arrow style

```mermaid
sequenceDiagram
    actor User
    participant Api as API gateway
    Note left of User: Starts in the browser
    User->>Api: submit form
    Api->>Api: validate payload
    Note over User,Api: The gateway retries once
    Api-->>User: 202 Accepted
    Api->User: open acknowledgement
    Api-xUser: dropped notification
```

## Numbered messages

```mermaid
sequenceDiagram
    autonumber
    participant A as Alice
    participant B as Bob
    A->>B: Hello Bob
    B-->>A: Hi Alice
    A->>B: Shall we start?
```

## A fragment, which degrades gracefully

`loop` and friends are not laid out yet: the frame is missing and one `MERMAID003` is reported,
but the messages inside still render in source order.

```mermaid
sequenceDiagram
    participant Poller
    participant Queue
    loop every minute
        Poller->>Queue: poll
        Queue-->>Poller: batch
    end
```

Every diagram is hand-written SVG: no JavaScript, no external requests, no fonts to install.
