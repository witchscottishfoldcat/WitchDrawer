import json, os

for p in ["src/WitchDrawer.Native", "src/WitchDrawer.Core", "src/WitchDrawer.App"]:
    f = os.path.join(p, "obj", "project.assets.json")
    if not os.path.exists(f):
        print(p, ": no assets file")
        continue
    d = json.load(open(f, encoding="utf-8"))
    rids = sorted({t.split("/", 1)[1] for t in d.get("targets", {}) if "/" in t})
    print(p, "rids:", rids)
