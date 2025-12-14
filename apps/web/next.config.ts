//@ts-nocheck

import type { NextConfig } from "next";

const nextConfig: NextConfig = {
    output: "standalone",
    async headers() {
        const isProd = process.env.NODE_ENV === "production";

        const baseDirectives = [
            // "default-src 'self'",
            // "base-uri 'self'",
            // "form-action 'self'",
            // "frame-ancestors 'none'",
            // "object-src 'none'",
            // "img-src 'self' data: blob: https:",
            // "font-src 'self' data:",
            // "style-src 'self' 'unsafe-inline'",
            // "connect-src 'self' https:",
        ];

        const scriptDirective = isProd
            ? "script-src 'self' 'unsafe-inline'"
            : "script-src 'self' 'unsafe-inline' 'unsafe-eval'";

        const csp = [
            ...baseDirectives,
            scriptDirective,
            ...(isProd ? ["upgrade-insecure-requests"] : []),
        ].join("; ");

        return [
            {
                source: "/(.*)",
                headers: [
                    { key: "Content-Security-Policy", value: csp },
                    { key: "Referrer-Policy", value: "same-origin" },
                    { key: "X-Content-Type-Options", value: "nosniff" },
                    { key: "X-Frame-Options", value: "DENY" },
                    { key: "X-Permitted-Cross-Domain-Policies", value: "none" },
                ],
            },
        ];
    },
};

export default nextConfig;
