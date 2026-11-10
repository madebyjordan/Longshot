"""A run's numbers from its log (scripts/test-run.ps1's <runs>/<name>/log.txt, scripts/bench.ps1's, or a played game's
hookshot_log.txt): the frame rate in play, what a frame costs on the GPU and in the replay, which threads are busy, the
present intervals, the virtual cores' waits, and what was built or uploaded in play. With two logs, the same rows side by
side and the change: an A/B of two renderers, two switches or two builds on the same save.

    python -I scripts/benchlog.py <log or run folder> [<another, to compare with>] [--draws 800] [--skip 2] [--json]

In play: the 120-present samples that draw at least --draws draws a frame (the title and the menus draw a few dozen), after
the first --skip of them (the area still loading in, its pipelines and textures made). Every other line counts while the
last sample was in play. The switches that add lines: HOS_PROFILE=<seconds> (the busiest threads) and RX_VK_PASS_TIMING=1
(the GPU's time by pass and program; its timestamps make the GPU time itself longer, so compare runs that both have it or
both lack it). Standard library only.
"""
import argparse
import collections
import json
import os
import re
import sys

STAT = re.compile(r"vk: (\d+) presents, ([\d.]+) fps; a frame: (\d+) draws .*?(\d+) pipelines \((\d+) ms\).*?(\d+) textures \((\d+) MB\)")
GPU = re.compile(r"vk: GPU time ([\d.]+) ms a frame \(the longest submission ([\d.]+) ms\)")
REPLAY = re.compile(r"vk: a frame's replay time \(ms\): (.*)")
PROF = re.compile(r"prof: busiest threads \(% of a core\): (.*)")
INTERVALS = re.compile(r"vi: present intervals: (.*)")
VCORES = re.compile(r"vcores: (\d+) takes, (\d+) waits \(mean ([\d.]+) us, longest ([\d.]+) ms\), (\d+) kicks, (\d+) yields, "
                    r"(\d+) escapes in ([\d.]+) s")
STALL = re.compile(r"vcores: .* ran without a core after ([\d.]+) ms")
CAPTURE = re.compile(r"vk: a submit's memory work \(ms\): maps ([\d.]+), capture taken ([\d.]+), writes collected ([\d.]+)")
VRAM = re.compile(r"vk: VRAM: (\d+) MB used of (\d+) MB allowed")
WORKING = re.compile(r"vk: memory: working set (\d+) MB \(peak (\d+)\)")
SLOW_UPLOAD = re.compile(r"vk: slow upload ([\d.]+) ms")
PASS_ALL = re.compile(r"vk:\s+([\d.]+) ms a frame in all")
SPANS_HEAD = re.compile(r"vk:\s+the (\d+) spans that took the most")
SPAN = re.compile(r"vk:\s+[mu]\s+([\d.]+) ms\s+[\d.]+ x\s+[\d.]+ us\s+[\d.]+ items\s+[\d.]+ MB\s+(.*\S)")
VERSION = re.compile(r"\] hookshot (.*)")
DEVICE = re.compile(r"vk: device \d+: ([^,]+),")
OUTPUT = re.compile(r"oe: output resolution (.*)")
OFFSCREEN = re.compile(r"vk: no window: presents render offscreen")


def quantile(values, q):
    """The q-quantile of values (0..1), between the two nearest of them; None of nothing"""
    if not values:
        return None
    s = sorted(values)
    at = (len(s) - 1) * q
    lo = int(at)
    hi = min(lo + 1, len(s) - 1)
    return s[lo] + (s[hi] - s[lo]) * (at - lo)


def pairs(text):
    """ "submit 31.19, present 0.09, memory sync 14.33" -> [("submit", 31.19), ...]: the number is each part's last word"""
    out = []
    for part in text.split(","):
        name, _, value = part.strip().rpartition(" ")
        try:
            out.append((name.strip(), float(value)))
        except ValueError:
            pass
    return out


def log_path(path):
    if os.path.isdir(path):
        for name in ("log.txt", "hookshot_log.txt"):
            if os.path.exists(os.path.join(path, name)):
                return os.path.join(path, name)
        raise SystemExit("%s: no log.txt or hookshot_log.txt in it" % path)
    return path


