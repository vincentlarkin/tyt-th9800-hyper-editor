# TH-9800 Hyper Studio

A focused Windows editor for the six Hyper Memory profiles in the TYT / TYTech TH-9800. It exposes the profile settings that the factory CPS treats as opaque: left and right memory channels, key beep, backlight level, and all four microphone key assignments.

The interface is built for day-to-day radio use: six clear A–F profile cards, friendly channel labels when a CHIRP CSV is available, quick mute and P3-scan actions, automatic backups, guarded writes, and a complete read-back verification after every radio write.

> **Experimental radio software:** Always make a verified read before editing. Keep the radio on stable 13.8 V power and do not disconnect the cable during a transfer. You are responsible for complying with your licence, local band plans, and transmit restrictions.

## Features

- Read all six Hyper Memory profiles from a TH-9800 clone image or directly from the radio
- Edit both displayed memory channels for profiles A through F
- Enable or disable the key beep independently for every profile
- Set backlight and P1–P4 behavior independently in every profile
- Choose from every documented microphone-key function, with an always-visible beginner reference
- Apply clear bulk actions or discard every staged edit back to the loaded image
- Import channel names from a standard CHIRP CSV (the CSV is never embedded in the saved image)
- Create a timestamped backup before every radio write
- Read the radio back and verify the exact SHA-256 after every write
- Reject files with the wrong size or missing `NT_9800` identifier
- Never silently alter settings outside the selected Hyper profile fields

## Requirements

- Windows 10 or 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- A programming cable whose driver creates a Windows COM port
- A TYT / TYTech TH-9800 compatible radio

## Run from source

```powershell
dotnet run --project .\src\TH9800.HyperEditor\TH9800.HyperEditor.csproj
```

For a Release build:

```powershell
dotnet build .\src\TH9800.HyperEditor\TH9800.HyperEditor.csproj -c Release
```

## Safe workflow

1. Connect the radio, select its COM port, and click **Read radio**.
2. Save that untouched read as a known-good backup.
3. Optionally import your CHIRP CSV so the channel selectors show names.
4. Edit A–F. Use **Save image as…** before writing if you want a portable copy.
5. Click **Write + verify**. Confirm the warning only after checking power and cable stability.
6. Wait for the post-write read-back verification to finish before touching the radio or cable.

The application deliberately writes a complete clone image because the TH-9800 clone protocol does not provide a documented atomic “write one Hyper setting” command. The pre-write backup and byte-for-byte post-write verification are therefore mandatory parts of its workflow.

## What is not in this repository

Radio clone images and personal channel plans can contain callsigns, location choices, and device-specific settings. This repository intentionally excludes `*.bin`, `*.img`, `*.dat`, `*.data`, CSV/spreadsheet files, logs, backups, and compiled output.

## Technical notes

The TH-9800 clone image is 65,296 bytes and starts with the ASCII model identifier `NT_9800`. The six 64-byte Hyper Memory blocks begin at image offset `0xDA90`, with A through F spaced by `0x40`. Within each block, microphone assignments P1 through P4 are stored consecutively at offsets `0x38` through `0x3B`. The code keeps these details isolated in `HyperImage`; tests use synthetic images only.

This project is independent community software and is not affiliated with TYT Electronics or CHIRP.

## License

[MIT](LICENSE)
