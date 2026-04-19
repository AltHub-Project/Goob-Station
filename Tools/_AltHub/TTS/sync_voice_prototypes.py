#!/usr/bin/env python3
# SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>
#
# SPDX-License-Identifier: MIT

from __future__ import annotations

import argparse
import json
from dataclasses import dataclass
from pathlib import Path
from urllib.request import urlopen


DEFAULT_API_URL = "https://nttsfuckrkn.fdev.team/api/v1/tts/speakers"
DEFAULT_OUTPUT = Path("Resources/Prototypes/_AltHub/tts_voices.yml")


@dataclass(frozen=True)
class VoiceRecord:
    prototype_id: str
    name: str
    speaker: str
    gender: str
    description: str
    source: str


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Synchronize AltHub TTS voice prototypes from the current /N/TTS speakers API.",
    )
    parser.add_argument("--api-url", default=DEFAULT_API_URL)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    return parser.parse_args()


def fetch_payload(api_url: str) -> dict:
    with urlopen(api_url, timeout=30) as response:
        return json.load(response)


def to_voice_records(payload: dict) -> list[VoiceRecord]:
    records: list[VoiceRecord] = []

    for voice in payload.get("voices", []):
        provider_gender = (voice.get("gender") or "").strip().lower()
        if provider_gender not in {"male", "female"}:
            continue

        gender = "Male" if provider_gender == "male" else "Female"
        name = (voice.get("name") or "").strip()
        description = (voice.get("description") or "").strip()
        source = (voice.get("source") or "").strip() or "/N/TTS"

        for speaker in voice.get("speakers", []):
            speaker_id = str(speaker).strip()
            if not speaker_id:
                continue

            records.append(
                VoiceRecord(
                    prototype_id=speaker_id,
                    name=name or speaker_id,
                    speaker=speaker_id,
                    gender=gender,
                    description=description,
                    source=source,
                )
            )

    records.sort(key=lambda item: (item.source.casefold(), item.gender, item.name.casefold(), item.speaker))
    return records


def yaml_string(value: str) -> str:
    return json.dumps(value, ensure_ascii=False)


def render_yaml(records: list[VoiceRecord], api_url: str) -> str:
    lines = [
        "# SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>",
        "#",
        "# SPDX-License-Identifier: MIT",
        f"# Generated from {api_url}",
        "",
    ]

    for index, voice in enumerate(records):
        if index:
            lines.append("")

        lines.extend(
            [
                "- type: ttsVoice",
                f"  id: {voice.prototype_id}",
                f"  name: {yaml_string(voice.name)}",
                f"  speaker: {voice.speaker}",
                f"  gender: {voice.gender}",
                f"  description: {yaml_string(voice.description)}",
                f"  source: {yaml_string(voice.source)}",
                "  roundStart: true",
                "  adminAnnounceRoundPool: true",
            ]
        )

    lines.append("")
    return "\n".join(lines)


def main() -> None:
    args = parse_args()
    payload = fetch_payload(args.api_url)
    records = to_voice_records(payload)
    text = render_yaml(records, args.api_url)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(text, encoding="utf-8")

    print(f"Wrote {len(records)} voice prototypes to {args.output}")


if __name__ == "__main__":
    main()
