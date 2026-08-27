"""CI smoke: ffmpeg fade + wipeleft xfade matching FfmpegService.XfadeJoinAsync.

Does not check PARKING_LOT user-validated Export (needs a human). Proves the
filter graph Space burns on mixed-source V1 export actually runs.
"""
from __future__ import annotations

import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FFMPEG_CS = ROOT / "src/NetieSpace/Services/FfmpegService.cs"
GRAPH_CS = ROOT / "src/NetieSpace/Services/FfmpegExportGraph.cs"
TIMELINE = ROOT / "src/NetieSpace/Services/Timeline/TimelineProject.cs"


def _have_ffmpeg() -> bool:
    return shutil.which("ffmpeg") is not None and shutil.which("ffprobe") is not None


def _run(args: list[str]) -> None:
    subprocess.run(args, check=True, capture_output=True, text=True)


def _probe_duration(path: Path) -> float:
    out = subprocess.check_output(
        [
            "ffprobe",
            "-v",
            "error",
            "-show_entries",
            "format=duration",
            "-of",
            "default=noprint_wrappers=1:nokey=1",
            str(path),
        ],
        text=True,
    ).strip()
    return float(out)


class FfmpegNleExportTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.missing = not _have_ffmpeg()
        if cls.missing and os.environ.get("CI"):
            raise AssertionError("ffmpeg/ffprobe required in CI for NLE export smoke")

    def test_cs_wipe_graph_still_wipeleft(self):
        ffmpeg = FFMPEG_CS.read_text(encoding="utf-8")
        graph = GRAPH_CS.read_text(encoding="utf-8")
        timeline = TIMELINE.read_text(encoding="utf-8")
        self.assertIn('public const string XfadeWipeName = "wipeleft"', timeline)
        self.assertIn("FfmpegExportGraph.XfadeWithAudio", ffmpeg)
        self.assertIn("FfmpegExportGraph.ClipVideoFilters", ffmpeg)
        self.assertIn("FfmpegExportGraph.PadBlackAfterVideo", ffmpeg)
        self.assertIn("FfmpegExportGraph.EscapeFilterPath(safeSrt)", ffmpeg)
        self.assertIn("IsBlackPlaceholder", ffmpeg)
        self.assertIn("_ns_hole_", ffmpeg)
        self.assertIn("GetTempPath()", ffmpeg)
        self.assertIn("_ns_burn_", ffmpeg)
        self.assertIn("_ns_txt_", ffmpeg)
        self.assertIn("_ns_lut_", ffmpeg)
        self.assertIn("CopyFilterFileToTemp", ffmpeg)
        self.assertNotIn('lut3d=\'{clip.LutPath.Replace', ffmpeg)
        self.assertIn("FfmpegExportGraph.ConcatDemuxerLine", ffmpeg)
        self.assertNotIn("\"'\\''\"", ffmpeg)
        self.assertIn("ConcatDemuxerLine", graph)
        burn_txt = ffmpeg.index("BurnTextOverlayAsync")
        burn_txt_end = ffmpeg.index("ToTimedSrt")
        self.assertIn("GetTempPath()", ffmpeg[burn_txt:burn_txt_end])
        self.assertNotIn("GetDirectoryName(output)", ffmpeg[burn_txt:burn_txt_end])
        self.assertIn("BlackPadAfterVideo", ffmpeg)
        self.assertIn("tpad=stop_mode=add", graph)
        self.assertIn("BlackPadAfterVideo", timeline)
        self.assertIn("ExportDuration", timeline)
        self.assertIn("xfade=transition=", graph)
        self.assertIn("fade=t=in", graph)
        self.assertIn("fade=t=out", graph)
        self.assertIn("XfadeOffsetSeconds", graph)
        yml = "\n".join(
            p.read_text(encoding="utf-8")
            for p in sorted((ROOT / ".github/workflows").glob("*.yml"))
        )
        self.assertIn("apt-get install -y -qq ffmpeg", yml)
        self.assertIn("FedericoCarboni/setup-ffmpeg@v3", yml)
        self.assertNotIn("choco install ffmpeg", yml)
        self.assertIn("cache: true", yml)
        self.assertIn("dotnet test NetieSpace.sln", yml)

    def test_split_fade_wipe_export_runs(self):
        if self.missing:
            self.skipTest("ffmpeg not installed")
        with tempfile.TemporaryDirectory() as tmp:
            td = Path(tmp)
            a = td / "a.mp4"
            b = td / "b.mp4"
            split = td / "split.mp4"
            faded = td / "faded.mp4"
            out = td / "wipe.mp4"
            _run(
                [
                    "ffmpeg", "-y", "-f", "lavfi", "-i", "color=c=red:s=320x240:d=2",
                    "-f", "lavfi", "-i", "sine=f=440:d=2", "-shortest",
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", str(a),
                ]
            )
            _run(
                [
                    "ffmpeg", "-y", "-f", "lavfi", "-i", "color=c=blue:s=320x240:d=2",
                    "-f", "lavfi", "-i", "sine=f=880:d=2", "-shortest",
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", str(b),
                ]
            )
            # Split / trim: keep 0-1.6s of A (Select then Split analogue).
            _run(
                [
                    "ffmpeg", "-y", "-i", str(a), "-ss", "0", "-t", "1.6",
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", str(split),
                ]
            )
            _run(
                [
                    "ffmpeg", "-y", "-i", str(split),
                    "-vf", "fade=t=in:st=0:d=0.2,fade=t=out:st=1.2:d=0.2",
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", str(faded),
                ]
            )
            scale = (
                "scale=320:240:force_original_aspect_ratio=decrease,"
                "pad=320:240:(ow-iw)/2:(oh-ih)/2,setsar=1,fps=30,format=yuv420p,setpts=PTS-STARTPTS"
            )
            # Match FfmpegService.XfadeJoinAsync: offset = left duration - 0.5s wipe.
            fc = (
                f"[0:v]{scale}[v0];[1:v]{scale}[v1];"
                "[v0][v1]xfade=transition=wipeleft:duration=0.5:offset=1.1[vout];"
                "[0:a]aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo[a0];"
                "[1:a]aformat=sample_fmts=fltp:sample_rates=44100:channel_layouts=stereo[a1];"
                "[a0][a1]acrossfade=d=0.5[aout]"
            )
            _run(
                [
                    "ffmpeg", "-y", "-i", str(faded), "-i", str(b),
                    "-filter_complex", fc,
                    "-map", "[vout]", "-map", "[aout]",
                    "-c:v", "libx264", "-preset", "veryfast", "-crf", "23",
                    "-c:a", "aac", "-movflags", "+faststart", str(out),
                ]
            )
            self.assertTrue(out.exists() and out.stat().st_size > 1000)
            dur = _probe_duration(out)
            # faded 1.6 + b 2 - 0.5 wipe ≈ 3.1s. Hard-cut concat is ~3.6s
            # and used to pass the old 2.5..4.0 window.
            self.assertGreaterEqual(dur, 2.8)
            self.assertLessEqual(dur, 3.4)

    def test_subtitle_burn_accepts_apostrophe_in_path(self):
        if self.missing:
            self.skipTest("ffmpeg not installed")
        graph = GRAPH_CS.read_text(encoding="utf-8")
        self.assertIn('Replace("\'", "\\\\\'")', graph)
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp) / "Mom's clips"
            folder.mkdir()
            video = folder / "clip.mp4"
            srt = folder / "clip.srt"
            out = folder / "burned.mp4"
            _run(
                [
                    "ffmpeg", "-y", "-f", "lavfi", "-i", "color=c=blue:s=320x240:d=0.4",
                    "-f", "lavfi", "-i", "sine=f=440:d=0.4", "-shortest",
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", str(video),
                ]
            )
            srt.write_text("1\n00:00:00,000 --> 00:00:00,400\nhi\n", encoding="utf-8")
            safe = Path(tmp) / "safe.srt"
            safe.write_bytes(srt.read_bytes())
            escaped = str(safe).replace("\\", "/").replace(":", "\\:")
            _run(
                [
                    "ffmpeg", "-y", "-i", str(video),
                    "-vf", f"subtitles='{escaped}'",
                    "-c:v", "libx264", "-preset", "veryfast", "-crf", "23",
                    "-c:a", "copy", "-movflags", "+faststart", str(out),
                ]
            )
            self.assertTrue(out.exists() and out.stat().st_size > 500)

    def test_drawtext_from_temp_not_output_dir(self):
        if self.missing:
            self.skipTest("ffmpeg not installed")
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp) / "Mom's clips"
            folder.mkdir()
            video = folder / "clip.mp4"
            out = folder / "titled.mp4"
            text = Path(tmp) / "safe.txt"
            text.write_text("Title", encoding="utf-8")
            _run(
                [
                    "ffmpeg", "-y", "-f", "lavfi", "-i", "color=c=blue:s=320x240:d=0.4",
                    "-c:v", "libx264", "-pix_fmt", "yuv420p", str(video),
                ]
            )
            escaped = str(text).replace("\\", "/").replace(":", "\\:")
            _run(
                [
                    "ffmpeg", "-y", "-i", str(video),
                    "-vf", f"drawtext=textfile='{escaped}':fontsize=24:fontcolor=white:x=10:y=10",
                    "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", str(out),
                ]
            )
            self.assertTrue(out.exists() and out.stat().st_size > 500)

    def test_concat_list_uses_file_name_in_apostrophe_folder(self):
        if self.missing:
            self.skipTest("ffmpeg not installed")
        graph = GRAPH_CS.read_text(encoding="utf-8")
        self.assertIn("ConcatDemuxerLine", graph)
        self.assertIn("LastIndexOf('\\\\')", graph)
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp) / "Mom's clips"
            folder.mkdir()
            a = folder / "_ns_a.mp4"
            b = folder / "_ns_b.mp4"
            lst = folder / "_ns_tl.txt"
            out = folder / "concat.mp4"
            for path, color in ((a, "blue"), (b, "red")):
                _run(
                    [
                        "ffmpeg", "-y", "-f", "lavfi", "-i",
                        f"color=c={color}:s=320x240:d=0.4",
                        "-c:v", "libx264", "-pix_fmt", "yuv420p", str(path),
                    ]
                )
            # Absolute '\' escape opens Moms. File names next to the list work.
            lst.write_text("file '_ns_a.mp4'\nfile '_ns_b.mp4'\n", encoding="utf-8")
            _run(
                [
                    "ffmpeg", "-y", "-f", "concat", "-safe", "0",
                    "-i", str(lst),
                    "-c:v", "libx264", "-preset", "veryfast", str(out),
                ]
            )
            self.assertTrue(out.exists() and out.stat().st_size > 500)
            self.assertGreaterEqual(_probe_duration(out), 0.7)


if __name__ == "__main__":
    unittest.main()
