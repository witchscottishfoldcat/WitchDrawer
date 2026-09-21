"""换行符自适应的精确替换（混合 CRLF/LF 文件用）。

用法: python tools/mixed_edit.py <目标文件> <old.txt> <new.txt>
old/new 文件用 LF 书写；匹配时 old 的每个换行同时兼容 \r\n 与 \n，
替换结果继承被匹配区域实际使用的换行风格。
"""
import re
import sys
from pathlib import Path


def main() -> int:
    target, old_path, new_path = sys.argv[1], sys.argv[2], sys.argv[3]
    data = Path(target).read_bytes().decode("utf-8")
    old = Path(old_path).read_text(encoding="utf-8").replace("\r\n", "\n")
    new = Path(new_path).read_text(encoding="utf-8").replace("\r\n", "\n")

    pattern = r"\r?\n".join(re.escape(line) for line in old.split("\n"))
    matches = list(re.finditer(pattern, data))
    if len(matches) != 1:
        print(f"ERROR: matched {len(matches)} times", file=sys.stderr)
        return 1

    match = matches[0]
    span = match.group(0)
    crlf = span.count("\r\n")
    lf_only = span.count("\n") - crlf
    ending = "\r\n" if crlf >= lf_only else "\n"
    replacement = new.replace("\n", ending)
    # 保持与匹配区域一致的首尾换行处理
    data = data[: match.start()] + replacement + data[match.end():]
    Path(target).write_bytes(data.encode("utf-8"))
    print(f"OK: replaced 1 occurrence in {target} (eol={ending!r})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
