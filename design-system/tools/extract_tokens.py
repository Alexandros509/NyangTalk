"""Extracts NyangTalk's design tokens from the source code (deterministic, no hand-typed values).

Run from the repository root:  python -I design-system/tools/extract_tokens.py
Writes design-system/tokens/tokens.json, tokens.css and contrast.md.
Fails if a non-colour token is no longer found at its source line (the code changed: update the list below).
"""
import json, os, re, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "design-system", "tokens")
ROLES = ["window", "panel", "otherBubble", "soft", "border", "text", "midText", "subText", "faintText", "datePill", "notice", "accent", "accentText"]


def read(rel):
    with open(os.path.join(ROOT, rel), encoding="utf-8") as f:
        return f.read()


def themes():
    src = read("src/Domain/Preferences/Theme.cs")
    dark_part = src[src.index("public static Theme Get(string id, bool dark)"):src.index("private static Theme Light(string id)")]
    light_part = src[src.index("private static Theme Light(string id)"):src.index("private static Theme Make(")]
    pat = re.compile(r'Make\("(\w+)",\s*((?:"#[0-9A-Fa-f]{6}",?\s*){13})\)')
    out = {}
    for mode, part in (("light", light_part), ("dark", dark_part)):
        for m in pat.finditer(part):
            colours = re.findall(r'#[0-9A-Fa-f]{6}', m.group(2))
            out.setdefault(m.group(1), {})[mode] = dict(zip(ROLES, [c.upper() for c in colours]))
    warn = re.search(r'Warning \{ get \{ return Dark \? Rgb.FromHex\("(#\w{6})"\) : Rgb.FromHex\("(#\w{6})"\)', src)
    send_fill = re.search(r'SendFill.*?Rgb.FromHex\("(#[0-9A-Fa-f]{6})"\)', src).group(1)
    send_text = re.search(r'SendText.*?Rgb.FromHex\("(#[0-9A-Fa-f]{6})"\)', src).group(1)
    for t in out.values():
        t["light"]["warning"] = warn.group(2).upper()
        t["dark"]["warning"] = warn.group(1).upper()
    for name, modes in out.items():
        for roles in modes.values():
            roles.update(otherText=roles["text"], mineBubble=roles["soft"] if name == "peach" else roles["accent"],
                         mineText=roles["text"] if name == "peach" else roles["accentText"],
                         sendFill=send_fill if name == "peach" else roles["accent"],
                         sendText=send_text if name == "peach" else roles["accentText"])
    return out


def person_palette():
    src = read("src/Domain/People/People.cs")
    block = src[src.index("public static readonly Rgb[] Palette"):]
    block = block[:block.index("};")]
    return [c.upper() for c in re.findall(r'#[0-9A-Fa-f]{6}', block)]


