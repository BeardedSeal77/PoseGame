'use client';

import { useState, useEffect } from 'react';

export default function DroneVideoPage() {
  const [isConnected, setIsConnected] = useState(false);
  const [stats, setStats] = useState({ fps: 0, frameCount: 0 });

  // Poll stats every second
  useEffect(() => {
    const interval = setInterval(async () => {
      try {
        const response = await fetch('http://localhost:8000/api/stats');
        const data = await response.json();
        setStats({
          fps: data.fps || 0,
          frameCount: data.frame_count || 0
        });
        setIsConnected(data.frame_count > 0);
      } catch (error) {
        console.error('Failed to fetch stats:', error);
        setIsConnected(false);
      }
    }, 1000);

    return () => clearInterval(interval);
  }, []);

  return (
    <div className="min-h-screen bg-gray-900 text-white p-8">
      <div className="max-w-6xl mx-auto">
        {/* Header */}
        <div className="mb-8">
          <h1 className="text-4xl font-bold mb-2">🚁 Drone Camera Feed</h1>
          <div className="flex items-center gap-4">
            <div className="flex items-center gap-2">
              <div className={`w-3 h-3 rounded-full ${isConnected ? 'bg-green-500 animate-pulse' : 'bg-red-500'}`} />
              <span className="text-sm">{isConnected ? 'Connected' : 'Disconnected'}</span>
            </div>
            <div className="text-sm text-gray-400">
              FPS: {stats.fps.toFixed(1)} | Frames: {stats.frameCount}
            </div>
          </div>
        </div>

        {/* Video Feed */}
        <div className="relative bg-black rounded-lg overflow-hidden shadow-2xl">
          <div className="aspect-video">
            <img
              src="http://localhost:8000/video/stream"
              alt="Drone Camera Feed"
              className="w-full h-full object-contain"
              onError={(e) => {
                console.error('Failed to load video stream');
              }}
            />
          </div>
          
          {/* Overlay - No video message */}
          {!isConnected && (
            <div className="absolute inset-0 flex items-center justify-center bg-black bg-opacity-75">
              <div className="text-center">
                <div className="text-6xl mb-4">📡</div>
                <div className="text-xl font-semibold mb-2">Waiting for Drone Connection</div>
                <div className="text-gray-400">Start Webots simulation with mavic2pro_python controller</div>
              </div>
            </div>
          )}
        </div>

        {/* Instructions */}
        <div className="mt-8 bg-gray-800 rounded-lg p-6">
          <h2 className="text-xl font-bold mb-4">🎮 Instructions</h2>
          <div className="space-y-2 text-sm text-gray-300">
            <p><strong>1. Start Flask Backend:</strong> <code className="bg-gray-700 px-2 py-1 rounded">python -m backend.app.hub_threaded</code></p>
            <p><strong>2. Start Webots Simulation:</strong> Open <code className="bg-gray-700 px-2 py-1 rounded">sim-webot/mavic/worlds/mavic_2_pro.wbt</code></p>
            <p><strong>3. Verify Controller:</strong> Drone should use <code className="bg-gray-700 px-2 py-1 rounded">mavic2pro_python</code> controller</p>
            <p><strong>4. Control Drone:</strong> Use arrow keys (forward/back/left/right), numpad 1/3 (altitude), 7/9 (yaw)</p>
          </div>
        </div>

        {/* Stats Panel */}
        <div className="mt-4 grid grid-cols-3 gap-4">
          <div className="bg-gray-800 rounded-lg p-4">
            <div className="text-gray-400 text-sm mb-1">Frame Rate</div>
            <div className="text-2xl font-bold">{stats.fps.toFixed(1)} FPS</div>
          </div>
          <div className="bg-gray-800 rounded-lg p-4">
            <div className="text-gray-400 text-sm mb-1">Total Frames</div>
            <div className="text-2xl font-bold">{stats.frameCount.toLocaleString()}</div>
          </div>
          <div className="bg-gray-800 rounded-lg p-4">
            <div className="text-gray-400 text-sm mb-1">Status</div>
            <div className="text-2xl font-bold">{isConnected ? '✅ Live' : '⏸️ Waiting'}</div>
          </div>
        </div>
      </div>
    </div>
  );
}
