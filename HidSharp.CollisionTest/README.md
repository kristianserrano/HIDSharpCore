# HidSharp collision test

Checks the Windows fix: several HidSharp copies in **one process** must not collide on the
device-monitor window class (`HidSharp RegisterClass failed`).

It loads copies of HidSharp into separate `AssemblyLoadContext`s of one process:

- `stock`: HidSharp 2.1.0 from NuGet, the version the Logi Plugin SDK bundles
- `patched`: HidSharp built from this repository

The test app has no compile-time reference to either; it loads both by path at runtime.

## What you need

- Windows 10 or 11. The collision only exists on Windows, so results from macOS or Linux prove nothing.
- The .NET 8 SDK (`dotnet --version`)
- Internet access for the first build (NuGet restore of HidSharp 2.1.0)

## Run it

From a fresh clone on the Windows machine, on this branch:

```powershell
cd HidSharp.CollisionTest
dotnet build -c Release
powershell -ExecutionPolicy Bypass -File .\run-tests.ps1
```

You should see:

| Scenario | Expected |
|---|---|
| `patched` | PASS (exit 0) |
| `stock+patched` | PASS: the Logitech case, stock host copy plus one patched copy |
| `patched+stock` | PASS |
| `patched+patched` | PASS |
| `stock+stock` (control) | process crashes (exit -532462766): this is the bug |

If the `stock+stock` control does **not** crash, the machine did not reproduce the bug and the
PASS rows mean little.

Run a single scenario by hand:

```powershell
dotnet bin\Release\net8.0\HidSharp.CollisionTest.dll stock+patched
```

## Optional: hot-plug

Check that the monitor window actually works (the patched class name must still receive device
notifications). Give a number of seconds, then plug or unplug a USB HID device (mouse, keyboard)
in that time:

```powershell
dotnet bin\Release\net8.0\HidSharp.CollisionTest.dll patched 20
```

Expect at least one "device-change event". Exit code 3 means none arrived.

## Optional: exercise the init-failure path

To see the second fix turn a crash into a catchable exception, force a collision between two
patched copies:

1. In `HidSharp\Platform\Windows\WinHidManager.cs`, temporarily replace the `className` line
   with `const string className = "HidSharpDeviceMonitor";`
2. `dotnet build -c Release`, then `dotnet bin\Release\net8.0\HidSharp.CollisionTest.dll patched+patched`
3. Expect `[patched#2] CAUGHT TypeInitializationException ...`, "Process survived", exit code 2.
4. Undo the edit (`git checkout HidSharp\Platform\Windows\WinHidManager.cs`).

## Exit codes

| Code | Meaning |
|---|---|
| 0 | every copy initialized |
| 2 | process survived, but a copy failed to initialize (exception caught) |
| 3 | hot-plug requested, a copy saw no events |
| other | the process crashed (`-532462766` is an unhandled .NET exception) |
