import json
import os
import sqlite3
import sys
from pathlib import Path


def write_jsonl(home: Path, thread_id: str, message: str, archived=False, asset=False):
    folder = home / ("archived_sessions" if archived else "sessions") / "2026" / "08" / "13"
    folder.mkdir(parents=True, exist_ok=True)
    path = folder / f"rollout-2026-08-13T00-00-00-{thread_id}.jsonl"
    rows = [
        {"timestamp": "2026-08-13T00:00:00Z", "type": "session_meta", "payload": {"id": thread_id, "session_id": thread_id}},
        {"timestamp": "2026-08-13T00:00:01Z", "type": "response_item", "payload": {"thread_id": thread_id, "message": message}},
    ]
    if asset:
        old = str(home / "generated_images" / thread_id / "poster.png")
        rows.append({"type": "response_item", "payload": {"local_images": [old], "content": [{"text": f"image at {old}"}]}})
        asset_path = home / "generated_images" / thread_id / "poster.png"
        asset_path.parent.mkdir(parents=True, exist_ok=True)
        asset_path.write_bytes(b"fixture-image")
    path.write_text("".join(json.dumps(r, ensure_ascii=False) + "\n" for r in rows), encoding="utf-8")
    return path


def state_db(home: Path, threads):
    db = sqlite3.connect(home / "state_5.sqlite")
    db.executescript("""
    PRAGMA foreign_keys=ON;
    CREATE TABLE thread_sections(id TEXT PRIMARY KEY, name TEXT NOT NULL);
    CREATE TABLE threads(id TEXT PRIMARY KEY, rollout_path TEXT NOT NULL, title TEXT, archived INTEGER NOT NULL DEFAULT 0, thread_section_id TEXT, is_pinned INTEGER NOT NULL DEFAULT 0, FOREIGN KEY(thread_section_id) REFERENCES thread_sections(id));
    CREATE TABLE thread_dynamic_tools(thread_id TEXT NOT NULL, position INTEGER NOT NULL, name TEXT, PRIMARY KEY(thread_id, position), FOREIGN KEY(thread_id) REFERENCES threads(id));
    CREATE TABLE thread_spawn_edges(parent_thread_id TEXT, child_thread_id TEXT PRIMARY KEY, status TEXT, FOREIGN KEY(child_thread_id) REFERENCES threads(id));
    CREATE TABLE remote_control_enrollments(id TEXT);
    """)
    db.execute("INSERT INTO thread_sections VALUES('section','Inbox')")
    for t in threads:
        db.execute("INSERT INTO threads VALUES(?,?,?,?,?,?)", (t["id"], str(t["path"]), t["title"], int(t.get("archived", False)), "section", int(t.get("pinned", False))))
        db.execute("INSERT INTO thread_dynamic_tools VALUES(?,?,?)", (t["id"], 0, "fixture_tool"))
    if len(threads) > 1:
        db.execute("INSERT INTO thread_spawn_edges VALUES(?,?,?)", (threads[0]["id"], threads[1]["id"], "ready"))
    db.commit()
    db.close()


def goals_db(home: Path, threads):
    db = sqlite3.connect(home / "goals_1.sqlite")
    db.executescript("""
    CREATE TABLE thread_goals(thread_id TEXT PRIMARY KEY, objective TEXT);
    CREATE TABLE thread_goal_continuation_deferrals(thread_id TEXT PRIMARY KEY);
    """)
    for t in threads:
        db.execute("INSERT INTO thread_goals VALUES(?,?)", (t["id"], "fixture goal"))
        db.execute("INSERT INTO thread_goal_continuation_deferrals VALUES(?)", (t["id"],))
    db.commit()
    db.close()


def make_home(root: Path, name: str, specs):
    home = root / name / ".codex"
    home.mkdir(parents=True, exist_ok=True)
    threads = []
    for spec in specs:
        path = write_jsonl(home, spec["id"], spec["message"], spec.get("archived", False), spec.get("asset", False))
        threads.append({**spec, "path": path})
    state_db(home, threads)
    goals_db(home, threads)
    with (home / "session_index.jsonl").open("w", encoding="utf-8") as f:
        for spec in specs:
            f.write(json.dumps({"id": spec["id"], "thread_name": spec["title"]}, ensure_ascii=False) + "\n")
    return home


def create(root: Path):
    shared = "11111111-1111-4111-8111-111111111111"
    source_only = "22222222-2222-4222-8222-222222222222"
    target_only = "33333333-3333-4333-8333-333333333333"
    source = make_home(root, "OldUser", [
        {"id": shared, "message": "source divergent", "title": "Source version", "asset": True},
        {"id": source_only, "message": "source only", "title": "Source only", "archived": True},
    ])
    target = make_home(root, "NewUser", [
        {"id": shared, "message": "target divergent", "title": "Target version", "pinned": True},
        {"id": target_only, "message": "target only", "title": "Target only"},
    ])
    print(json.dumps({"source": str(source), "target": str(target), "shared": shared, "source_only": source_only, "target_only": target_only}))


def verify(root: Path, data):
    target = Path(data["target"])
    files = list((target / "sessions").rglob("*.jsonl")) + list((target / "archived_sessions").rglob("*.jsonl"))
    assert len(files) == 4, f"expected 4 chats, got {len(files)}"
    canonical = {}
    for p in files:
        rows = [json.loads(x) for x in p.read_text(encoding="utf-8").splitlines() if x.strip()]
        tid = rows[0]["payload"]["id"]
        canonical[tid] = (p, rows)
    assert data["shared"] in canonical and data["target_only"] in canonical and data["source_only"] in canonical
    conflict = next(x for x in canonical if x not in {data["shared"], data["target_only"], data["source_only"]})
    conflict_rows = canonical[conflict][1]
    serialized = json.dumps(conflict_rows, ensure_ascii=False)
    imported_image = conflict_rows[2]["payload"]["local_images"][0]
    assert imported_image.startswith(str(target)) and "OldUser" not in imported_image, imported_image
    assert conflict in serialized
    assert (target / "generated_images" / conflict / "poster.png").exists()
    db = sqlite3.connect(target / "state_5.sqlite")
    ids = {r[0] for r in db.execute("SELECT id FROM threads")}
    assert ids == set(canonical)
    paths = list(db.execute("SELECT id, rollout_path FROM threads"))
    assert all(str(target) in p and Path(p).exists() for _, p in paths)
    assert db.execute("SELECT is_pinned FROM threads WHERE id=?", (data["shared"],)).fetchone()[0] == 1
    assert db.execute("PRAGMA integrity_check").fetchone()[0] == "ok"
    assert list(db.execute("PRAGMA foreign_key_check")) == []
    assert db.execute("SELECT COUNT(*) FROM thread_dynamic_tools").fetchone()[0] == 4
    assert db.execute("SELECT COUNT(*) FROM thread_spawn_edges").fetchone()[0] >= 1
    db.close()
    goals = sqlite3.connect(target / "goals_1.sqlite")
    assert goals.execute("SELECT COUNT(*) FROM thread_goals").fetchone()[0] == 4
    assert goals.execute("SELECT COUNT(*) FROM thread_goal_continuation_deferrals").fetchone()[0] == 4
    goals.close()
    print(json.dumps({"threads": len(files), "conflict_id": conflict, "status": "ok"}))


if __name__ == "__main__":
    mode, root = sys.argv[1], Path(sys.argv[2]).resolve()
    if mode == "create": create(root)
    else: verify(root, json.loads((root / "fixture.json").read_text(encoding="utf-8-sig")))