# name, value, unit, source file, text that must appear in that file
FIXED = [
    ("contrast.min", 4.5, "ratio", "src/Domain/People/People.cs", "MinContrast"),
    ("tooltip.delay", 500, "ms", "src/App/Controls/Themed.cs", "public const int DelayMs = 500;"),
    ("settings.window.default", "720x680", "px", "src/App/SettingsForm.cs", "DisplayScale.Size(720, 680, dpi)"),
    ("settings.window.min", "560x420", "px", "src/App/SettingsForm.cs", "DisplayScale.Size(560, 420, dpi)"),
    ("settings.topbar.height", 52, "px", "src/App/SettingsForm.cs", "_top.Height = 52"),
    ("settings.category.width", 172, "px", "src/App/SettingsForm.cs", "_cats.Width = 172"),
    ("settings.category.itemHeight", 38, "px", "src/App/Controls/Themed.cs", "public const int ItemHeight = 38;"),
    ("settings.page.margin", 18, "px", "src/App/SettingsForm.cs", "public const int Margin = 18"),
    ("settings.label.width", "40% of page, 150-260", "px", "src/App/SettingsForm.cs", "LabelMin = 150, LabelMax = 260"),
    ("settings.editor.width", "150-220", "px", "src/App/SettingsForm.cs", "EditorMin = 150, EditorMax = 220"),
    ("settings.group.gap", 14, "px", "src/App/SettingsForm.cs", "int top = y == Margin ? 0 : 14;"),
    ("settings.title.size", "body +5pt bold", "pt", "src/App/SettingsForm.cs", "+ 5, FontStyle.Bold"),
    ("settings.heading.size", "body +2pt bold", "pt", "src/App/SettingsForm.cs", "_form.Font.Size + 2, FontStyle.Bold"),
    ("settings.body.min", 10, "pt", "src/App/SettingsForm.cs", "Math.Max(10f, style.FontSize)"),
    ("control.height", 28, "px", "src/App/Controls/Themed.cs", "Height = 28;"),
    ("control.radius", 4, "px", "src/App/Controls/Themed.cs", "Round(r, 4)"),
    ("checkbox.size", 16, "px", "src/App/Controls/Themed.cs", "private const int Box = 16, Gap = 8;"),
    ("checkbox.radius", 3, "px", "src/App/Controls/Themed.cs", "Round(box, 3)"),
    ("category.radius", 6, "px", "src/App/Controls/Themed.cs", "Round(r, 6)"),
    ("stepper.width", 132, "px", "src/App/Controls/Themed.cs", "Width = 132;"),
    ("scrollbar.width.page", 10, "px", "src/App/Controls/Themed.cs", "public const int BarWidth = 10;"),
    ("scrollbar.width.list", 12, "px", "src/App/Controls/ThemedLists.cs", "public const int BarWidth = 12;"),
    ("pill.height", 28, "px", "src/App/Controls/FaceStack.cs", "PillHeight = 28"),
    ("pill.face", 22, "px", "src/App/Controls/FaceStack.cs", "Face = 22"),
    ("pill.faceStep", 14, "px", "src/App/Controls/FaceStack.cs", "FaceStep = 14"),
    ("header.pill.height", 28, "px", "src/App/Controls/IconButton.cs", "Height = 28"),
    ("header.pill.radius", 8, "px", "src/App/Controls/IconButton.cs", "Radius = 8"),
    ("header.pill.font", 11, "px", "src/App/Controls/IconButton.cs", "FontPixels = 11"),
    ("header.pill.padding.wide", 9, "px", "src/App/Controls/IconButton.cs", "WidePadding = 9"),
    ("header.pill.padding.narrow", 6, "px", "src/App/Controls/IconButton.cs", "NarrowPadding = 6"),
    ("header.pill.gap.wide", 6, "px", "src/App/Controls/IconButton.cs", "WideGap = 6"),
    ("header.pill.gap.narrow", 4, "px", "src/App/Controls/IconButton.cs", "NarrowGap = 4"),
    ("header.pill.hoverOpacity", 0.8, "ratio", "src/App/Controls/IconButton.cs", "HoverOpacity = 0.8f"),
    ("header.pill.disabledOpacity", 0.4, "ratio", "src/App/Controls/IconButton.cs", "DisabledOpacity = 0.4f"),
    ("typography.body.default", 11, "pt", "src/Domain/Preferences/Settings.cs", 'I("screen.fontSize", 11, 8, 16)'),
    ("header.pill.font.scale", "11px + max(0, bodyPt - 9)", "", "src/App/Controls/IconButton.cs", "FontPixels + Math.Max(0, bodyPoints - 9)"),
    ("header.pill.height.scale", "max(28px, fontHeight + 10px)", "", "src/App/Controls/IconButton.cs", "Math.Max(Height, font.Height + 10)"),
    ("notice.surface", "soft / text", "", "src/App/MainForm.cs", "NoticeBand.BackColor = RenderStyle.C(th.Soft); NoticeLabel.ForeColor = RenderStyle.C(th.Text);"),
    ("send.radius", "max(6, bubbleRadius) at device DPI", "", "src/App/MainForm.cs", "DisplayScale.Pixels(Math.Max(6, s.Radius), DeviceDpiSafe)"),
    ("iconButton.size", "32x30", "px", "src/App/Controls/IconButton.cs", "Size = new Size(32, 30);"),
    ("header.minHeight", 50, "px", "src/App/MainForm.cs", "Header.Height = Math.Max(50,"),
    ("header.status.size", "body -1pt, 9-10", "pt", "src/App/MainForm.cs", "Math.Max(9f, Math.Min(10f, bodyPoints - 1))"),
    ("header.title.minWidth", 110, "px", "src/App/MainForm.cs", "MinTitleWidth = 110"),
    ("mix.hover", "panel -> accent 12%", "", "src/App/Controls/Themed.cs", "Mix(RenderStyle.C(th.Panel), RenderStyle.C(th.Accent), 0.12)"),
    ("mix.pressed", "panel -> accent 25%", "", "src/App/Controls/Themed.cs", "Mix(Panel, Accent, 0.25)"),
    ("mix.disabledText", "back -> text 50%", "", "src/App/Controls/Themed.cs", "Palette.Mix(Pal.Back, Pal.Text, 0.5)"),
    ("mix.scrollThumb", "back -> subText 55% (70% hover, 85% drag)", "", "src/App/Controls/ThemedLists.cs", "_drag >= 0 ? 0.85 : _hot ? 0.7 : 0.55"),
    ("status.connected", "#1F9D55", "colour", "src/App/Controls/Themed.cs", "Color.FromArgb(0x1F, 0x9D, 0x55)"),
    ("status.waiting", "#C98A00", "colour", "src/App/Controls/Themed.cs", "Color.FromArgb(0xC9, 0x8A, 0x00)"),
    ("window.chat.steps", "mini 300x360, small 380x520, default 510x790, large 680x900", "logical px", "src/Domain/Preferences/Settings.cs", 'case "mini": return new[] { 300, 360 };'),
]


