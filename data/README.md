# data (local only — contents are git-ignored)

- `inbox/` — drop 1-minute CSV files here; mounted into `xd-worker` and imported automatically.
- `exports/` — `exports/sim/<run-id>/` holds the output of a simulation run (`trades.csv`, `open_at_end.csv`, `run.json`, `README.txt`, `manifest.json`, and from P3-04c `index.html` and `cards/<nnnn>-<ticker>-<side>-<date>-<exit>.html`, one self-contained trade card each, capped by `--cards`), written by the `simulate` verb (`scripts/simulate.sh`, P3-04b2) through the export folder writer (P3-04b1) with owner-only permissions, under the root set by `Export__Directory` (the script sets it to this folder); the run id is `<strategy>-<from>-<to>-<12 hex of the run key>`. A different export under the same id is refused unless forced, which keeps the old folder once as `<run-id>.previous`; a `.staging-*` folder is the leftover of a killed run and is removed by hand. Nothing in the system reads an export back.
- `models/`, backups and database volumes are created at runtime.

Market data, trades and models never go into git.