def read(path, draws=800, skip=2):
    """The log's numbers as a dict of plain values (what --json prints, and what compare() lines up)"""
    fps, gpu, gpu_longest, capture, vram, slow_uploads, pass_all = [], [], [], [], [], [], []
    replay = collections.defaultdict(list)
    threads = collections.defaultdict(list)
    intervals = collections.OrderedDict()
    spans = collections.defaultdict(list)
    vc = {"waits": 0, "wait_us": 0.0, "longest_ms": 0.0, "escapes": 0, "seconds": 0.0}
    r = {"log": path, "version": None, "device": None, "output": None, "headless": False, "samples": 0, "play_samples": 0,
         "stalls": 0, "stalls_in_play": 0, "working_set_peak_mb": None}
    seen = 0            # in-play samples so far, the skipped ones too
    play = False        # the last sample was in play, past the skipped ones
    first = last = None  # (pipelines, their ms) at the first and the last in-play sample
    prof_lines = span_blocks = 0
    spans_left = 0      # span lines still to come of the list being read (other threads' lines come between them)
    with open(path, encoding="utf-8", errors="replace") as f:
        for line in f:
            if spans_left and (m := SPAN.search(line)):
                spans[m.group(2)].append(float(m.group(1)))
                spans_left -= 1
                continue
            m = STAT.search(line)
            if m:
                r["samples"] += 1
                if int(m.group(3)) >= draws:
                    seen += 1
                    play = seen > skip
                else:
                    play = False
                if play:
                    fps.append(float(m.group(2)))
                    last = (int(m.group(4)), int(m.group(5)))
                    first = first or last
                continue
            if r["version"] is None and (m := VERSION.search(line)):
                r["version"] = m.group(1).strip()
                continue
            if r["device"] is None and (m := DEVICE.search(line)):
                r["device"] = m.group(1).strip()
                continue
            if r["output"] is None and (m := OUTPUT.search(line)):
                r["output"] = m.group(1).strip()
                continue
            if OFFSCREEN.search(line):
                r["headless"] = True
                continue
            if m := STALL.search(line):
                r["stalls"] += 1
                r["stalls_in_play"] += play
                continue
            if m := WORKING.search(line):
                r["working_set_peak_mb"] = max(r["working_set_peak_mb"] or 0, int(m.group(2)))
                continue
            if not play:
                spans_left = 0
                continue
            if m := GPU.search(line):
                gpu.append(float(m.group(1)))
                gpu_longest.append(float(m.group(2)))
            elif m := REPLAY.search(line):
                for name, value in pairs(m.group(1)):
                    replay[name].append(value)
            elif m := PROF.search(line):
                prof_lines += 1
                for name, value in pairs(m.group(1)):
                    threads[name].append(value)
            elif m := INTERVALS.search(line):
                for name, value in pairs(m.group(1)):
                    intervals[name] = intervals.get(name, 0) + int(value)
            elif m := VCORES.search(line):
                waits = int(m.group(2))
                vc["waits"] += waits
                vc["wait_us"] += waits * float(m.group(3))
                vc["longest_ms"] = max(vc["longest_ms"], float(m.group(4)))
                vc["escapes"] += int(m.group(7))
                vc["seconds"] += float(m.group(8))
            elif m := CAPTURE.search(line):
                capture.append(float(m.group(2)))
            elif m := VRAM.search(line):
                vram.append(int(m.group(1)))
            elif m := SLOW_UPLOAD.search(line):
                slow_uploads.append(float(m.group(1)))
            elif m := PASS_ALL.search(line):
                pass_all.append(float(m.group(1)))
                spans_left = 0
            elif m := SPANS_HEAD.search(line):
                spans_left = int(m.group(1))
                span_blocks += 1
    r["play_samples"] = len(fps)
    r["fps"] = {"median": quantile(fps, 0.5), "p10": quantile(fps, 0.1), "p90": quantile(fps, 0.9), "min": min(fps) if fps else None}
    r["gpu_ms"] = quantile(gpu, 0.5)
    r["gpu_longest_submission_ms"] = quantile(gpu_longest, 0.5)
    r["replay_ms"] = collections.OrderedDict((k, quantile(v, 0.5)) for k, v in replay.items())
    # (a thread not among a line's ten busiest counts as idle in it: the mean is over every profile line)
    r["threads"] = collections.OrderedDict(sorted(((k, sum(v) / prof_lines) for k, v in threads.items()), key=lambda kv: -kv[1]))
    total = sum(intervals.values())
    r["intervals"] = collections.OrderedDict((k, 100.0 * v / total) for k, v in intervals.items()) if total else {}
    r["vcores"] = {"waits_per_s": vc["waits"] / vc["seconds"] if vc["seconds"] else None,
                   "mean_wait_us": vc["wait_us"] / vc["waits"] if vc["waits"] else None,
                   "longest_wait_ms": vc["longest_ms"] if vc["seconds"] else None, "escapes": vc["escapes"]}
    r["capture_ms_per_submit"] = quantile(capture, 0.5)
    r["vram_mb"] = {"median": quantile(vram, 0.5), "max": max(vram) if vram else None}
    r["slow_uploads"] = {"count": len(slow_uploads), "longest_ms": max(slow_uploads) if slow_uploads else None}
    r["pipelines_in_play"] = {"made": last[0] - first[0], "ms": last[1] - first[1]} if first else {"made": None, "ms": None}
    r["pass_timing_ms"] = quantile(pass_all, 0.5)
    # (a span missing from a block took too little to be listed there: the mean is over every block)
    r["spans_ms"] = collections.OrderedDict(sorted(((k, sum(v) / span_blocks) for k, v in spans.items()), key=lambda kv: -kv[1]))
    return r


