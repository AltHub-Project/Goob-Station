#!/usr/bin/env python3
# SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>
#
# SPDX-License-Identifier: MIT

from __future__ import annotations

import argparse
import os
import sys
from time import monotonic, sleep
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from pathlib import Path


PREVIEW_PHRASE = "Проверка голоса. Космическая станция АльтХаб на связи."


@dataclass(frozen=True)
class Voice:
    voice_id: str
    speaker: str


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Generate local preview ogg files for AltHub TTS voices."
    )
    parser.add_argument(
        "--api-url",
        default=os.environ.get("ALTHUB_TTS_API_URL", "https://nttsfuckrkn.fdev.team"),
        help="Base /N/TTS API URL. Defaults to ALTHUB_TTS_API_URL or https://nttsfuckrkn.fdev.team",
    )
    parser.add_argument(
        "--token",
        default=os.environ.get("ALTHUB_TTS_API_TOKEN"),
        help="Bearer token. Defaults to ALTHUB_TTS_API_TOKEN",
    )
    parser.add_argument(
        "--voices-file",
        default=Path(__file__).resolve().parents[3] / "Resources" / "Prototypes" / "_AltHub" / "tts_voices.yml",
        type=Path,
        help="Path to tts_voices.yml",
    )
    parser.add_argument(
        "--output-dir",
        default=Path(__file__).resolve().parents[3] / "Resources" / "Audio" / "_AltHub" / "TTS" / "Previews",
        type=Path,
        help="Directory for generated preview ogg files",
    )
    parser.add_argument(
        "--phrase",
        default=PREVIEW_PHRASE,
        help="Preview phrase text",
    )
    parser.add_argument(
        "--overwrite",
        action="store_true",
        help="Regenerate previews even if the target file already exists",
    )
    parser.add_argument(
        "--request-interval-seconds",
        type=float,
        default=1.0,
        help="Minimum delay between preview requests. Defaults to 1 second",
    )
    return parser.parse_args()


def load_voices(path: Path) -> list[Voice]:
    voices: list[Voice] = []
    current_id: str | None = None
    current_speaker: str | None = None
    in_voice_block = False

    for raw_line in path.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#"):
            continue

        if line.startswith("- type:"):
            if in_voice_block and current_id and current_speaker:
                voices.append(Voice(current_id, current_speaker))

            in_voice_block = line == "- type: ttsVoice"
            current_id = None
            current_speaker = None
            continue

        if not in_voice_block:
            continue

        if line.startswith("id:"):
            current_id = line.split(":", 1)[1].strip()
            continue

        if line.startswith("speaker:"):
            current_speaker = line.split(":", 1)[1].strip()
            continue

    if in_voice_block and current_id and current_speaker:
        voices.append(Voice(current_id, current_speaker))

    return voices


def build_request_url(api_url: str, speaker: str, phrase: str) -> str:
    base = api_url.rstrip("/")
    if not base.endswith("/api/v1/tts"):
        base = f"{base}/api/v1/tts"

    query = urllib.parse.urlencode(
        {
            "speaker": speaker,
            "text": phrase,
            "ext": "ogg",
        }
    )
    return f"{base}?{query}"


def fetch_preview(api_url: str, token: str, speaker: str, phrase: str) -> bytes:
    request = urllib.request.Request(build_request_url(api_url, speaker, phrase))
    request.add_header("Authorization", f"Bearer {token}")

    with urllib.request.urlopen(request, timeout=30) as response:
        return response.read()


def wait_for_request_window(next_request_time: float) -> float:
    now = monotonic()
    if now < next_request_time:
        sleep(next_request_time - now)

    return monotonic()


def is_rate_limited(error: urllib.error.HTTPError) -> bool:
    if error.code == 429:
        return True

    reason = str(error.reason).lower()
    if "too many requests" in reason or "rate limit" in reason:
        return True

    try:
        body = error.read().decode("utf-8", errors="ignore").lower()
    except Exception:
        body = ""

    return "too many requests" in body or "rate limit" in body


def main() -> int:
    args = parse_args()

    if not args.token:
        print("Missing bearer token. Provide --token or ALTHUB_TTS_API_TOKEN.", file=sys.stderr)
        return 1

    voices = load_voices(args.voices_file)
    if not voices:
        print(f"No voices found in {args.voices_file}", file=sys.stderr)
        return 1

    args.output_dir.mkdir(parents=True, exist_ok=True)
    request_interval_seconds = max(0.0, args.request_interval_seconds)
    next_request_time = 0.0

    for voice in voices:
        output_path = args.output_dir / f"{voice.voice_id}.ogg"
        if output_path.exists() and not args.overwrite:
            print(f"skip {voice.voice_id}: exists")
            continue

        while True:
            wait_for_request_window(next_request_time)
            print(f"generate {voice.voice_id} -> {output_path}")

            try:
                data = fetch_preview(args.api_url, args.token, voice.speaker, args.phrase)
            except urllib.error.HTTPError as error:
                if not is_rate_limited(error):
                    raise

                print(
                    f"rate limited while generating {voice.voice_id}, waiting 1 second before retry",
                    file=sys.stderr,
                )
                next_request_time = monotonic() + max(1.0, request_interval_seconds)
                sleep(1)
                continue

            output_path.write_bytes(data)
            next_request_time = monotonic() + request_interval_seconds
            break

    print(f"generated previews for {len(voices)} voices")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
