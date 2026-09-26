"""Check evaluated restore/build artifacts, not package names in conditional XML."""
import argparse
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("database", choices=("postgres", "sqlserver", "oracle"))
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
expected = {"postgres": "Npgsql.EntityFrameworkCore.PostgreSQL", "sqlserver": "Microsoft.EntityFrameworkCore.SqlServer", "oracle": "Oracle.EntityFrameworkCore"}[args.database]
forbidden = {"postgres": ("microsoft.data.sqlclient", "microsoft.entityframeworkcore.sqlserver", "oracle"),
             "sqlserver": ("npgsql", "oracle"), "oracle": ("npgsql", "microsoft.data.sqlclient", "microsoft.entityframeworkcore.sqlserver")}[args.database]

for project in ("OpenBaseNET.Domain", "OpenBaseNET.Application", "OpenBaseNET.Infrastructure", "OpenBaseNET.Api"):
    assets = root / "src" / project / "obj" / args.database / "project.assets.json"
    libraries = json.loads(assets.read_text(encoding="utf-8"))["libraries"]
    packages = [name for name, details in libraries.items() if details["type"] == "package"]
    if project.endswith((".Domain", ".Application")):
        assert not packages, (project, packages)
    else:
        assert any(name.startswith(expected + "/") for name in packages), (project, expected)
    assert not any(name.lower().startswith(forbidden) for name in packages), (project, packages)

deps = root / "src/OpenBaseNET.Api/bin" / args.database / "Release/net10.0/OpenBaseNET.Api.deps.json"
runtime = json.loads(deps.read_text(encoding="utf-8"))["libraries"]
assert any(name.startswith(expected + "/") for name in runtime), expected
assert not any(name.lower().startswith(forbidden) for name in runtime), list(runtime)
print(f"{args.database}: only selected provider in restore/runtime dependencies; core has no NuGet packages")
