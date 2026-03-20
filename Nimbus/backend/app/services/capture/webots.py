"""Video source abstraction for Webots controller."""

from __future__ import annotations

import asyncio
import logging
from typing import Optional

logger = logging.getLogger(__name__)


class WebotsSource:
    def __init__(self, ws_url: str = "ws://localhost:8080/video"):
        self.ws_url = ws_url
        self.is_running = False
        self._ws_task: Optional[asyncio.Task] = None

    async def start(self) -> bool:
        if self.is_running:
            logger.warning("Webots source already running")
            return True

        self.is_running = True
        self._ws_task = asyncio.create_task(self._ws_loop())
        logger.info("Webots source started")
        return True

    async def stop(self) -> None:
        self.is_running = False
        if self._ws_task:
            self._ws_task.cancel()
            try:
                await self._ws_task
            except asyncio.CancelledError:
                pass

        logger.info("Webots source stopped")

    async def _ws_loop(self) -> None:
        # Placeholder: implement WebSocket connection to Webots controller
        while self.is_running:
            await asyncio.sleep(0.1)

    async def read_frame(self) -> Optional[bytes]:
        # Placeholder: return frame from Webots
        return None
