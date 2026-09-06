import hashlib, os, zipfile

src = r"publish\v1.3.7\WitchDrawer.App.exe"
zip_path = r"publish\WitchDrawer-v1.3.7-win-x64.zip"

with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
    z.write(src, "WitchDrawer.App.exe")

h = hashlib.sha256()
with open(zip_path, "rb") as f:
    for chunk in iter(lambda: f.read(1 << 20), b""):
        h.update(chunk)
digest = h.hexdigest()

sha_path = zip_path + ".sha256"
with open(sha_path, "w", newline="\n") as f:
    f.write(f"{digest}  {os.path.basename(zip_path)}")

print("zip :", zip_path, os.path.getsize(zip_path), "bytes")
print("sha :", digest)
print("wrote:", sha_path)