def rows(r, top=8):
    """(label, value, unit, lower is better) for every number worth a row; None where the log has no such line"""
    out = [("fps, median", r["fps"]["median"], "fps", False), ("fps, 10th percentile", r["fps"]["p10"], "fps", False),
           ("fps, 90th percentile", r["fps"]["p90"], "fps", False), ("fps, lowest sample", r["fps"]["min"], "fps", False),
           ("GPU time a frame", r["gpu_ms"], "ms", True), ("GPU, longest submission", r["gpu_longest_submission_ms"], "ms", True)]
    out += [("replay: " + k, v, "ms", True) for k, v in r["replay_ms"].items()]
    out += [("capture, a submit", r["capture_ms_per_submit"], "ms", True)]
    out += [("thread: " + k, v, "% of a core", None) for k, v in list(r["threads"].items())[:top + 1]]
    out += [("presents " + k, v, "%", None) for k, v in r["intervals"].items()]
    out += [("virtual cores: waits a second", r["vcores"]["waits_per_s"], "", True),
            ("virtual cores: mean wait", r["vcores"]["mean_wait_us"], "us", True),
            ("virtual cores: longest wait", r["vcores"]["longest_wait_ms"], "ms", True),
            ("ran without a core, in play", r["stalls_in_play"], "", True), ("ran without a core, whole run", r["stalls"], "", True),
            ("pipelines built in play", r["pipelines_in_play"]["made"], "", True),
            ("their compile time", r["pipelines_in_play"]["ms"], "ms", True),
            ("slow uploads", r["slow_uploads"]["count"], "", True), ("the longest of them", r["slow_uploads"]["longest_ms"], "ms", True),
            ("VRAM used, median", r["vram_mb"]["median"], "MB", True), ("VRAM used, most", r["vram_mb"]["max"], "MB", True),
            ("working set, peak", r["working_set_peak_mb"], "MB", True)]
    if r["pass_timing_ms"] is not None:
        out += [("GPU, timed spans in all", r["pass_timing_ms"], "ms", True)]
        out += [("GPU span: " + k, v, "ms", True) for k, v in list(r["spans_ms"].items())[:top]]
    return out


def number(v):
    if v is None:
        return "-"
    if isinstance(v, int):
        return "%d" % v
    return "%.2f" % v if abs(v) < 10 else "%.1f" % v


def head(r):
    return "%s: %s, %s, output %s%s; %d samples in play of %d" % (
        r["log"], r["version"] or "?", r["device"] or "?", r["output"] or "?", ", no window" if r["headless"] else "",
        r["play_samples"], r["samples"])


WIDEST = 76   # (a GPU span's label names its pass and both its programs: cut there)


def column(labels):
    return max([30] + [min(len(x), WIDEST) for x in labels])


def report(r):
    lines = [head(r)]
    if not r["play_samples"]:
        lines.append("no sample in play: the run never drew a scene, or stopped before it settled (see --draws and --skip)")
    shown = [x for x in rows(r) if x[1] is not None]
    w = column([x[0] for x in shown])
    for label, value, unit, _ in shown:
        lines.append("  %-*s %10s %s" % (w, label[:w], number(value), unit))
    return "\n".join(lines)


def compare(a, b):
    """The rows both logs have, with b's change from a; a row only one has is shown with a dash for the other"""
    rb = collections.OrderedDict((label, (value, unit)) for label, value, unit, _ in rows(b))
    w = column([x[0] for x in rows(a) if x[1] is not None] + [k for k, v in rb.items() if v[0] is not None])
    lines = ["A  " + head(a), "B  " + head(b), "  %-*s %10s %10s %9s" % (w, "", "A", "B", "B - A")]
    done = set()
    for label, va, unit, _ in rows(a):
        vb = rb.get(label, (None, unit))[0]
        done.add(label)
        if va is None and vb is None:
            continue
        change = ""
        if va is not None and vb is not None:
            change = "%+.1f%%" % (100.0 * (vb - va) / va) if va else ("" if not vb else "new")
        lines.append("  %-*s %10s %10s %9s %s" % (w, label[:w], number(va), number(vb), change, unit))
    for label, (vb, unit) in rb.items():
        if label not in done and vb is not None:
            lines.append("  %-*s %10s %10s %9s %s" % (w, label[:w], "-", number(vb), "", unit))
    if a["headless"] != b["headless"] or a["output"] != b["output"] or (a["pass_timing_ms"] is None) != (b["pass_timing_ms"] is None):
        lines.append("note: the two runs differ in window, output resolution or pass timing: their GPU times do not compare")
    return "\n".join(lines)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("log", help="a log file, or a run folder with log.txt in it")
    p.add_argument("other", nargs="?", help="a second one: the first is A, this is B")
    p.add_argument("--draws", type=int, default=800, help="a sample is in play from this many draws a frame (800)")
    p.add_argument("--skip", type=int, default=2, help="in-play samples left out at the start (2)")
    p.add_argument("--json", action="store_true", help="the numbers as JSON instead of a table")
    a = p.parse_args()
    runs = [read(log_path(x), a.draws, a.skip) for x in (a.log, a.other) if x]
    if a.json:
        print(json.dumps(runs if len(runs) > 1 else runs[0], indent=1))
    elif len(runs) > 1:
        print(compare(*runs))
    else:
        print(report(runs[0]))


if __name__ == "__main__":
    sys.exit(main())
