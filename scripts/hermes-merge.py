"""Merges answers.jsonl with a Hermes session export into answer lines that carry tool_results (P9-10). Mirrors the domain class HermesAnswerImporter;
the export shape is UNVERIFIED: {"sessions":[{"id","tool_calls":[{"name","arguments","result"}]}]}; answers name their session in "session_ref". Run with python3 -I."""
import json
import sys

answers_path, export_path = sys.argv[1], sys.argv[2]
sessions = {s["id"]: s["tool_calls"] for s in json.load(open(export_path))["sessions"]}
for line in open(answers_path):
    if not line.strip():
        continue
    row = json.loads(line)
    calls = sessions.get(row.pop("session_ref", None) or "", [])
    row.pop("tool_results", None)
    if calls:
        row["tool_results"] = [c["result"] if isinstance(c.get("result"), str) else json.dumps(c.get("result")) for c in calls]
    print(json.dumps(row))
