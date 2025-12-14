import NextAuth from "next-auth";
import Keycloak from "next-auth/providers/keycloak";
import type { NextAuthOptions } from "next-auth";
import type { JWT } from "next-auth/jwt";

const scope =
    process.env.KEYCLOAK_SCOPE ??
    "openid profile email offline_access products.read products.write";

/**
 * Runtime env helper: fail loudly if a required env var is missing.
 * This is only called inside request/refresh flows, not at module load.
 */
function envOrThrow(name: string): string {
    const value = process.env[name];
    if (!value) {
        throw new Error(`Missing ${name}. Set it in environment for NextAuth.`);
    }
    return value;
}

async function refreshAccessToken(token: JWT): Promise<JWT> {
    if (!token.refreshToken) {
        return { ...token, error: "MissingRefreshToken" };
    }

    const issuer = envOrThrow("KEYCLOAK_ISSUER");
    const clientId = envOrThrow("KEYCLOAK_CLIENT_ID");
    const clientSecret = envOrThrow("KEYCLOAK_CLIENT_SECRET");

    const tokenEndpoint = `${issuer}/protocol/openid-connect/token`;

    const body = new URLSearchParams({
        client_id: clientId,
        client_secret: clientSecret,
        grant_type: "refresh_token",
        refresh_token: token.refreshToken as string,
    });

    const response = await fetch(tokenEndpoint, {
        method: "POST",
        headers: { "Content-Type": "application/x-www-form-urlencoded" },
        body: body.toString(),
    });

    if (!response.ok) {
        return { ...token, error: "RefreshAccessTokenError" };
    }

    const refreshed = await response.json();
    const expiresAt =
        Math.floor(Date.now() / 1000) + (refreshed.expires_in ?? 0);

    return {
        ...token,
        accessToken: refreshed.access_token,
        refreshToken: refreshed.refresh_token ?? token.refreshToken,
        expiresAt,
        error: undefined,
    };
}

const config: NextAuthOptions = {
    // This can be undefined at build time; we enforce presence via envOrThrow
    // when the auth flow actually runs.
    secret: process.env.NEXTAUTH_SECRET,
    session: { strategy: "jwt" },
    providers: [
        Keycloak({
            // Use empty string fallback to avoid TS complaining; real validation
            // happens via envOrThrow at runtime.
            clientId: process.env.KEYCLOAK_CLIENT_ID ?? "",
            clientSecret: process.env.KEYCLOAK_CLIENT_SECRET ?? "",
            issuer: process.env.KEYCLOAK_ISSUER,
            authorization: {
                params: { scope },
            },
        }),
    ],
    callbacks: {
        async jwt({ token, account }) {
            // Ensure critical env is present when JWT flow is actually used.
            envOrThrow("NEXTAUTH_SECRET");
            envOrThrow("KEYCLOAK_ISSUER");
            envOrThrow("KEYCLOAK_CLIENT_ID");
            envOrThrow("KEYCLOAK_CLIENT_SECRET");

            if (account) {
                return {
                    ...token,
                    accessToken: account.access_token,
                    refreshToken: account.refresh_token,
                    expiresAt:
                        account.expires_at ??
                        Math.floor(Date.now() / 1000) +
                            Number(account.expires_in ?? 0),
                    error: undefined,
                };
            }

            if (
                !token.expiresAt ||
                Date.now() / 1000 < (token.expiresAt as number) - 60
            ) {
                return token;
            }

            return refreshAccessToken(token);
        },
        async session({ session, token }) {
            session.accessToken = token.accessToken as string | undefined;
            session.error = token.error as string | undefined;
            return session;
        },
    },
};

const handler = NextAuth(config);

export { handler as GET, handler as POST };
