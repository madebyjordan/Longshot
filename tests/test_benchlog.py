"""scripts/benchlog.py reads a run's numbers from the log lines the game writes, in play only, and lines two runs up.

python -I -B tests/test_benchlog.py
"""
import importlib.util
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def load():
    spec = importlib.util.spec_from_file_location("benchlog", ROOT / "scripts" / "benchlog.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def stat(presents, fps, draws, pipelines, ms):
    return ("[hos Presentation Thread] vk: %d presents, %.1f fps; a frame: %d draws (0 skipped), 168 dispatches, 174 passes, "
            "150 barriers, 6 submits, 0.0 uploads (0.0 MB), 80 ub updates (56 overlays), GPU-bound wait 0.1 ms; "
            "%d pipelines (%d ms), 0 programs (10 failed), 1502 textures (3058 MB)" % (presents, fps, draws, pipelines, ms))


def log(fps, gpu, memory_sync, timing=False):
    """A run: the title (5 draws a frame), then len(fps) samples in play with the lines a run writes between them"""
    lines = ["[hos host] hookshot pre-1.1.0 70d9583 (source bf46bb96)",
             "[hos MainThread] vk: device 0: AMD Radeon RX 6700 XT, Vulkan 1.4, driver 0x800161, type 2",
             "[hos MainThread] vk: no window: presents render offscreen (1920x1080)",
             "[hos sead::MainThread] oe: output resolution 2560x1440",
             stat(120, 60.0, 5, 4037, 45504),
             # (the title's lines are not the game's: none of these may be counted)
             "[hos Presentation Thread] vk: GPU time 1.0 ms a frame (the longest submission 0.5 ms); 0.0 textures made a frame",
             "[hos host] prof: busiest threads (% of a core): Presentation Thread 5, all 9",
             "[hos MovieThread] vcores: MovieThread (priority 17) ran without a core after 250.4 ms: core 4: Worker4 (16);"]
    for i, f in enumerate(fps):
        lines += [stat(240 + 120 * i, f, 3025, 4040 + i, 45504 + 10 * i),
                  "[hos Presentation Thread] vk: GPU time %.1f ms a frame (the longest submission 8.6 ms); 0.2 textures made a frame" % gpu,
                  "[hos Presentation Thread] vk: a frame's replay time (ms): submit 29.72, present 0.13, draw 9.19, "
                  "texture lookups 0.09, memory sync %.2f, slot wait 4.00" % memory_sync,
                  "[hos host] prof: busiest threads (% of a core): Presentation Thread 58, sead::MainThread 24, all 262",
                  "[hos Presentation Thread] vi: present intervals: <15.5 ms 0, 15.5-17.8 0, 17.8-22 0, 22-30 1, 30-40 8, over 40 1",
                  "[hos Worker2] vcores: 57454 takes, 1000 waits (mean 40 us, longest 1.4 ms), 772 kicks, 774 yields, 0 escapes in 5 s",
                  "[hos sead::MainThread] vk: a submit's memory work (ms): maps 0.00, capture taken 1.96, writes collected 0.06",
                  "[hos Presentation Thread] vk: VRAM: 7000 MB used of 9392 MB allowed (1050 MB of it free in the blocks)",
                  "[hos Presentation Thread] vk: memory: working set 6490 MB (peak 10147), private 28006 MB"]
        if timing:
            lines += ["[hos Presentation Thread] vk: GPU pass timing, 600 presents, per frame (read the shares):",
                      "[hos Presentation Thread] vk:   26.00 ms a frame in all (recordings' command buffers + upload command buffers)",
                      "[hos Presentation Thread] vk:     m pass       12.543 ms  50.7%    173.8 spans    3025.2 items     0.00 MB     72.2 us a span",
                      "[hos Presentation Thread] vk:   the 25 spans that took the most (ms a frame, spans a frame, us a span, items a frame, MB a frame):",
                      "[hos Presentation Thread] vk:   m   6.400 ms     1.0 x   6400.0 us       1.0 items    0.00 MB  dispatch  prog 9dc98871eb7fc101",
                      # (another thread's line lands between two spans)
                      "[hos host] prof: busiest threads (% of a core): Presentation Thread 58, sead::MainThread 24, all 262",
                      "[hos Presentation Thread] vk:   u   1.000 ms    45.6 x     23.1 us    1579.6 items   10.98 MB  msync copies (CPU pages from a capture -> mirror)"]
    return "\n".join(lines) + "\n"


def close(a, b):
    return abs(a - b) < 1e-6


def main():
    bl = load()
    assert bl.quantile([], 0.5) is None
    assert close(bl.quantile([1, 2, 3, 4], 0.5), 2.5) and close(bl.quantile([10], 0.9), 10)
    assert bl.pairs("submit 31.19, memory sync 14.33, <15.5 ms 0, over 40 2") == [
        ("submit", 31.19), ("memory sync", 14.33), ("<15.5 ms", 0.0), ("over 40", 2.0)]
    with tempfile.TemporaryDirectory(prefix="benchlog-") as tmp:
        a = Path(tmp) / "a"
        a.mkdir()
        (a / "log.txt").write_text(log([20.0, 22.0, 30.0, 31.0, 32.0], 24.0, 12.0, timing=True), encoding="utf-8")
        b = Path(tmp) / "b.txt"
        b.write_text(log([20.0, 22.0, 40.0, 41.0, 42.0], 18.0, 6.0), encoding="utf-8")

        r = bl.read(bl.log_path(str(a)), draws=800, skip=2)   # (a folder: its log.txt)
        assert r["version"].startswith("pre-1.1.0 70d9583") and r["device"] == "AMD Radeon RX 6700 XT"
        assert r["output"] == "2560x1440" and r["headless"]
        # the title's sample and the first two in play (the area still loading in) are left out
        assert r["samples"] == 6 and r["play_samples"] == 3, (r["samples"], r["play_samples"])
        assert close(r["fps"]["median"], 31.0) and close(r["fps"]["min"], 30.0)
        assert close(r["gpu_ms"], 24.0), r["gpu_ms"]   # (not the title's 1.0)
        assert close(r["replay_ms"]["memory sync"], 12.0) and close(r["replay_ms"]["texture lookups"], 0.09)
        assert close(r["threads"]["Presentation Thread"], 58.0) and close(r["threads"]["all"], 262.0), dict(r["threads"])
        assert close(r["intervals"]["30-40"], 80.0) and close(r["intervals"]["over 40"], 10.0)
        assert close(r["vcores"]["waits_per_s"], 200.0) and close(r["vcores"]["mean_wait_us"], 40.0)
        assert r["stalls"] == 1 and r["stalls_in_play"] == 0
        assert r["pipelines_in_play"] == {"made": 2, "ms": 20}, r["pipelines_in_play"]
        assert close(r["capture_ms_per_submit"], 1.96) and r["vram_mb"]["max"] == 7000 and r["working_set_peak_mb"] == 10147
        # the span list goes on past another thread's line, and the pass totals above it are not spans
        assert close(r["pass_timing_ms"], 26.0) and list(r["spans_ms"]) == [
            "dispatch  prog 9dc98871eb7fc101", "msync copies (CPU pages from a capture -> mirror)"], list(r["spans_ms"])
        assert close(r["spans_ms"]["dispatch  prog 9dc98871eb7fc101"], 6.4)
        text = bl.report(r)
        assert "fps, median" in text and "replay: memory sync" in text and "GPU span: dispatch" in text, text

        rb = bl.read(str(b))
        assert rb["pass_timing_ms"] is None and not rb["spans_ms"]
        both = bl.compare(r, rb)
        row = next(x for x in both.splitlines() if "fps, median" in x)
        assert "31.0" in row and "41.0" in row and "+32.3%" in row, row
        row = next(x for x in both.splitlines() if "replay: memory sync" in x)
        assert "-50.0%" in row, row
        assert "do not compare" in both   # (one run timed its passes, the other did not)

        empty = Path(tmp) / "title.txt"
        empty.write_text(stat(120, 60.0, 5, 1, 1) + "\n", encoding="utf-8")
        assert "no sample in play" in bl.report(bl.read(str(empty)))
    print("PASS: a log's numbers are read in play only, and two runs line up")


if __name__ == "__main__":
    main()
