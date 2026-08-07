import json
import sys
import pathlib

pkg_json_path, name, version, sha256, url, repo = sys.argv[1:7]

pkg = json.loads(pathlib.Path(pkg_json_path).read_text(encoding="utf-8"))
pkg["url"] = url
pkg["zipSHA256"] = sha256

index_path = pathlib.Path("index.json")
if index_path.exists():
    index = json.loads(index_path.read_text(encoding="utf-8"))
else:
    index = {
        "name": "menou-store VPM Listing",
        "author": "menou-store",
        "id": f"dev.menou2846.{repo.split('/')[-1]}",
        "url": f"https://raw.githubusercontent.com/{repo}/master/index.json",
        "packages": {},
    }

index["packages"].setdefault(name, {"versions": {}})["versions"][version] = pkg

index_path.write_text(
    json.dumps(index, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
)
