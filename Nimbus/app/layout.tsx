import type { Metadata } from 'next'
import './globals.css'

export const metadata: Metadata = {
  title: 'Nimbus Drone Control',
  description: 'AI-powered drone control system with YOLO object detection',
}

export default function RootLayout({
  children,
}: {
  children: React.ReactNode
}) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  )
}
