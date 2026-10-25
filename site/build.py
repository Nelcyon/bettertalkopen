"""Builds the download page into a folder ready to publish (see .github/workflows/site.yml).

Fills in the {{...}} values in site/index.html from the newest GitHub release (version, size, SHA-256
checksum) and copies the screenshot next to it. If GitHub cannot be reached, the page still builds and
points people to the releases page for the checksum.

    python3 site/build.py <output folder>
"""
import datetime
import html
import json
import os
import re
import shutil
import sys
import urllib.request

REPO = "Nelcyon/bettertalkopen"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RELEASES = f"https://github.com/{REPO}/releases/latest"


def get(url):
    request = urllib.request.Request(url, headers={"User-Agent": "bettertalk-site", "Accept": "application/vnd.github+json"})
    token = os.environ.get("GITHUB_TOKEN")
    if token and url.startswith("https://api.github.com/"):
        request.add_header("Authorization", f"Bearer {token}")
    with urllib.request.urlopen(request, timeout=30) as response:
        return response.read()


def latest_release():
    """Returns (version, size in MB, sha256), with None for anything that could not be found."""
    try:
        release = json.loads(get(f"https://api.github.com/repos/{REPO}/releases/latest"))
    except Exception as error:  # noqa: BLE001 - any failure just means the page shows less
        print(f"Could not read the latest release: {error}")
        return None, None, None
    version = release.get("tag_name", "").lstrip("v") or None
    size_mb = sha256 = None
    for asset in release.get("assets", []):
        if asset["name"] == "BetterTalk-Setup.exe":
            size_mb = round(asset["size"] / 1_000_000)
        elif asset["name"] == "BetterTalk-Setup.exe.sha256":
            try:
                text = get(asset["browser_download_url"]).decode("ascii").split()
                if text and len(text[0]) == 64:
                    sha256 = text[0].lower()
            except Exception as error:  # noqa: BLE001
                print(f"Could not read the checksum: {error}")
    return version, size_mb, sha256


def inline(markdown):
    """Release-note markdown (one bullet) as safe HTML: **bold** kept, links and `code` reduced to text."""
    text = html.escape(markdown, quote=False)
    text = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", text)
    text = re.sub(r"`([^`]+)`", r"\1", text)
    return re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", text)


def recent_updates(count=5):
    """The newest releases as HTML: version and date, then each change in plain words.

    The notes are each version's section of CHANGELOG.md, which is written for the people using BetterTalk.
    """
    try:
        releases = json.loads(get(f"https://api.github.com/repos/{REPO}/releases?per_page={count + 5}"))
    except Exception as error:  # noqa: BLE001
        print(f"Could not read the releases: {error}")
        return ""
    items = []
    for release in releases:
        if release.get("draft") or release.get("prerelease"):
            continue
        notes = (release.get("body") or "").replace("\r", "").split("\n---", 1)[0]
        bullets = []
        for line in notes.split("\n"):
            if line.startswith("- "):
                bullets.append(line[2:].strip())
            elif bullets and line.strip():
                bullets[-1] += " " + line.strip()
        if not bullets:
            continue
        published = datetime.datetime.fromisoformat(release["published_at"].replace("Z", "+00:00"))
        changes = "\n".join(f"        <li>{inline(b)}</li>" for b in bullets)
        items.append(
            f'    <li>\n      <h3>Version {html.escape(release["tag_name"].lstrip("v"))}'
            f' <span class="when">{published.day} {published:%B %Y}</span></h3>\n'
            f'      <ul>\n{changes}\n      </ul>\n    </li>')
        if len(items) == count:
            break
    return "\n".join(items)


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "_site")
    version, size_mb, sha256 = latest_release()

    if version and sha256:
        checksum = (
            f' For version {html.escape(version)} it is <span class="checksum">{sha256}</span>.'
            ' To check yours, open PowerShell in the folder where you saved the file and type'
            ' <span class="checksum command">Get-FileHash .\\BetterTalk-Setup.exe</span>: the number must match.'
        )
    else:
        checksum = f' You can find it on the <a href="{RELEASES}">download page for each version</a>.'

    page = open(os.path.join(ROOT, "site", "index.html"), encoding="utf-8").read()
    page = (page
            .replace("{{VERSION_LINE}}", f"Version {html.escape(version)}. " if version else "")
            .replace("{{SIZE_MB}}", str(size_mb or 60))
            .replace("{{CHECKSUM_LINE}}", checksum)
            .replace("{{RECENT_UPDATES}}", recent_updates()))
    if re.search(r"\{\{[A-Z_]+\}\}", page):
        sys.exit("site/index.html has a {{...}} value that build.py does not fill in")

    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "index.html"), "w", encoding="utf-8") as f:
        f.write(page)
    for image in ("main-window.png", "itch.png"):
        shutil.copyfile(os.path.join(ROOT, "docs", image), os.path.join(out, image))
    # Served as plain files, never run through Jekyll.
    open(os.path.join(out, ".nojekyll"), "w").close()
    # A custom domain for GitHub Pages, if one is ever added, lives in site/CNAME.
    if os.path.exists(os.path.join(ROOT, "site", "CNAME")):
        shutil.copyfile(os.path.join(ROOT, "site", "CNAME"), os.path.join(out, "CNAME"))
    print(f"Built {out} (version {version}, {size_mb} MB, checksum {'yes' if sha256 else 'no'})")


if __name__ == "__main__":
    main()
