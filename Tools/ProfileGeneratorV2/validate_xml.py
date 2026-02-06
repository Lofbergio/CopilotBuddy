#!/usr/bin/env python3
"""Validate all XML files in output_v2 directory."""
from xml.etree import ElementTree as ET
from pathlib import Path
import sys

print("Starting validation...", flush=True)

errors = []
files = list(Path('output_v2').rglob('*.xml'))
print(f"Found {len(files)} files to check", flush=True)

for p in files:
    try:
        ET.parse(p)
    except Exception as e:
        errors.append((p, str(e)))

if errors:
    print(f"\nFound {len(errors)} XML errors:")
    for path, error in errors:
        print(f"  {path}: {error}")
    sys.exit(1)
else:
    print(f"\nAll {len(files)} XML files are valid!")
    sys.exit(0)
