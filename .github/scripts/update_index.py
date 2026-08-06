import json
import sys
import pathlib

name, version, sha256, url, repo = sys.argv[1:6]

pkg = json.loads(pathlib.Path("package.json").read_text(encoding="utf-8"))
pkg["url"] = url
pkg["zipSHA256"] = sha256

index_path = pathlib.Path("index.json")
if index_path.exists():
    index = json.loads(index_path.read_text(encoding="utf-8"))
else:
    index = {
        "name": "menou-store VPM Listing",
        "author": "menou-store",
        "id": f"dev.menou2846.{name}",
        "url": f"https://raw.githubusercontent.com/{repo}/main/index.json",
        "packages": {},
    }

index["packages"].setdefault(name, {"versions": {}})["versions"][version] = pkg

index_path.write_text(
    json.dumps(index, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
)
