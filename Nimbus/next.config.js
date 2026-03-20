/** @type {import('next').NextConfig} */
const nextConfig = {
    rewrites: async () => {
        return [
            {
                source: '/api/:path*',
                destination:
                    process.env.NODE_ENV === 'development'
                        ? 'http://127.0.0.1:8000/api/:path*'
                        : '/api/:path*',
            },
            {
                source: '/video/:path*',
                destination:
                    process.env.NODE_ENV === 'development'
                        ? 'http://127.0.0.1:8000/video/:path*'
                        : '/video/:path*',
            },
        ]
    },
}

module.exports = nextConfig
