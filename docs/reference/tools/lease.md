# Simulators and emulators of your own

## <code>cleanup_<wbr>simulator_<wbr>acquire</code>

Get an iOS simulator of your own: a clone of a shut-down device, or a new device of a type, named for
this session so that no other agent uses it and only this session's cleanup removes it. Use it instead
of booting a simulator somebody may already be using, and drive it by the udid it returns.

*only adds · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `from` | `string?` | — | `null` | A shut-down device to clone, by name or udid, or a device type such as iPhone 17 Pro; the newest iPhone Pro when omitted. |
| `boot` | `boolean` | — | `false` | Whether to boot it and wait until it is ready; false by default. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "from": {
      "description": "A shut-down device to clone, by name or udid, or a device type such as iPhone 17 Pro; the newest iPhone Pro when omitted.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "boot": {
      "description": "Whether to boot it and wait until it is ready; false by default.",
      "type": "boolean",
      "default": false
    }
  }
}
```

</details>

## <code>cleanup_<wbr>emulator_<wbr>acquire</code>

Get an Android emulator of your own: a new virtual device from an installed system image, started on a
free console port without saving a snapshot, named for this session so that only its cleanup removes
it. It returns the adb serial at once; the device needs a minute or two to boot.

*only adds · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `image` | `string?` | — | `null` | Part of an installed system image path, such as android-37.0 or google_apis; the newest image without the Play Store when omitted. |
| `window` | `boolean` | — | `false` | Whether to show the emulator's window; false runs it headless. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "image": {
      "description": "Part of an installed system image path, such as android-37.0 or google_apis; the newest image without the Play Store when omitted.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "window": {
      "description": "Whether to show the emulator\u0027s window; false runs it headless.",
      "type": "boolean",
      "default": false
    }
  }
}
```

</details>

