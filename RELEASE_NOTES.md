## CodexPort v1.2.0

CodexPort is an open-source Windows utility for incrementally merging chat libraries across computers that both already contain chats.

- keeps the destination library intact and adds missing chats
- skips identical chats
- preserves both versions when the same chat ID has different content
- rewrites attachment and generated-image paths for the destination Windows user
- merges chat-related state and goal database rows without replacing the destination database
- creates a unique backup before writing and restores it on failure
- validates package hashes, paths, file counts, expanded size, SQLite integrity, and foreign keys
- repeated imports are idempotent

Configuration, accounts, credentials, plugins, skills, rules, automations, and memories are still excluded.
