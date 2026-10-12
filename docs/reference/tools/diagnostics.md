# Diagnostics

## <code>cleanup_<wbr>diagnose</code>

Say what state this server is in: its version, the session it counts as yours, and which of the
programs it works through (simctl, adb, docker) it can reach. Call it when another tool reports that
something is unavailable, not before every task.

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

