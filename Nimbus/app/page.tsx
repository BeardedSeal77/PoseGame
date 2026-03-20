'use client'

import { useState, useEffect } from 'react'

interface Stats {
  frame_count: number
  detection_count: number
  successful_detections: number
  video_source: string
  detection_rate: number
}

export default function Home() {
  const [stats, setStats] = useState<Stats | null>(null)
  const [videoSource, setVideoSource] = useState('webcam')
  const [targetObject, setTargetObject] = useState('person')
  const [isUpdatingTarget, setIsUpdatingTarget] = useState(false)

  useEffect(() => {
    const fetchStats = async () => {
      try {
        const response = await fetch('/api/stats')
        const data = await response.json()
        setStats(data)
        
        // Sync video source with backend state
        if (data.video_source && data.video_source !== videoSource) {
          setVideoSource(data.video_source)
        }
      } catch (error) {
        console.error('Failed to fetch stats:', error)
      }
    }

    fetchStats()
    const interval = setInterval(fetchStats, 2000)
    return () => clearInterval(interval)
  }, [])

  const switchVideoSource = async (source: string) => {
    try {
      await fetch('/api/video/source', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ source }),
      })
      setVideoSource(source)
    } catch (error) {
      console.error('Failed to switch video source:', error)
    }
  }

  const updateTargetObject = async () => {
    if (!targetObject.trim()) return
    
    setIsUpdatingTarget(true)
    try {
      await fetch('/api/detection/target', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ target: targetObject.trim() }),
      })
    } catch (error) {
      console.error('Failed to update target object:', error)
    } finally {
      setIsUpdatingTarget(false)
    }
  }

  return (
    <div className="min-h-screen bg-gray-900 text-white p-8">
      <div className="max-w-7xl mx-auto">
        <h1 className="text-4xl font-bold mb-8">Nimbus Drone Control</h1>
        
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
          {/* Video Feed */}
          <div className="lg:col-span-2 bg-gray-800 rounded-lg p-4">
            <h2 className="text-2xl font-semibold mb-4">Live Video Feed</h2>
            <div className="relative aspect-video bg-black rounded-lg overflow-hidden">
              <img 
                src="/video/stream" 
                alt="Video stream"
                className="w-full h-full object-contain"
              />
            </div>
            
            {/* Video Source Controls */}
            <div className="mt-4 flex gap-2">
              <button
                onClick={() => switchVideoSource('webcam')}
                className={`px-4 py-2 rounded ${
                  videoSource === 'webcam' 
                    ? 'bg-blue-600 hover:bg-blue-700' 
                    : 'bg-gray-700 hover:bg-gray-600'
                }`}
              >
                Webcam
              </button>
              <button
                onClick={() => switchVideoSource('webots')}
                className={`px-4 py-2 rounded ${
                  videoSource === 'webots' 
                    ? 'bg-blue-600 hover:bg-blue-700' 
                    : 'bg-gray-700 hover:bg-gray-600'
                }`}
              >
                Webots
              </button>
              <button
                onClick={() => switchVideoSource('none')}
                className={`px-4 py-2 rounded ${
                  videoSource === 'none' 
                    ? 'bg-red-600 hover:bg-red-700' 
                    : 'bg-gray-700 hover:bg-gray-600'
                }`}
              >
                Stop
              </button>
            </div>
          </div>

          {/* Stats Panel */}
          <div className="bg-gray-800 rounded-lg p-4">
            <h2 className="text-2xl font-semibold mb-4">Statistics</h2>
            {stats ? (
              <div className="space-y-4">
                <div className="bg-gray-700 rounded p-3">
                  <div className="text-gray-400 text-sm">Video Source</div>
                  <div className="text-xl font-bold capitalize">{stats.video_source}</div>
                </div>
                <div className="bg-gray-700 rounded p-3">
                  <div className="text-gray-400 text-sm">Frame Count</div>
                  <div className="text-xl font-bold">{stats.frame_count}</div>
                </div>
                <div className="bg-gray-700 rounded p-3">
                  <div className="text-gray-400 text-sm">Detections</div>
                  <div className="text-xl font-bold">
                    {stats.successful_detections} / {stats.detection_count}
                  </div>
                </div>
                <div className="bg-gray-700 rounded p-3">
                  <div className="text-gray-400 text-sm">Detection Rate</div>
                  <div className="text-xl font-bold">
                    {(stats.detection_rate * 100).toFixed(1)}%
                  </div>
                </div>
              </div>
            ) : (
              <div className="text-gray-400">Loading stats...</div>
            )}

            {/* Target Object Control */}
            <div className="mt-6 pt-6 border-t border-gray-700">
              <h3 className="text-lg font-semibold mb-3">Target Object</h3>
              <div className="space-y-2">
                <input
                  type="text"
                  value={targetObject}
                  onChange={(e) => setTargetObject(e.target.value)}
                  onKeyPress={(e) => e.key === 'Enter' && updateTargetObject()}
                  placeholder="e.g., person, car, dog"
                  className="w-full px-3 py-2 bg-gray-700 rounded text-white placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-blue-500"
                />
                <button
                  onClick={updateTargetObject}
                  disabled={isUpdatingTarget || !targetObject.trim()}
                  className="w-full px-4 py-2 bg-green-600 hover:bg-green-700 disabled:bg-gray-600 disabled:cursor-not-allowed rounded font-medium transition-colors"
                >
                  {isUpdatingTarget ? 'Updating...' : 'Update Target'}
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
