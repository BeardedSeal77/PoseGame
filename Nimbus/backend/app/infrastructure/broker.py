"""Simple in-memory pub/sub broker for hub events (threaded version)."""

from __future__ import annotations

import time
from typing import Any, Dict, List


class PubSubBroker:
    """Simple pub/sub broker without async dependencies"""
    
    def __init__(self) -> None:
        self._topics: Dict[str, Dict[str, Any]] = {}

    def publish(self, topic: str, message: Any) -> None:
        timestamp = time.time()
        self._topics[topic] = {"data": message, "timestamp": timestamp}

    def get_latest(self, topic: str) -> Dict[str, Any] | None:
        return self._topics.get(topic)

    def list_topics(self) -> List[Dict[str, Any]]:
        return [
            {
                "topic": topic,
                "timestamp": payload.get("timestamp", 0),
                "has_data": payload.get("data") is not None,
            }
            for topic, payload in self._topics.items()
        ]
