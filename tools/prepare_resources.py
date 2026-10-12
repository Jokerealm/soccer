#!/usr/bin/env python3
"""Fetch shared art resources. Godot setup is historical and explicitly opt-in."""
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
import argparse
import hashlib
import json
import shutil
import subprocess
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
DOWNLOADS = ROOT / ".tools/downloads"
VERSION = "4.7.2"
ENGINE_BASE = f"https://github.com/godotengine/godot-builds/releases/download/{VERSION}-stable/"
JOBS = [
    ("godot", ENGINE_BASE + f"Godot_v{VERSION}-stable_macos.universal.zip", "c58a24e31d720be9d62f60cb5627c4e695fb72f21b0cfe1bc9ccaa9a3b3ba63e"),
    ("templates", ENGINE_BASE + f"Godot_v{VERSION}-stable_export_templates.tpz", "f298490b8d44d934be425a5a65a51bf15f422428b229a06a6e11d9ffea248011"),
    ("sports-pack", "https://kenney.nl/media/pages/assets/sports-pack/ede5629412-1677693282/kenney_sports-pack.zip", None),
    ("ui-pack", "https://kenney.nl/media/pages/assets/ui-pack/f651646eab-1718203990/kenney_ui-pack.zip", None),
    ("input-prompts", "https://kenney.nl/media/pages/assets/input-prompts/8de120163f-1783763952/kenney_input-prompts_1.5.zip", None),
    ("particle-pack", "https://kenney.nl/media/pages/assets/particle-pack/f8fe0f8cb8-1677578741/kenney_particle-pack.zip", None),
    ("font", "https://raw.githubusercontent.com/notofonts/noto-cjk/main/Sans/OTF/SimplifiedChinese/NotoSansCJKsc-Regular.otf", None),
    ("font-license", "https://raw.githubusercontent.com/notofonts/noto-cjk/main/Sans/LICENSE", None),
]

def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

