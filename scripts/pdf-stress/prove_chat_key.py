"""Proof for the doc-chat keying fix (DocumentChatStore.KeyForPath).

Replicates BOTH key formulas exactly as the C# computes them, then edits a real
file the way Fill & Sign autosave / compress-overwrite do, and shows:

  OLD key (SummaryCache.FileMetaFingerprint = path|length|lastWriteUtc.Ticks)
      -> CHANGES on edit  => chat thread orphaned ("notes gone")
  NEW key (normalized path only)
      -> STABLE across edits => thread reattaches

Run: python scripts/pdf-stress/prove_chat_key.py
"""
import hashlib, os, tempfile, time, datetime

def sha_hex(s: str) -> str:
    return hashlib.sha256(s.encode("utf-8")).hexdigest()

def dotnet_ticks_utc(path: str) -> int:
    # .NET DateTime.Ticks = 100-ns intervals since 0001-01-01T00:00:00.
    mtime = os.path.getmtime(path)  # POSIX seconds (UTC epoch)
    dt = datetime.datetime(1970, 1, 1) + datetime.timedelta(seconds=mtime)
    epoch0001 = datetime.datetime(1, 1, 1)
    return int((dt - epoch0001).total_seconds() * 10_000_000)

def old_key(path: str) -> str:
    # C#: $"{filePath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}"
    length = os.path.getsize(path)
    return sha_hex(f"{path}|{length}|{dotnet_ticks_utc(path)}")

def new_key(path: str) -> str:
    # C#: SHA256("path:" + fullpath.Replace('/','\\').TrimEnd('\\').ToLowerInvariant())
    full = os.path.abspath(path).replace("/", "\\").rstrip("\\").lower()
    return sha_hex("path:" + full)

def edit_file(path: str, data: bytes):
    with open(path, "wb") as f:
        f.write(data)
    # Fill & Sign / compress rewrite the file → new length AND new mtime.
    time.sleep(1.1)  # guarantee a distinct mtime tick on any filesystem

def main():
    fd, path = tempfile.mkstemp(suffix="_form.pdf")
    os.close(fd)
    try:
        edit_file(path, b"%PDF-1.4 original referee form ...")
        o1, n1 = old_key(path), new_key(path)

        edit_file(path, b"%PDF-1.4 original referee form ... + jianhong signature baked")
        o2, n2 = old_key(path), new_key(path)

        edit_file(path, b"%PDF-1.4 ... + signature + LIM MEI LING typed into cell")
        o3, n3 = old_key(path), new_key(path)

        print("file:", path)
        print()
        print("OLD key (metadata: path|len|mtime) — the shipped bug")
        print("  after open  :", o1[:16])
        print("  after sign  :", o2[:16], "  <- CHANGED" if o2 != o1 else "  (same)")
        print("  after fill  :", o3[:16], "  <- CHANGED" if o3 != o2 else "  (same)")
        old_stable = (o1 == o2 == o3)
        print("  stable across edits:", old_stable, "=> chat/notes ORPHANED" if not old_stable else "")
        print()
        print("NEW key (normalized path only) — the fix")
        print("  after open  :", n1[:16])
        print("  after sign  :", n2[:16], "  (same)" if n2 == n1 else "  <- CHANGED")
        print("  after fill  :", n3[:16], "  (same)" if n3 == n2 else "  <- CHANGED")
        new_stable = (n1 == n2 == n3)
        print("  stable across edits:", new_stable, "=> chat/notes REATTACH" if new_stable else "")
        print()
        ok = (not old_stable) and new_stable
        print("RESULT:", "PASS — regression reproduced and fix holds" if ok
              else "INCONCLUSIVE (mtime granularity?)")
        return 0 if ok else 1
    finally:
        os.remove(path)

if __name__ == "__main__":
    raise SystemExit(main())
