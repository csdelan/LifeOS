#!/usr/bin/env python3
"""Prove the bsk_reader read-only door from Python, with no .NET involved.

Connects to Postgres as the ``bsk_reader`` role, reads subjects and events
(through both the base tables and the flattened ``v_`` views), then confirms a
write is rejected. Exits non-zero if any read fails or if a write is allowed.

Connection settings come from the ``BSK_READER_DSN`` environment variable when
set, otherwise from the local-development defaults that match
``docker-compose.yml`` and migration 0005.

    pip install "psycopg[binary]"
    python scripts/verify_reader.py
"""

from __future__ import annotations

import os
import sys

import psycopg

DEFAULT_DSN = "host=localhost port=5432 dbname=lifeos user=bsk_reader password=bsk_reader"

READABLE = [
    "bsk.subject",
    "bsk.event",
    "bsk.subject_relation",
    "bsk.subject_event",
    "bsk_derived.subject_current",
    "bsk.v_subject",
    "bsk.v_event",
    "bsk.v_subject_relation",
    "bsk.v_subject_event",
    "bsk.v_subject_current",
]


def main() -> int:
    dsn = os.environ.get("BSK_READER_DSN", DEFAULT_DSN)

    with psycopg.connect(dsn) as conn, conn.cursor() as cur:
        cur.execute("SELECT current_user;")
        who = cur.fetchone()[0]
        print(f"connected as {who}")
        if who != "bsk_reader":
            print(f"ERROR: expected to be bsk_reader, got {who}", file=sys.stderr)
            return 1

        for relation in READABLE:
            cur.execute(f"SELECT count(*) FROM {relation};")
            count = cur.fetchone()[0]
            print(f"  read {relation}: {count} row(s)")

        # A write must be rejected.
        try:
            cur.execute(
                "INSERT INTO bsk.subject (urn, type, title) "
                "VALUES ('urn:bsk:task:python-should-fail', 'Task', 'nope');"
            )
        except psycopg.errors.InsufficientPrivilege:
            conn.rollback()
            print("  write correctly rejected (insufficient privilege)")
        else:
            print("ERROR: bsk_reader was able to INSERT — role is not read-only", file=sys.stderr)
            return 1

    print("OK: bsk_reader can read and cannot write")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
