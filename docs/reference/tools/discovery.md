# Finding a tool

## <code>find_<wbr>tool</code>

Find which of this server's tools does a thing, described in your own words, with every argument each one takes.

*read-only · repeat-safe · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `task` | `string` | yes | — | What you want to do, such as «free the port my dev server held». |
| `limit` | `integer` | — | `5` | How many tools to return at most; 5 by default. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "task": {
      "description": "What you want to do, such as \u00ABfree the port my dev server held\u00BB.",
      "type": "string"
    },
    "limit": {
      "description": "How many tools to return at most; 5 by default.",
      "type": "integer",
      "default": 5
    }
  },
  "required": [
    "task"
  ]
}
```

</details>