def channel(c):
    s = c / 255.0
    return s / 12.92 if s <= 0.03928 else ((s + 0.055) / 1.055) ** 2.4


def lum(h):
    r, g, b = (int(h[i:i + 2], 16) for i in (1, 3, 5))
    return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b)


def contrast(a, b):
    la, lb = sorted((lum(a), lum(b)), reverse=True)
    return (la + 0.05) / (lb + 0.05)


PAIRS = [("text", "window"), ("subText", "window"), ("text", "panel"), ("subText", "panel"), ("otherText", "otherBubble"),
         ("mineText", "mineBubble"), ("accentText", "accent"), ("sendText", "sendFill"), ("notice", "datePill"), ("warning", "window"), ("warning", "panel")]


def main():
    t = themes()
    assert len(t) == 6 and all(len(v) == 2 for v in t.values()), "expected 6 themes x light/dark, got %r" % {k: list(v) for k, v in t.items()}
    fixed = []
    for name, value, unit, path, needle in FIXED:
        src = read(path)
        if needle not in src:
            sys.exit("token %s: '%s' not found in %s (the code changed; update FIXED)" % (name, needle, path))
        line = src[:src.index(needle)].count("\n") + 1
        fixed.append({"name": name, "value": value, "unit": unit, "source": "%s:%d" % (path, line)})
    data = {"generatedFrom": "NyangTalk source (see each token's source)", "themes": t, "personColours": person_palette(), "tokens": fixed}
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, "tokens.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

    css = ["/* Generated by design-system/tools/extract_tokens.py from NyangTalk's source. Do not edit by hand. */",
           "/* Usage: <html data-theme=\"default|mint|sky|purple|peach|mono\" data-mode=\"light|dark\"> */"]
    for name, modes in t.items():
        for mode, roles in modes.items():
            sel = '[data-theme="%s"][data-mode="%s"]' % (name, mode)
            if name == "default" and mode == "light":
                sel = ":root, " + sel
            css.append(sel + " {")
            for role, hexv in roles.items():
                css.append("  --nt-%s: %s;" % (re.sub(r'([A-Z])', lambda m: '-' + m.group(1).lower(), role), hexv))
            css.append("}")
    css.append(":root {")
    for i, c in enumerate(person_palette()):
        css.append("  --nt-person-%d: %s;" % (i + 1, c))
    for tok in fixed:
        if tok["unit"] == "px" and isinstance(tok["value"], int):
            css.append("  --nt-%s: %dpx;" % (tok["name"].replace(".", "-"), tok["value"]))
        elif tok["unit"] == "ms":
            css.append("  --nt-%s: %dms;" % (tok["name"].replace(".", "-"), tok["value"]))
    css.append("}")
    with open(os.path.join(OUT, "tokens.css"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(css) + "\n")

    md = ["# 대비 표 (자동 생성)", "", "`tools/extract_tokens.py`가 코드의 색으로 계산했다. 기준 4.5:1 이상.", "",
          "| 테마 | 판 | " + " | ".join("%s/%s" % p for p in PAIRS) + " |", "|---|---|" + "---|" * len(PAIRS)]
    worst = 99
    for name, modes in t.items():
        for mode, roles in modes.items():
            vals = [contrast(roles[a], roles[b]) for a, b in PAIRS]
            worst = min(worst, min(vals))
            md.append("| %s | %s | %s |" % (name, mode, " | ".join("%.2f" % v for v in vals)))
    md += ["", "가장 낮은 값: %.2f" % worst, "", "기본 밝게의 보조 글자·날짜 색은 사용자 승인 원본 팔레트 그대로이며 일부 대비가 4.5 미만이다. 나머지 테마의 본문·보조 글자 기준은 4.5이다."]
    with open(os.path.join(OUT, "contrast.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(md) + "\n")
    print("themes %d x 2, person colours %d, other tokens %d, lowest contrast %.2f" % (len(t), len(person_palette()), len(fixed), worst))
    if any(contrast(roles[a], roles[b]) < 4.5 for name, modes in t.items() for mode, roles in modes.items() for a, b in PAIRS if not (name == "default" and mode == "light")):
        sys.exit("contrast below 4.5 outside the approved legacy light palette")


if __name__ == "__main__":
    main()
