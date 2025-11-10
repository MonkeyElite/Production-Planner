"use client";

import { useEffect, useState } from "react";

interface Product {
    id: number;
    name: string;
    description: string;
    price: number;
}

export default function Home() {
    const [products, setProducts] = useState<Product[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        const fetchProducts = async () => {
            try {
                // Read from environment variable set at build/run time
                const res = await fetch(
                    `${process.env.NEXT_PUBLIC_GATEWAY_URL}/api/products`
                );
                if (!res.ok) throw new Error(`Request failed: ${res.status}`);
                const data = await res.json();
                setProducts(data);
            } catch (err: unknown) {
                if (err instanceof Error) setError(err.message);
            } finally {
                setLoading(false);
            }
        };

        fetchProducts();
    }, []);

    return (
        <div className="font-sans grid grid-rows-[20px_1fr_20px] items-center justify-items-center min-h-screen p-8 pb-20 gap-16 sm:p-20">
            <main className="flex flex-col gap-8 row-start-2 items-center sm:items-start w-full max-w-3xl">
                <h1 className="text-2xl font-bold">Products</h1>

                {loading && (
                    <p className="text-gray-500">Loading products...</p>
                )}
                {error && <p className="text-red-500">Error: {error}</p>}

                {!loading && !error && (
                    <ul className="w-full divide-y divide-gray-200">
                        {products.map((p) => (
                            <li key={p.id} className="py-3">
                                <div className="font-semibold">{p.name}</div>
                                <div className="text-sm text-gray-500">
                                    {p.description}
                                </div>
                                <div className="text-sm font-mono">
                                    ${p.price.toFixed(2)}
                                </div>
                            </li>
                        ))}
                    </ul>
                )}
            </main>
        </div>
    );
}