def fetch(job):
    key, url, expected = job
    suffix = ".zip" if key not in ("templates", "font", "font-license") else {"templates": ".tpz", "font": ".otf", "font-license": ".txt"}[key]
    path = DOWNLOADS / (key + suffix)
    if not path.exists() or (expected and digest(path) != expected):
        part = path.with_suffix(path.suffix + ".part")
        print(f"Downloading {key}", flush=True)
        if key == "templates":
            total = 1281349702
            if part.exists() and part.stat().st_size > total:
                part.unlink()
            while not part.exists() or part.stat().st_size < total:
                start = part.stat().st_size if part.exists() else 0
                end = min(start + 64 * 1024 * 1024 - 1, total - 1)
                request = urllib.request.Request(url, headers={"User-Agent": "Soccer-Resource-Prep/1.0", "Range": f"bytes={start}-{end}"})
                with urllib.request.urlopen(request, timeout=120) as source:
                    if source.status != 206 or not source.headers.get("Content-Range", "").startswith(f"bytes {start}-"):
                        raise RuntimeError("Server did not honor range request")
                    with part.open("ab") as output:
                        shutil.copyfileobj(source, output, length=1024 * 1024)
                print(f"templates: {part.stat().st_size * 100 // total}%", flush=True)
        else:
          try:
            request = urllib.request.Request(url, headers={"User-Agent": "Soccer-Resource-Prep/1.0"})
            with urllib.request.urlopen(request, timeout=120) as source, part.open("wb") as output:
                shutil.copyfileobj(source, output, length=1024 * 1024)
          except Exception:
            subprocess.run(["curl", "--http1.1", "-fLsS", "--retry", "1", "--connect-timeout", "20", "--max-time", "900", url, "-o", str(part)], check=True)
        if expected and digest(part) != expected:
            raise RuntimeError(f"SHA256 mismatch: {key}")
        part.replace(path)
    sha = digest(path)
    if key == "godot":
        unpack = ROOT / ".tools/godot-unpack"
        subprocess.run(["ditto", "-xk", str(path), str(unpack)], check=True)
        app = Path.home() / "Applications" / f"Godot-{VERSION}.app"
        app.parent.mkdir(parents=True, exist_ok=True)
        if not app.exists():
            shutil.move(str(unpack / "Godot.app"), app)
        subprocess.run(["codesign", "--verify", "--deep", "--strict", str(app)], check=True)
        engine_version = subprocess.check_output([str(app / "Contents/MacOS/Godot"), "--headless", "--version"], text=True).strip()
        return {"id": key, "url": url, "sha256": sha, "size": path.stat().st_size, "installed_path": str(app), "version": engine_version, "status": "ready"}
    if key == "templates":
        dest = Path.home() / "Library/Application Support/Godot/export_templates" / f"{VERSION}.stable"
        dest.mkdir(parents=True, exist_ok=True)
        selected = []
        with zipfile.ZipFile(path) as archive:
            for item in archive.infolist():
                name = Path(item.filename).name
                if name in ["version.txt", "windows_debug_x86_64.exe", "windows_release_x86_64.exe", "linux_debug.x86_64", "linux_release.x86_64"]:
                    with archive.open(item) as source, (dest / name).open("wb") as output:
                        shutil.copyfileobj(source, output)
                    selected.append(name)
        required = {"windows_debug_x86_64.exe", "windows_release_x86_64.exe", "linux_debug.x86_64", "linux_release.x86_64"}
        if not required.issubset(selected):
            raise RuntimeError(f"Missing export templates: {required - set(selected)}")
        return {"id": key, "url": url, "sha256": sha, "size": path.stat().st_size, "installed_path": str(dest), "selected_files": sorted(selected), "status": "ready"}
    if key in ("font", "font-license"):
        dest = ROOT / "assets/third_party/noto-sans-cjk"
        dest.mkdir(parents=True, exist_ok=True)
        target = dest / ("NotoSansCJKsc-Regular.otf" if key == "font" else "OFL.txt")
        shutil.copy2(path, target)
    else:
        dest = ROOT / "assets/third_party/kenney" / key
        dest.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(path) as archive:
            base = dest.resolve()
            for item in archive.infolist():
                if not (dest / item.filename).resolve().is_relative_to(base):
                    raise ValueError("Unsafe archive path")
            archive.extractall(dest)
        target = dest
        licenses = [p for p in dest.rglob("*") if p.is_file() and "license" in p.name.lower()]
        if not licenses:
            raise RuntimeError(f"No license file: {key}")
        license_dest = ROOT / "assets/licenses"
        license_dest.mkdir(parents=True, exist_ok=True)
        shutil.copy2(licenses[0], license_dest / f"kenney-{key}-license.txt")
    return {"id": key, "url": url, "sha256": sha, "size": path.stat().st_size, "installed_path": str(target), "status": "ready"}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--include-legacy-godot", action="store_true", help="Also reproduce the archived Godot preview toolchain; not needed for Unity.")
    args = parser.parse_args()
    DOWNLOADS.mkdir(parents=True, exist_ok=True)
    report = ROOT / "reports/preparation-downloads.json"
    jobs = JOBS if args.include_legacy_godot else [job for job in JOBS if job[0] not in ("godot", "templates")]
    selected = {job[0] for job in jobs}
    previous = json.loads(report.read_text()) if report.exists() else []
    results = [item for item in previous if item.get("id") not in selected]
    with ThreadPoolExecutor(max_workers=5) as pool:
        pending = {pool.submit(fetch, job): job[0] for job in jobs}
        for future in as_completed(pending):
            key = pending[future]
            try:
                result = future.result()
            except Exception as exc:
                result = {"id": key, "status": "failed", "error": str(exc)}
            results.append(result)
            print(f"{key}: {result['status']}", flush=True)
            report.write_text(json.dumps(results, ensure_ascii=False, indent=2) + "\n")
    if any(r["status"] != "ready" for r in results if r.get("id") in selected):
        raise SystemExit(1)

if __name__ == "__main__":
    main()
