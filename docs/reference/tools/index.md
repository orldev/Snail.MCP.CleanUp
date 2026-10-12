# Tools

11 tools in 5 families.
The names, descriptions and schemas are generated from the server itself, so they are exactly what the model receives in `tools/list`.

Look, decide, then act: `cleanup_inventory` says what is there and whose, `cleanup_plan` decides a cleanup and shows it, `cleanup_apply` carries out exactly that plan. Something merely seen appearing during a session is reported, never removed.

| Tool | Family | What it does |
| --- | --- | --- |
| [`cleanup_inventory`](account.md#cleanup_inventory) | The account: inventory, plan, apply | Say what is on this machine that agents may have left: what is yours, what sessions that ended left, caches, discoveries — new things no known kind covers — … |
| [`cleanup_plan`](account.md#cleanup_plan) | The account: inventory, plan, apply | Decide a cleanup and show it before anything is done: what will be removed and why, what is only reported because nothing proves it is an agent's, what is kept. |
| [`cleanup_apply`](account.md#cleanup_apply) | The account: inventory, plan, apply | Carry out the removals of a plan cleanup_plan showed, in its order. |
| [`cleanup_release`](account.md#cleanup_release) | The account: inventory, plan, apply | Release one thing now, such as a simulator or an emulator you acquired, if it is proven yours or an ended session's. |
| [`cleanup_keep`](account.md#cleanup_keep) | The account: inventory, plan, apply | Ask for something to be left alone by every cleanup, with the reason, or withdraw that. |
| [`cleanup_learn`](account.md#cleanup_learn) | The account: inventory, plan, apply | Turn a discovery into a known kind: a rule written to rules.json covering every trace like it from now on. |
| [`cleanup_simulator_acquire`](lease.md#cleanup_simulator_acquire) | Simulators and emulators of your own | Get an iOS simulator of your own: a clone of a shut-down device, or a new device of a type, named for this session so that no other agent uses it and only th… |
| [`cleanup_emulator_acquire`](lease.md#cleanup_emulator_acquire) | Simulators and emulators of your own | Get an Android emulator of your own: a new virtual device from an installed system image, started on a free console port without saving a snapshot, named for… |
| [`cleanup_run`](run.md#cleanup_run) | Programs started on the record | Start a program that keeps running — a dev server, a watcher, a worker — written down as yours from its first second, so that cleaning up after this session … |
| [`find_tool`](discovery.md#find_tool) | Finding a tool | Find which of this server's tools does a thing, described in your own words, with every argument each one takes. |
| [`cleanup_diagnose`](diagnostics.md#cleanup_diagnose) | Diagnostics | Say what state this server is in: its version, the session it counts as yours, and which of the programs it works through (simctl, adb, docker) it can reach. |
