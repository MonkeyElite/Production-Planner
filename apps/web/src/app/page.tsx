"use client";

import { useCallback, useEffect, useState } from "react";
import { signIn, signOut, useSession } from "next-auth/react";

interface Product {
    id: string;
    name: string;
    description: string;
    price: number;
    isActive?: boolean;
}

interface ProductResponse {
    id?: string;
    Id?: string;
    name?: string;
    Name?: string;
    description?: string;
    Description?: string;
    price?: number;
    Price?: number;
    isActive?: boolean;
    IsActive?: boolean;
}

const gatewayUrl = process.env.NEXT_PUBLIC_GATEWAY_URL ?? "";

export default function Home() {
    const { data: session, status } = useSession();
    const [products, setProducts] = useState<Product[]>([]);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const loadProducts = useCallback(async () => {
        if (!gatewayUrl) {
            setError("NEXT_PUBLIC_GATEWAY_URL is not configured.");
            return;
        }

        if (!session?.accessToken) {
            setError("Missing access token. Please sign in again.");
            return;
        }

        setLoading(true);
        setError(null);

        try {
            const response = await fetch(`${gatewayUrl}/api/products`, {
                headers: {
                    Authorization: `Bearer ${session.accessToken}`,
                },
                cache: "no-store",
            });

            if (!response.ok) {
                const detail = await response.text();
                const message = response.status === 403
                    ? "Gateway blocked the request. Complete MFA in Keycloak to continue."
                    : `Request failed (${response.status}). ${detail}`;
                throw new Error(message);
            }

            const payload: unknown = await response.json();
            if (!Array.isArray(payload)) {
                throw new Error("Unexpected response payload.");
            }

            const normalized: Product[] = (payload as ProductResponse[]).map((item) => ({
                id: item.id ?? item.Id ?? crypto.randomUUID(),
                name: item.name ?? item.Name ?? "Unnamed product",
                description: item.description ?? item.Description ?? "",
                price: Number(item.price ?? item.Price ?? 0),
                isActive: item.isActive ?? item.IsActive ?? true,
            }));

            setProducts(normalized);
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : "Failed to load products.";
            setProducts([]);
            setError(message);
        } finally {
            setLoading(false);
        }
    }, [session?.accessToken]);

    useEffect(() => {
        if (status === "authenticated") {
            void loadProducts();
        }

        if (status === "unauthenticated") {
            setProducts([]);
            setLoading(false);
        }
    }, [status, loadProducts]);

    useEffect(() => {
        if (session?.error) {
            setError(session.error);
        }
    }, [session?.error]);

    const isAuthenticated = status === "authenticated";
    const sessionLoading = status === "loading";
    const hasProducts = products.length > 0;
    const displayName = session?.user?.name ?? session?.user?.email ?? "";

    return (
        <div className="font-sans grid grid-rows-[20px_1fr_20px] items-center justify-items-center min-h-screen p-8 pb-20 gap-16 sm:p-20">
            <main className="flex flex-col gap-8 row-start-2 items-center sm:items-start w-full max-w-3xl">
                <div className="flex w-full flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                    <div>
                        <h1 className="text-2xl font-bold">Products</h1>
                        <p className="text-sm text-gray-600">
                            {sessionLoading && "Checking your session..."}
                            {isAuthenticated && !sessionLoading && (
                                <span>
                                    Signed in{displayName && ` as ${displayName}`}.
                                </span>
                            )}
                            {!isAuthenticated && !sessionLoading && "Sign in to view inventory."}
                        </p>
                    </div>
                    <div className="flex gap-2">
                        <button
                            className="rounded border border-gray-300 px-3 py-1 text-sm"
                            onClick={() => signIn("keycloak")}
                            disabled={sessionLoading}
                        >
                            Sign in
                        </button>
                        <button
                            className="rounded border border-gray-300 px-3 py-1 text-sm"
                            onClick={() => signOut({ callbackUrl: "/" })}
                            disabled={!isAuthenticated || sessionLoading}
                        >
                            Sign out
                        </button>
                        <button
                            className="rounded bg-blue-600 px-3 py-1 text-sm font-semibold text-white disabled:bg-blue-300"
                            onClick={() => void loadProducts()}
                            disabled={!isAuthenticated || loading}
                        >
                            Refresh
                        </button>
                    </div>
                </div>

                {(loading || sessionLoading) && (
                    <p className="text-gray-500">Loading products...</p>
                )}

                {!loading && error && (
                    <p className="text-red-600" role="alert">
                        {error}
                    </p>
                )}

                {isAuthenticated && !loading && !error && !hasProducts && (
                    <p className="text-gray-500">No products found.</p>
                )}

                {hasProducts && !loading && !error && (
                    <ul className="w-full divide-y divide-gray-200">
                        {products.map((product) => (
                            <li key={product.id} className="py-3">
                                <div className="flex items-center justify-between">
                                    <div>
                                        <div className="font-semibold">{product.name}</div>
                                        <div className="text-sm text-gray-500">
                                            {product.description || "No description"}
                                        </div>
                                    </div>
                                    <div className="text-sm font-mono">
                                        ${product.price.toFixed(2)}
                                    </div>
                                </div>
                                {!product.isActive && (
                                    <div className="text-xs uppercase text-orange-600">Inactive</div>
                                )}
                            </li>
                        ))}
                    </ul>
                )}
            </main>
        </div>
    );
}
