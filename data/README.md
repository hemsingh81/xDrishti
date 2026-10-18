# data (local only — contents are git-ignored)

- `inbox/` — drop 1-minute CSV files here; mounted into `xd-worker` and imported automatically.
- `exports/` — `exports/sim/<run-id>/` holds the output of a simulation run (`trades.csv`, `open_at_end.csv`, `run.json`, `README.txt`, `manifest.json`), written by the export folder writer (P3-04b1) with owner-only permissions; the run id is `<strategy>-<from>-<to>-<12 hex of the run key>`. A different export under the same id is refused unless forced, which keeps the old folder once as `<run-id>.previous`; a `.staging-*` folder is the leftover of a killed run and is removed by hand. Nothing in the system reads an export back.
- `models/`, backups and database volumes are created at runtime.

Market data, trades and models never go into git.
