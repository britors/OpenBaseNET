"""Scaffold in a disposable CI checkout and prove other providers are untouched."""
import os
from pathlib import Path
import subprocess

if os.environ.get("CI", "").lower() != "true":
    raise SystemExit("Run this check in a disposable CI checkout; it creates an unapplied probe migration.")
database = os.environ.get("OpenBaseDatabase", "postgres")
providers = {"postgres": ("Postgres", "PostgresModelSnapshot"), "sqlserver": ("SqlServer", "SqlServerModelSnapshot")}
folder, snapshot = providers[database]
root = Path(__file__).resolve().parents[1]
persistence = root / "src/OpenBaseNET.Infrastructure/Persistence"
before = {p: p.read_bytes() for p in persistence.rglob("*.cs")}
subprocess.run(["dotnet", "ef", "migrations", "add", "IsolationProbe", "--project", "src/OpenBaseNET.Infrastructure",
                "--output-dir", f"Persistence/{folder}/Migrations", "--configuration", "Release", "--no-build"],
               cwd=root, check=True)
for path, original in before.items():
    if folder not in path.relative_to(persistence).parts:
        assert path.read_bytes() == original, f"Scaffolding changed another provider: {path}"
assert (persistence / folder / "Migrations" / (snapshot + ".cs")).is_file()
assert not list(persistence.rglob("OpenBaseDbContextModelSnapshot.cs")), "Ambiguous default snapshot name reintroduced"
new_files = [p for p in persistence.rglob("*.cs") if p not in before]
assert len(new_files) == 2, new_files  # migration + designer; reuse selected snapshot
assert all(folder in p.relative_to(persistence).parts for p in new_files), new_files
print(f"{database}: scaffolding reuses {snapshot} and preserves all other providers")
