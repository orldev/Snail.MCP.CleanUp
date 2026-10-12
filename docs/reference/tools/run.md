# Programs started on the record

## <code>cleanup_<wbr>run</code>

Start a program that keeps running — a dev server, a watcher, a worker — written down as yours from its
first second, so that cleaning up after this session ends it for certain and nothing of anyone else's.
It returns at once with the process id and a log file; read the log for its output. Prefer it to
starting such a program in the background from a shell.

*only adds · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `command` | `string` | yes | — | The whole command line, run by zsh, such as npm run dev -- --port 3000. |
| `directory` | `string?` | — | `null` | Directory to run it in; the server's own working directory when omitted. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "command": {
      "description": "The whole command line, run by zsh, such as npm run dev -- --port 3000.",
      "type": "string"
    },
    "directory": {
      "description": "Directory to run it in; the server\u0027s own working directory when omitted.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "command"
  ]
}
```

</details>

