# Board files

Board files give sensors of a motherboard better names, or hide sensors that are not connected, without a new release of the app.

## Where they go

Put one or more `.json` files into `Documents\LibreHardwareMonitor\Boards`. The app reads them when it starts and after the computer wakes from sleep.

## Format

```json
{
  "board": "ROG STRIX X570-E GAMING",
  "sensors": {
    "/lpc/nct6798d/0/temperature/3": { "name": "Chipset" },
    "/lpc/nct6798d/0/voltage/7": { "hidden": true }
  }
}
```

- `board` must match the motherboard's SMBIOS product name exactly (case does not matter). The details pane shows it for the motherboard.
- The keys under `sensors` are sensor identifiers. The details pane shows the identifier of the selected sensor.
- `name` sets the name of the sensor (at most 64 characters). A name you set yourself with Rename always wins. This also holds for names stored by earlier versions of the app, even if you reset them to the default then; rename the sensor and clear the field once to let the board file name it again.
- `hidden` moves the sensor to the advanced values, which are shown only with "Advanced values".
- Comments and trailing commas are allowed. Several files may describe the same board; files later in alphabetical order win. A file that cannot be read, or that is larger than 1 MB, is skipped.

## What board files cannot do

Board files only name and hide sensors. They contain no registers, scaling or anything that changes the hardware, so a file from someone else cannot harm the computer.
