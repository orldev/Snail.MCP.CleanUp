# The account: inventory, plan, apply

## <code>cleanup_<wbr>inventory</code>

Say what is on this machine that agents may have left: what is yours, what sessions that ended left,
caches, discoveries — new things no known kind covers — and how full the disk is. Changes nothing.

*read-only · repeat-safe · stays on this machine.*

Takes no parameters.

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {}
}
```

</details>

## <code>cleanup_<wbr>plan</code>

Decide a cleanup and show it before anything is done: what will be removed and why, what is only
reported because nothing proves it is an agent's, what is kept. Pass the planId to cleanup_apply
within 10 minutes; after that the plan is stale.

*read-only · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `scope` | `string` | — | `mine` | mine (this session's), orphans (left by ended sessions), caches (shared caches that rebuild themselves) or everything; mine by default. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "scope": {
      "description": "mine (this session\u0027s), orphans (left by ended sessions), caches (shared caches that rebuild themselves) or everything; mine by default.",
      "type": "string",
      "default": "mine"
    }
  }
}
```

</details>

## <code>cleanup_<wbr>apply</code>

Carry out the removals of a plan cleanup_plan showed, in its order. Each thing is checked again first and left alone if it changed.

*may overwrite or delete · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `planId` | `string` | yes | — | The planId cleanup_plan returned. |
| `only` | `array?` | — | `null` | Ids of the plan's removals to carry out; all of them when omitted. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "planId": {
      "description": "The planId cleanup_plan returned.",
      "type": "string"
    },
    "only": {
      "description": "Ids of the plan\u0027s removals to carry out; all of them when omitted.",
      "type": [
        "array",
        "null"
      ],
      "items": {
        "type": [
          "string",
          "null"
        ]
      },
      "default": null
    }
  },
  "required": [
    "planId"
  ]
}
```

</details>

## <code>cleanup_<wbr>release</code>

Release one thing now, such as a simulator or an emulator you acquired, if it is proven yours or an ended session's.

*may overwrite or delete · repeat-safe · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `id` | `string` | yes | — | Its id as cleanup_inventory printed it, such as simulator:&lt;udid&gt;. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "id": {
      "description": "Its id as cleanup_inventory printed it, such as simulator:\u003Cudid\u003E.",
      "type": "string"
    }
  },
  "required": [
    "id"
  ]
}
```

</details>

## <code>cleanup_<wbr>keep</code>

Ask for something to be left alone by every cleanup, with the reason, or withdraw that.

*only adds · repeat-safe · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `id` | `string` | yes | — | Its id as cleanup_inventory printed it. |
| `reason` | `string?` | — | `null` | Why it stays; required unless withdrawing. |
| `withdraw` | `boolean` | — | `false` | True to let cleanups have it again. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "id": {
      "description": "Its id as cleanup_inventory printed it.",
      "type": "string"
    },
    "reason": {
      "description": "Why it stays; required unless withdrawing.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "withdraw": {
      "description": "True to let cleanups have it again.",
      "type": "boolean",
      "default": false
    }
  },
  "required": [
    "id"
  ]
}
```

</details>

## <code>cleanup_<wbr>learn</code>

Turn a discovery into a known kind: a rule written to rules.json covering every trace like it from now
on. ignore says it is harmless, report shows it as a leftover, cache makes it a shared cache.

*only adds · repeat-safe · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `pattern` | `string` | yes | — | Keys it covers: * is one path segment, ** any number, ~ is home; such as ~/Library/Caches/tool/*. |
| `action` | `string` | yes | — | ignore, report or cache. |
| `trail` | `string` | — | `file` | The trail of the discovery, the part of its id before the colon; file by default. |
| `note` | `string?` | — | `null` | Why the rule exists, for the person who reads rules.json. |
| `blockedWhileRunning` | `string?` | — | `null` | A program that keeps what it covers open, such as Code; nothing covered is removed while it runs. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "pattern": {
      "description": "Keys it covers: * is one path segment, ** any number, ~ is home; such as ~/Library/Caches/tool/*.",
      "type": "string"
    },
    "action": {
      "description": "ignore, report or cache.",
      "type": "string"
    },
    "trail": {
      "description": "The trail of the discovery, the part of its id before the colon; file by default.",
      "type": "string",
      "default": "file"
    },
    "note": {
      "description": "Why the rule exists, for the person who reads rules.json.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "blockedWhileRunning": {
      "description": "A program that keeps what it covers open, such as Code; nothing covered is removed while it runs.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "pattern",
    "action"
  ]
}
```

</details>

