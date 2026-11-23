"""Checks that every test name and source path cited in design-system/*.md exists in the repository."""
import os, re, sys
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DS = os.path.join(ROOT, "design-system")
tests = ""
for d, _, fs in os.walk(os.path.join(ROOT, "tests")):
    for f in fs:
        if f.endswith(".cs"):
            tests += open(os.path.join(d, f), encoding="utf-8").read()
bad = []
for f in os.listdir(DS):
    if not f.endswith(".md"):
        continue
    text = open(os.path.join(DS, f), encoding="utf-8").read()
    for m in re.findall(r"`((?:src|tests|docs|third_party|assets)/[^`\s]+)`", text):
        if not os.path.exists(os.path.join(ROOT, m)):
            bad.append("%s: path %s" % (f, m))
    for cls, name in re.findall(r"(\w+Tests)(?:\.cs`)?[`\s.]+`?(\w+)`", text):
        if "public static void " + name + "(" not in tests:
            bad.append("%s: test %s.%s" % (f, cls, name))
    for img in re.findall(r"^\| ([\w-]+\.png) \|", open(os.path.join(DS, "images", "README.md"), encoding="utf-8").read(), re.M):
        if not os.path.exists(os.path.join(DS, "images", img)):
            bad.append("images: %s" % img)
print("\n".join(sorted(set(bad))) or "all references found")
sys.exit(1 if bad else 0)
