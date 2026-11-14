#!/usr/bin/env python3
"""Assemble the static GitHub Pages build into dist/.

Runs `dotnet publish` on the WASM bridge, then lays the game files over the
AppBundle: index.html, adapter.js, data/*.json. Result is a fully static site —
the sim runs in the browser, saves live in localStorage.
"""
import glob
import os
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BUNDLE = os.path.join(
    ROOT, 'src', 'QuietHand.Wasm', 'bin', 'Release', 'net8.0',
    'browser-wasm', 'AppBundle')
DIST = os.path.join(ROOT, 'dist')


def main():
    print('== dotnet publish (browser-wasm) ==')
    r = subprocess.run(
        ['dotnet', 'publish', 'src/QuietHand.Wasm', '-c', 'Release', '-v', 'q'],
        cwd=ROOT)
    if r.returncode != 0:
        sys.exit('publish failed')

    if not os.path.isdir(BUNDLE):
        sys.exit(f'AppBundle not found at {BUNDLE}')

    # wipe dist without touching a possibly-locked dir handle: clear contents
    if os.path.isdir(DIST):
        for entry in os.listdir(DIST):
            p = os.path.join(DIST, entry)
            try:
                shutil.rmtree(p) if os.path.isdir(p) else os.remove(p)
            except PermissionError:
                print(f'  warn: locked {p}, overwriting instead')
    else:
        os.makedirs(DIST)

    shutil.copytree(BUNDLE, DIST, dirs_exist_ok=True)
    shutil.copy(os.path.join(ROOT, 'quiet-hand-ui.html'), os.path.join(DIST, 'index.html'))
    shutil.copy(os.path.join(ROOT, 'ui', 'adapter.js'), os.path.join(DIST, 'adapter.js'))
    shutil.copy(os.path.join(ROOT, 'data', 'ui_geo.json'), os.path.join(DIST, 'ui_geo.json'))
    os.makedirs(os.path.join(DIST, 'data'), exist_ok=True)
    for f in glob.glob(os.path.join(ROOT, 'data', '*.json')):
        if 'schema' not in f:
            shutil.copy(f, os.path.join(DIST, 'data'))

    # GitHub Pages runs Jekyll, which ignores _-prefixed paths like _framework/
    open(os.path.join(DIST, '.nojekyll'), 'w').close()
    for junk in ('package.json', 'QuietHand.Wasm.runtimeconfig.json'):
        p = os.path.join(DIST, junk)
        if os.path.exists(p):
            os.remove(p)

    total = sum(os.path.getsize(os.path.join(r, f)) for r, _, fs in os.walk(DIST) for f in fs)
    print(f'dist ready — {total / 1e6:.1f} MB')


if __name__ == '__main__':
    main()
