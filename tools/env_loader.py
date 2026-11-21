"""Load .env from project root for Python tools."""
from __future__ import annotations

import os
from pathlib import Path


def find_project_root(start: Path | None = None) -> Path:
    current = (start or Path(__file__).resolve()).parent
    for candidate in [current, *current.parents]:
        if (candidate / ".env").exists() or (candidate / "convert_all.py").exists():
            return candidate
    return Path(__file__).resolve().parent.parent


def load_env(root: Path | None = None) -> dict[str, str]:
    root = root or find_project_root()
    env_path = root / ".env"
    loaded: dict[str, str] = {}

    if not env_path.exists():
        return loaded

    for line in env_path.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        key = key.strip()
        value = value.strip().strip('"').strip("'")
        loaded[key] = value
        os.environ.setdefault(key, value)

    return loaded


def get_ai_key(env: dict[str, str] | None = None) -> tuple[str, str, str]:
    """Return (api_key, base_url, model) preferring Groq."""
    env = env or load_env()

    if key := env.get("GROQ_API_KEY"):
        return key, "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile"

    if key := env.get("OPENROUTER_API_KEY"):
        return key, "https://openrouter.ai/api/v1", "openai/gpt-4o-mini"

    if key := env.get("SEA_LION_API_KEY"):
        return key, "https://api.sea-lion.ai/v1", "sea-lion/lion-gemini-1.5-flash"

    return "", "", ""


def get_vision_key(env: dict[str, str] | None = None) -> tuple[str, str, str]:
    env = env or load_env()

    if key := env.get("GROQ_API_KEY"):
        return key, "https://api.groq.com/openai/v1", "llama-3.2-90b-vision-preview"

    if key := env.get("OPENROUTER_API_KEY"):
        return key, "https://openrouter.ai/api/v1", "openai/gpt-4o-mini"

    return get_ai_key(env)
