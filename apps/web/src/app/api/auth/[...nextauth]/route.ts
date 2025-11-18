import NextAuth from "next-auth";
import Keycloak from "next-auth/providers/keycloak";
import type { NextAuthOptions } from "next-auth";
import type { JWT } from "next-auth/jwt";

const issuer =
    process.env.KEYCLOAK_ISSUER ?? "http://keycloak:8082/realms/production";
console.log("Keycloak Issuer:", issuer);
const scope =
    process.env.KEYCLOAK_SCOPE ??
    "openid profile email offline_access products.read products.write";
console.log("Keycloak Scope:", scope);
const tokenEndpoint = `${issuer}/protocol/openid-connect/token`;
console.log("Keycloak Token Endpoint:", tokenEndpoint);

async function refreshAccessToken(token: JWT): Promise<JWT> {
    if (!token.refreshToken) {
        return { ...token, error: "MissingRefreshToken" };
    }

    const body = new URLSearchParams({
        client_id: process.env.KEYCLOAK_CLIENT_ID ?? "gateway",
        client_secret: process.env.KEYCLOAK_CLIENT_SECRET ?? "gateway-secret",
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
    secret: process.env.NEXTAUTH_SECRET,
    session: { strategy: "jwt" },
    providers: [
        Keycloak({
            clientId: process.env.KEYCLOAK_CLIENT_ID ?? "gateway",
            clientSecret:
                process.env.KEYCLOAK_CLIENT_SECRET ?? "gateway-secret",
            issuer, // this is enough
            authorization: {
                params: { scope },
            },
        }),
    ],
    callbacks: {
        async jwt({ token, account }) {
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
