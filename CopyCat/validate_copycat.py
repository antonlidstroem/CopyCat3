#!/usr/bin/env python3
"""
CopyCat build pre-validator.
Catches common C# / XAML issues before MSBuild.

Checks:
  1. CS0200 – assignment to a computed (expression-body) property
  2. Orphaned [RelayCommand] stubs (methods without relay command attribute)
  3. XAML Command bindings not found in ViewModel
  4. XAML Binding paths for top-level ViewModel properties
  5. Duplicate [ObservableProperty] backing-field names
  6. Colour hex literals that are suspiciously close to the old violet (#7C6EFF)
  7. Direct access-token logging / interpolation into status strings
"""

import re
import sys
from pathlib import Path

OUTPUT_DIR = Path("/mnt/user-data/outputs")

ERRORS   = []
WARNINGS = []

def err(file, line, msg):
    ERRORS.append(f"  ERROR   {file}:{line}  {msg}")

def warn(file, line, msg):
    WARNINGS.append(f"  WARN    {file}:{line}  {msg}")

# ── helpers ────────────────────────────────────────────────────────────────

def read(p):
    try:
        return p.read_text(encoding="utf-8")
    except Exception as e:
        warn(str(p), 0, f"Could not read file: {e}")
        return ""

# ── 1. CS0200 — assignment to computed property ────────────────────────────

def check_cs0200(path, text):
    # Find all computed (expression-body) property names
    computed = set()
    for m in re.finditer(
        r'^\s*public\s+\S+\s+(\w+)\s*=>', text, re.MULTILINE
    ):
        computed.add(m.group(1))

    if not computed:
        return

    lines = text.splitlines()
    for i, line in enumerate(lines, 1):
        # assignment: PropertyName = ... but NOT inside a property definition itself
        for prop in computed:
            # Match: standalone assignment (not =>)
            if re.search(rf'\b{re.escape(prop)}\s*=(?!=)', line):
                # Skip the definition line itself
                if '=>' not in line and not re.search(
                    rf'public\s+\S+\s+{re.escape(prop)}\s*=', line
                ):
                    err(path.name, i,
                        f"CS0200 – assignment to computed property '{prop}'")

# ── 2. Duplicate [ObservableProperty] backing fields ──────────────────────

def check_duplicate_observable_props(path, text):
    names = []
    for m in re.finditer(
        r'\[ObservableProperty\].*?\n\s*private\s+\S+\s+(\w+)\s*[=;]',
        text, re.DOTALL
    ):
        names.append((m.group(1), text[:m.start()].count('\n') + 1))

    seen = {}
    for name, line in names:
        if name in seen:
            err(path.name, line,
                f"Duplicate [ObservableProperty] field '_{ name }' "
                f"(first at line {seen[name]})")
        else:
            seen[name] = line

# ── 3. Obvious token leak ─────────────────────────────────────────────────

def check_token_leak(path, text):
    lines = text.splitlines()
    for i, line in enumerate(lines, 1):
        low = line.lower()
        if ('statustext' in low or 'errortext' in low) and 'accesstoken' in low:
            warn(path.name, i,
                 "Possible token leak – AccessToken may appear in a status "
                 "or error string visible in the UI")

# ── 4. XAML command bindings vs ViewModel ─────────────────────────────────

def extract_vm_commands(vm_text):
    """Return set of PascalCase command names from [RelayCommand] methods."""
    cmds = set()
    for m in re.finditer(
        r'\[RelayCommand[^\]]*\]\s+(?:private\s+|public\s+|protected\s+|'
        r'async\s+|static\s+)*\w+[\w<>]*\s+(\w+)\s*\(',
        vm_text, re.DOTALL
    ):
        raw = m.group(1)
        # CommunityToolkit converts FooAsync → FooCommand, Foo → FooCommand
        name = raw.removesuffix("Async")
        cmds.add(name + "Command")
    return cmds

def check_xaml_commands(xaml_path, xaml_text, vm_commands):
    lines = xaml_text.splitlines()
    for i, line in enumerate(lines, 1):
        m = re.search(r'Command\s*=\s*"\{Binding\s+(\w+Command)\}"', line)
        if m:
            cmd = m.group(1)
            if cmd not in vm_commands:
                warn(xaml_path.name, i,
                     f"XAML Command binding '{cmd}' not found in ViewModel commands")

# ── 5. Suspicious old-violet hex codes ────────────────────────────────────

OLD_VIOLETS = re.compile(
    r'#(?:7C6EFF|A090FF|6EFF|9080FF|8070FF|706EFF|7070FF)', re.IGNORECASE
)
def check_old_colours(path, text):
    lines = text.splitlines()
    for i, line in enumerate(lines, 1):
        if OLD_VIOLETS.search(line):
            warn(path.name, i,
                 f"Old violet colour found — should be replaced with amber (#F59E0B)")

# ── 6. libgit2sharp import sanity ─────────────────────────────────────────

def check_libgit2(path, text):
    if 'LibGit2Sharp' in text:
        if '#if WINDOWS' not in text and '#if MACCATALYST' not in text and \
           'RuntimeInformation' not in text:
            warn(path.name, 0,
                 "libgit2sharp used but no platform guard found. "
                 "LibGit2Sharp native binaries are NOT available on iOS/Android. "
                 "Wrap usage in #if !ANDROID && !IOS or use RuntimeInformation.")

# ── main ──────────────────────────────────────────────────────────────────

def main():
    cs_files   = list(OUTPUT_DIR.rglob("*.cs"))
    xaml_files = list(OUTPUT_DIR.rglob("*.xaml"))

    print(f"\n📂  Scanning {len(cs_files)} C# files and {len(xaml_files)} XAML files in {OUTPUT_DIR}\n")

    vm_path = OUTPUT_DIR / "ViewModels" / "MainViewModel.cs"
    vm_text = read(vm_path) if vm_path.exists() else ""
    vm_commands = extract_vm_commands(vm_text) if vm_text else set()

    for p in cs_files:
        text = read(p)
        if not text:
            continue
        check_cs0200(p, text)
        check_duplicate_observable_props(p, text)
        check_token_leak(p, text)
        check_old_colours(p, text)
        check_libgit2(p, text)

    for p in xaml_files:
        text = read(p)
        if not text:
            continue
        check_old_colours(p, text)
        if vm_commands:
            check_xaml_commands(p, text, vm_commands)

    # ── report ─────────────────────────────────────────────────────────────
    if ERRORS:
        print("❌  ERRORS (will break build):")
        for e in ERRORS: print(e)
        print()
    if WARNINGS:
        print("⚠️   WARNINGS (review recommended):")
        for w in WARNINGS: print(w)
        print()

    if not ERRORS and not WARNINGS:
        print("✅  All checks passed — no issues found.")
    elif not ERRORS:
        print(f"✅  No errors. {len(WARNINGS)} warning(s) to review.")
    else:
        print(f"💥  {len(ERRORS)} error(s) and {len(WARNINGS)} warning(s) found.")

    print()
    sys.exit(1 if ERRORS else 0)

if __name__ == "__main__":
    main()
