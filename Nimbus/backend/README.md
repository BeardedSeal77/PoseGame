# Nimbus Backend (Hub)

This backend is the **core hub** for the Nimbus system. It runs an async pipeline for video → object detection → depth detection and exposes a thin API server (FastAPI) that is started by the hub.

## What’s here

- `app/hub.py` — main entrypoint (hub lifecycle + pipeline orchestration)
- `app/api_server.py` — FastAPI routes and WebSocket endpoints
- `app/infrastructure/` — message broker + shared state
- `app/pipelines/` — async video pipeline
- `app/services/` — service interfaces + placeholders

## Quick start (dev)

```powershell
# From root directory
python -m venv .venv
.\.venv\Scripts\activate
pip install -r backend\requirements.txt
cd backend
python -m app.hub
```

> The current services are **no‑op stubs**. Replace them with real implementations (YOLO, depth, STT, intent) once ready.
