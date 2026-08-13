# CodexPort

Move local Codex chats between Windows computers without copying configuration or credentials.

CodexPort is a small Windows GUI utility with two actions:

- **Export chats** — automatically closes Codex, creates a verified `.codexchat` package on the desktop, and selects it in File Explorer.
- **Import chats** — automatically closes Codex, verifies and imports the selected package, then starts Codex again.

## Download

Download the latest compiled Windows executable from [GitHub Releases](../../releases/latest).

## What it moves

- active and archived local chats
- titles, pin/archive state, and thread indexes
- chat attachments
- generated images and visualizations
- chat goal state

## What it excludes

- account tokens and authentication files
- `config.toml`
- secrets
- plugins and skills
- rules, automations, and memories

## Safety

- closes the official Codex Desktop process before database access
- exports allowlisted chat files only
- records and verifies SHA-256 for every package entry
- removes remote-control enrollment and external-agent configuration records from the database snapshot
- backs up the destination before importing
- rolls back automatically if an import fails
- refuses to overwrite a destination that already contains chats
- blocks path traversal and unexpected package content

Chat contents can contain private information or secrets previously pasted by the user. Protect `.codexchat` packages like backups.

## Build

Requirements:

- Windows 10 or 11
- Windows PowerShell 5.1
- .NET Framework 4.8

Run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

The executable is written to `dist\CodexPort.exe`.

## Current limitation

CodexPort does not merge two different existing chat libraries. Import into a newly initialized Codex installation that does not yet contain chats.

This is an independent community utility and is not affiliated with or endorsed by OpenAI.
