# CodexPort

Merge local Codex chats across Windows computers without copying configuration, accounts, or credentials.

CodexPort is a small open-source GUI with two actions:

- **Export chats**: closes Codex, creates a verified `.codexchat` package on the desktop, and selects it in File Explorer.
- **Import chats**: closes Codex, verifies the package, merges missing chats into the local library, then starts Codex again.

## Download

[Download CodexPort.exe v1.2.0](https://github.com/billowssun/CodexPort/releases/download/v1.2.0/CodexPort.exe)

Product page: [codexport.pddshop.cc](https://codexport.pddshop.cc)

## Merge behavior

- keeps every chat already on the destination computer
- adds chats missing from the destination
- skips identical chats
- when the same chat ID has different content, keeps both versions and labels the imported version as a copy
- preserves destination title, pin, and archive state for an identical chat
- rewrites local attachment and generated-image paths for the destination Windows user
- repeated imports are idempotent and do not create new copies again

To give both computers the complete combined library, export and import once in each direction.

## Included

- active and archived chats
- titles, pin/archive state, and local thread indexes
- chat attachments, generated images, and visualizations
- dynamic tool, parent/child task, and chat goal state

## Excluded

- account and login state
- API keys and authentication files
- `config.toml`
- plugins, skills, rules, automations, and memories

## Safety

- closes the official Codex Desktop process before database access
- allowlists package paths and rejects traversal, duplicates, oversized files, and abnormal compression ratios
- records and verifies SHA-256 for every package entry
- never imports remote-control enrollment or external-agent configuration
- merges only chat-related SQLite rows instead of replacing the destination database
- creates a unique backup before writing
- validates SQLite integrity and foreign keys after merging
- restores the backup automatically if importing fails

Chat contents can contain private information or secrets previously pasted by the user. Protect `.codexchat` packages like backups.

## Build and test

Requirements: Windows 10 or 11, Windows PowerShell 5.1, .NET Framework 4.8, and Python 3 for tests.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run.ps1
```

The single-file executable is written to `dist\CodexPort.exe`. The build compiles to a temporary artifact before atomically replacing the previous executable.

This independent community utility is not affiliated with or endorsed by OpenAI.
