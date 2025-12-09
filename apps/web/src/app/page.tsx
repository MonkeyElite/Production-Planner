"use client";

import {
    ChangeEvent,
    FormEvent,
    useCallback,
    useEffect,
    useMemo,
    useState,
} from "react";
import { signIn, signOut, useSession } from "next-auth/react";

interface Product {
    id: string;
    name: string;
    description: string;
    price: number;
    isActive: boolean;
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

interface ProductionLine {
    id: string;
    name: string;
    description: string;
    capacityPerShift: number;
    shiftSchedule: string;
    isActive: boolean;
    productIds: string[];
}

interface ProductionLineResponse {
    id?: string;
    Id?: string;
    name?: string;
    Name?: string;
    description?: string;
    Description?: string;
    capacityPerShift?: number;
    CapacityPerShift?: number;
    shiftSchedule?: string;
    ShiftSchedule?: string;
    isActive?: boolean;
    IsActive?: boolean;
    productIds?: string[];
    ProductIds?: string[];
}

type ProductFormState = {
    name: string;
    description: string;
    price: string;
    isActive?: boolean;
};

type ProductionLineFormState = {
    name: string;
    description: string;
    capacityPerShift: string;
    shiftSchedule: string;
    productIds: string[];
    isActive?: boolean;
};

const gatewayUrl = process.env.NEXT_PUBLIC_GATEWAY_URL ?? "";

const defaultProductForm: ProductFormState = {
    name: "",
    description: "",
    price: "",
};

const defaultLineForm: ProductionLineFormState = {
    name: "",
    description: "",
    capacityPerShift: "1",
    shiftSchedule: "Day shift",
    productIds: [],
};

export default function Home() {
    const { data: session, status } = useSession();
    const [products, setProducts] = useState<Product[]>([]);
    const [productionLines, setProductionLines] = useState<ProductionLine[]>(
        []
    );
    const [loading, setLoading] = useState(false);
    const [mutationPending, setMutationPending] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [productForm, setProductForm] =
        useState<ProductFormState>(defaultProductForm);
    const [lineForm, setLineForm] =
        useState<ProductionLineFormState>(defaultLineForm);
    const [editingProductId, setEditingProductId] = useState<string | null>(
        null
    );
    const [editingProductForm, setEditingProductForm] =
        useState<ProductFormState | null>(null);
    const [editingLineId, setEditingLineId] = useState<string | null>(null);
    const [editingLineForm, setEditingLineForm] =
        useState<ProductionLineFormState | null>(null);

    const isAuthenticated = status === "authenticated";
    const sessionLoading = status === "loading";

    const authedFetch = useCallback(
        async (path: string, init?: RequestInit) => {
            if (!gatewayUrl) {
                throw new Error("NEXT_PUBLIC_GATEWAY_URL is not configured.");
            }

            if (!session?.accessToken) {
                throw new Error("Missing access token. Please sign in again.");
            }

            const headers = new Headers(init?.headers ?? {});
            headers.set("Authorization", `Bearer ${session.accessToken}`);
            if (init?.body && !headers.has("Content-Type")) {
                headers.set("Content-Type", "application/json");
            }

            const normalizedBase = gatewayUrl.endsWith("/")
                ? gatewayUrl.slice(0, -1)
                : gatewayUrl;
            const normalizedPath = path.startsWith("/") ? path : `/${path}`;

            const response = await fetch(`${normalizedBase}${normalizedPath}`, {
                ...init,
                headers,
                cache: init?.cache ?? "no-store",
            });

            if (!response.ok) {
                const detail = await response.text();
                const message = `Request failed (${response.status}). ${detail}`;
                throw new Error(message.trim() || "Request failed.");
            }
            return response;
        },
        [session?.accessToken]
    );

    const loadProducts = useCallback(async () => {
        const response = await authedFetch("/api/products");
        const payload: unknown = await response.json();
        if (!Array.isArray(payload)) {
            throw new Error("Unexpected products payload.");
        }

        const normalized: Product[] = (payload as ProductResponse[]).map(
            (item) => ({
                id: item.id ?? item.Id ?? crypto.randomUUID(),
                name: item.name ?? item.Name ?? "Unnamed product",
                description: item.description ?? item.Description ?? "",
                price: Number(item.price ?? item.Price ?? 0),
                isActive: item.isActive ?? item.IsActive ?? true,
            })
        );

        setProducts(normalized);
    }, [authedFetch]);

    const loadProductionLines = useCallback(async () => {
        const response = await authedFetch("/api/productionlines");
        const payload: unknown = await response.json();
        if (!Array.isArray(payload)) {
            throw new Error("Unexpected production line payload.");
        }

        const normalized: ProductionLine[] = (
            payload as ProductionLineResponse[]
        ).map((item) => ({
            id: item.id ?? item.Id ?? crypto.randomUUID(),
            name: item.name ?? item.Name ?? "Unnamed line",
            description: item.description ?? item.Description ?? "",
            capacityPerShift: Number(
                item.capacityPerShift ?? item.CapacityPerShift ?? 0
            ),
            shiftSchedule: item.shiftSchedule ?? item.ShiftSchedule ?? "",
            isActive: item.isActive ?? item.IsActive ?? true,
            productIds: (item.productIds ?? item.ProductIds ?? []).filter(
                Boolean
            ) as string[],
        }));

        setProductionLines(normalized);
    }, [authedFetch]);

    const refreshInventory = useCallback(async () => {
        if (!isAuthenticated) {
            return;
        }

        setLoading(true);
        setError(null);

        try {
            await Promise.all([loadProducts(), loadProductionLines()]);
        } catch (err: unknown) {
            const message =
                err instanceof Error ? err.message : "Failed to load data.";
            setProducts([]);
            setProductionLines([]);
            setError(message);
        } finally {
            setLoading(false);
        }
    }, [isAuthenticated, loadProducts, loadProductionLines]);

    useEffect(() => {
        if (isAuthenticated) {
            void refreshInventory();
        }

        if (status === "unauthenticated") {
            setProducts([]);
            setProductionLines([]);
            setLoading(false);
        }
    }, [isAuthenticated, status, refreshInventory]);

    useEffect(() => {
        if (session?.error) {
            setError(session.error);
        }
    }, [session?.error]);

    useEffect(() => {
        setLineForm((prev) => ({
            ...prev,
            productIds: prev.productIds.filter((id) =>
                products.some((product) => product.id === id)
            ),
        }));
        setEditingLineForm((prev) =>
            prev
                ? {
                      ...prev,
                      productIds: prev.productIds.filter((id) =>
                          products.some((product) => product.id === id)
                      ),
                  }
                : null
        );
    }, [products]);

    const productNameById = useMemo(() => {
        return new Map(
            products.map((product) => [product.id, product.name] as const)
        );
    }, [products]);

    const handleProductFormChange = (
        event: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>
    ) => {
        const { name, value } = event.target;
        setProductForm((prev) => ({ ...prev, [name]: value }));
    };

    const handleLineFormChange = (
        event: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>
    ) => {
        const { name, value } = event.target;
        setLineForm((prev) => ({ ...prev, [name]: value }));
    };

    const toggleLineProductSelection = (productId: string) => {
        setLineForm((prev) => ({
            ...prev,
            productIds: prev.productIds.includes(productId)
                ? prev.productIds.filter((id) => id !== productId)
                : [...prev.productIds, productId],
        }));
    };

    const toggleEditingLineProductSelection = (productId: string) => {
        setEditingLineForm((prev) => {
            if (!prev) {
                return prev;
            }

            const exists = prev.productIds.includes(productId);
            return {
                ...prev,
                productIds: exists
                    ? prev.productIds.filter((id) => id !== productId)
                    : [...prev.productIds, productId],
            };
        });
    };

    const handleCreateProduct = async (event: FormEvent<HTMLFormElement>) => {
        event.preventDefault();
        setMutationPending(true);
        setError(null);

        try {
            await authedFetch("/api/products", {
                method: "POST",
                body: JSON.stringify({
                    name: productForm.name,
                    description: productForm.description,
                    price: Number(productForm.price || 0),
                }),
            });

            setProductForm(defaultProductForm);
            await refreshInventory();
        } catch (err: unknown) {
            const message =
                err instanceof Error
                    ? err.message
                    : "Failed to create product.";
            setError(message);
        } finally {
            setMutationPending(false);
        }
    };

    const beginEditProduct = (product: Product) => {
        setEditingProductId(product.id);
        setEditingProductForm({
            name: product.name,
            description: product.description,
            price: product.price.toString(),
            isActive: product.isActive,
        });
    };

    const cancelEditProduct = () => {
        setEditingProductId(null);
        setEditingProductForm(null);
    };

    const handleSaveProduct = async (event: FormEvent<HTMLFormElement>) => {
        event.preventDefault();
        if (!editingProductId || !editingProductForm) {
            return;
        }

        setMutationPending(true);
        setError(null);

        try {
            await authedFetch(`/api/products/${editingProductId}`, {
                method: "PUT",
                body: JSON.stringify({
                    name: editingProductForm.name,
                    description: editingProductForm.description,
                    price: Number(editingProductForm.price || 0),
                    isActive: editingProductForm.isActive ?? true,
                }),
            });

            cancelEditProduct();
            await refreshInventory();
        } catch (err: unknown) {
            const message =
                err instanceof Error
                    ? err.message
                    : "Failed to update product.";
            setError(message);
        } finally {
            setMutationPending(false);
        }
    };

    const handleDeleteProduct = async (id: string) => {
        if (!window.confirm("Delete this product?")) {
            return;
        }

        setMutationPending(true);
        setError(null);

        try {
            await authedFetch(`/api/products/${id}`, { method: "DELETE" });
            if (editingProductId === id) {
                cancelEditProduct();
            }
            await refreshInventory();
        } catch (err: unknown) {
            const message =
                err instanceof Error
                    ? err.message
                    : "Failed to delete product.";
            setError(message);
        } finally {
            setMutationPending(false);
        }
    };

    const handleCreateLine = async (event: FormEvent<HTMLFormElement>) => {
        event.preventDefault();
        setMutationPending(true);
        setError(null);

        try {
            await authedFetch("/api/productionlines", {
                method: "POST",
                body: JSON.stringify({
                    name: lineForm.name,
                    description: lineForm.description,
                    capacityPerShift: Number(lineForm.capacityPerShift || 0),
                    shiftSchedule: lineForm.shiftSchedule,
                    productIds: lineForm.productIds,
                }),
            });

            setLineForm(defaultLineForm);
            await refreshInventory();
        } catch (err: unknown) {
            const message =
                err instanceof Error
                    ? err.message
                    : "Failed to create production line.";
            setError(message);
        } finally {
            setMutationPending(false);
        }
    };

    const beginEditLine = (line: ProductionLine) => {
        setEditingLineId(line.id);
        setEditingLineForm({
            name: line.name,
            description: line.description,
            capacityPerShift: line.capacityPerShift.toString(),
            shiftSchedule: line.shiftSchedule,
            productIds: [...line.productIds],
            isActive: line.isActive,
        });
    };

    const cancelEditLine = () => {
        setEditingLineId(null);
        setEditingLineForm(null);
    };

    const handleSaveLine = async (event: FormEvent<HTMLFormElement>) => {
        event.preventDefault();
        if (!editingLineId || !editingLineForm) {
            return;
        }

        setMutationPending(true);
        setError(null);

        try {
            await authedFetch(`/api/productionlines/${editingLineId}`, {
                method: "PUT",
                body: JSON.stringify({
                    name: editingLineForm.name,
                    description: editingLineForm.description,
                    capacityPerShift: Number(
                        editingLineForm.capacityPerShift || 0
                    ),
                    shiftSchedule: editingLineForm.shiftSchedule,
                    isActive: editingLineForm.isActive ?? true,
                    productIds: editingLineForm.productIds,
                }),
            });

            cancelEditLine();
            await refreshInventory();
        } catch (err: unknown) {
            const message =
                err instanceof Error
                    ? err.message
                    : "Failed to update production line.";
            setError(message);
        } finally {
            setMutationPending(false);
        }
    };

    const handleDeleteLine = async (id: string) => {
        if (!window.confirm("Delete this production line?")) {
            return;
        }

        setMutationPending(true);
        setError(null);

        try {
            await authedFetch(`/api/productionlines/${id}`, {
                method: "DELETE",
            });
            if (editingLineId === id) {
                cancelEditLine();
            }
            await refreshInventory();
        } catch (err: unknown) {
            const message =
                err instanceof Error
                    ? err.message
                    : "Failed to delete production line.";
            setError(message);
        } finally {
            setMutationPending(false);
        }
    };

    const displayName = session?.user?.name ?? session?.user?.email ?? "";
    const hasProducts = products.length > 0;
    const hasLines = productionLines.length > 0;

    return (
        <div className="font-sans grid grid-rows-[20px_1fr_20px] items-center justify-items-center min-h-screen p-8 pb-20 gap-16 sm:p-20">
            <main className="flex flex-col gap-8 row-start-2 items-center sm:items-start w-full max-w-4xl">
                <div className="flex w-full flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                    <div>
                        <h1 className="text-2xl font-bold">
                            Production Planner
                        </h1>
                        <p className="text-sm text-gray-600">
                            {sessionLoading && "Checking your session..."}
                            {isAuthenticated && !sessionLoading && (
                                <span>
                                    Signed in
                                    {displayName && ` as ${displayName}`}.
                                </span>
                            )}
                            {!isAuthenticated &&
                                !sessionLoading &&
                                "Sign in to view inventory."}
                        </p>
                        <p className="text-xs text-gray-500 mt-1">
                            Your workspace is private to your tenant. Sharing
                            production data requires explicit invitations
                            (coming soon), so never assume other users can see
                            your lines.
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
                            onClick={() => void refreshInventory()}
                            disabled={!isAuthenticated || loading}
                        >
                            Refresh
                        </button>
                    </div>
                </div>

                {(loading || sessionLoading) && (
                    <p className="text-gray-500">
                        Loading your private data...
                    </p>
                )}

                {!loading && error && (
                    <p className="text-red-600" role="alert">
                        {error}
                    </p>
                )}

                {isAuthenticated && (
                    <section className="w-full space-y-6">
                        <div className="rounded border border-gray-200 p-4 shadow-sm">
                            <div className="flex items-center justify-between mb-4">
                                <h2 className="text-xl font-semibold">
                                    Products
                                </h2>
                                <span className="text-xs text-gray-500">
                                    Only you can see and edit your products.
                                </span>
                            </div>
                            <form
                                className="grid gap-2 sm:grid-cols-2"
                                onSubmit={handleCreateProduct}
                            >
                                <input
                                    name="name"
                                    value={productForm.name}
                                    onChange={handleProductFormChange}
                                    placeholder="Product name"
                                    className="rounded border px-2 py-1"
                                    required
                                    disabled={mutationPending}
                                />
                                <input
                                    name="price"
                                    type="number"
                                    min="0"
                                    step="0.01"
                                    value={productForm.price}
                                    onChange={handleProductFormChange}
                                    placeholder="Price"
                                    className="rounded border px-2 py-1"
                                    required
                                    disabled={mutationPending}
                                />
                                <textarea
                                    name="description"
                                    value={productForm.description}
                                    onChange={handleProductFormChange}
                                    placeholder="Description"
                                    className="rounded border px-2 py-1 sm:col-span-2"
                                    rows={2}
                                    disabled={mutationPending}
                                />
                                <button
                                    type="submit"
                                    className="rounded bg-green-600 px-3 py-1 text-sm font-semibold text-white disabled:bg-green-300 sm:col-span-2"
                                    disabled={mutationPending}
                                >
                                    Create product
                                </button>
                            </form>
                            {!loading && !error && !hasProducts && (
                                <p className="text-gray-500 mt-4">
                                    No products yet.
                                </p>
                            )}
                            {hasProducts && (
                                <ul className="mt-4 divide-y divide-gray-200">
                                    {products.map((product) => (
                                        <li key={product.id} className="py-4">
                                            {editingProductId === product.id &&
                                            editingProductForm ? (
                                                <form
                                                    className="space-y-2"
                                                    onSubmit={handleSaveProduct}
                                                >
                                                    <input
                                                        name="name"
                                                        value={
                                                            editingProductForm.name
                                                        }
                                                        onChange={(event) =>
                                                            setEditingProductForm(
                                                                (prev) =>
                                                                    prev
                                                                        ? {
                                                                              ...prev,
                                                                              name: event
                                                                                  .target
                                                                                  .value,
                                                                          }
                                                                        : prev
                                                            )
                                                        }
                                                        className="w-full rounded border px-2 py-1"
                                                        required
                                                        disabled={
                                                            mutationPending
                                                        }
                                                    />
                                                    <textarea
                                                        name="description"
                                                        value={
                                                            editingProductForm.description
                                                        }
                                                        onChange={(event) =>
                                                            setEditingProductForm(
                                                                (prev) =>
                                                                    prev
                                                                        ? {
                                                                              ...prev,
                                                                              description:
                                                                                  event
                                                                                      .target
                                                                                      .value,
                                                                          }
                                                                        : prev
                                                            )
                                                        }
                                                        className="w-full rounded border px-2 py-1"
                                                        rows={2}
                                                        disabled={
                                                            mutationPending
                                                        }
                                                    />
                                                    <div className="flex flex-col gap-2 sm:flex-row">
                                                        <input
                                                            name="price"
                                                            type="number"
                                                            min="0"
                                                            step="0.01"
                                                            value={
                                                                editingProductForm.price
                                                            }
                                                            onChange={(event) =>
                                                                setEditingProductForm(
                                                                    (prev) =>
                                                                        prev
                                                                            ? {
                                                                                  ...prev,
                                                                                  price: event
                                                                                      .target
                                                                                      .value,
                                                                              }
                                                                            : prev
                                                                )
                                                            }
                                                            className="rounded border px-2 py-1 flex-1"
                                                            required
                                                            disabled={
                                                                mutationPending
                                                            }
                                                        />
                                                        <label className="flex items-center gap-2 text-sm">
                                                            <input
                                                                type="checkbox"
                                                                checked={
                                                                    editingProductForm.isActive ??
                                                                    true
                                                                }
                                                                onChange={(
                                                                    event
                                                                ) =>
                                                                    setEditingProductForm(
                                                                        (
                                                                            prev
                                                                        ) =>
                                                                            prev
                                                                                ? {
                                                                                      ...prev,
                                                                                      isActive:
                                                                                          event
                                                                                              .target
                                                                                              .checked,
                                                                                  }
                                                                                : prev
                                                                    )
                                                                }
                                                                disabled={
                                                                    mutationPending
                                                                }
                                                            />
                                                            Active
                                                        </label>
                                                    </div>
                                                    <div className="flex gap-2">
                                                        <button
                                                            type="submit"
                                                            className="rounded bg-blue-600 px-3 py-1 text-sm font-semibold text-white disabled:bg-blue-300"
                                                            disabled={
                                                                mutationPending
                                                            }
                                                        >
                                                            Save
                                                        </button>
                                                        <button
                                                            type="button"
                                                            className="rounded border px-3 py-1 text-sm"
                                                            onClick={
                                                                cancelEditProduct
                                                            }
                                                            disabled={
                                                                mutationPending
                                                            }
                                                        >
                                                            Cancel
                                                        </button>
                                                    </div>
                                                </form>
                                            ) : (
                                                <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
                                                    <div>
                                                        <div className="font-semibold">
                                                            {product.name}
                                                        </div>
                                                        <div className="text-sm text-gray-500">
                                                            {product.description ||
                                                                "No description"}
                                                        </div>
                                                        {!product.isActive && (
                                                            <span className="text-xs uppercase text-orange-600">
                                                                Inactive
                                                            </span>
                                                        )}
                                                    </div>
                                                    <div className="flex items-center gap-2">
                                                        <div className="text-sm font-mono">
                                                            $
                                                            {product.price.toFixed(
                                                                2
                                                            )}
                                                        </div>
                                                        <button
                                                            className="rounded border px-2 py-1 text-xs"
                                                            onClick={() =>
                                                                beginEditProduct(
                                                                    product
                                                                )
                                                            }
                                                            disabled={
                                                                mutationPending
                                                            }
                                                        >
                                                            Edit
                                                        </button>
                                                        <button
                                                            className="rounded border border-red-300 px-2 py-1 text-xs text-red-700"
                                                            onClick={() =>
                                                                handleDeleteProduct(
                                                                    product.id
                                                                )
                                                            }
                                                            disabled={
                                                                mutationPending
                                                            }
                                                        >
                                                            Delete
                                                        </button>
                                                    </div>
                                                </div>
                                            )}
                                        </li>
                                    ))}
                                </ul>
                            )}
                        </div>

                        <div className="rounded border border-gray-200 p-4 shadow-sm">
                            <div className="flex items-center justify-between mb-4">
                                <h2 className="text-xl font-semibold">
                                    Production lines
                                </h2>
                                <span className="text-xs text-gray-500">
                                    Lines can only reference products that you
                                    own.
                                </span>
                            </div>
                            <form
                                className="grid gap-2"
                                onSubmit={handleCreateLine}
                            >
                                <div className="grid gap-2 sm:grid-cols-2">
                                    <input
                                        name="name"
                                        value={lineForm.name}
                                        onChange={handleLineFormChange}
                                        placeholder="Line name"
                                        className="rounded border px-2 py-1"
                                        required
                                        disabled={mutationPending}
                                    />
                                    <input
                                        name="capacityPerShift"
                                        type="number"
                                        min="1"
                                        step="1"
                                        value={lineForm.capacityPerShift}
                                        onChange={handleLineFormChange}
                                        placeholder="Capacity per shift"
                                        className="rounded border px-2 py-1"
                                        required
                                        disabled={mutationPending}
                                    />
                                </div>
                                <input
                                    name="shiftSchedule"
                                    value={lineForm.shiftSchedule}
                                    onChange={handleLineFormChange}
                                    placeholder="Shift schedule"
                                    className="rounded border px-2 py-1"
                                    required
                                    disabled={mutationPending}
                                />
                                <textarea
                                    name="description"
                                    value={lineForm.description}
                                    onChange={handleLineFormChange}
                                    placeholder="Description"
                                    className="rounded border px-2 py-1"
                                    rows={2}
                                    disabled={mutationPending}
                                />
                                <div>
                                    <p className="text-xs font-semibold text-gray-600 mb-1">
                                        Products running on this line
                                    </p>
                                    {products.length === 0 && (
                                        <p className="text-xs text-gray-500">
                                            Create products before assigning
                                            them to a line.
                                        </p>
                                    )}
                                    <div className="flex flex-wrap gap-3">
                                        {products.map((product) => (
                                            <label
                                                key={product.id}
                                                className="flex items-center gap-1 text-sm"
                                            >
                                                <input
                                                    type="checkbox"
                                                    checked={lineForm.productIds.includes(
                                                        product.id
                                                    )}
                                                    onChange={() =>
                                                        toggleLineProductSelection(
                                                            product.id
                                                        )
                                                    }
                                                    disabled={mutationPending}
                                                />
                                                {product.name}
                                            </label>
                                        ))}
                                    </div>
                                </div>
                                <button
                                    type="submit"
                                    className="rounded bg-green-600 px-3 py-1 text-sm font-semibold text-white disabled:bg-green-300"
                                    disabled={mutationPending}
                                >
                                    Create production line
                                </button>
                            </form>
                            {!loading && !error && !hasLines && (
                                <p className="text-gray-500 mt-4">
                                    No production lines yet.
                                </p>
                            )}
                            {hasLines && (
                                <ul className="mt-4 divide-y divide-gray-200">
                                    {productionLines.map((line) => (
                                        <li key={line.id} className="py-4">
                                            {editingLineId === line.id &&
                                            editingLineForm ? (
                                                <form
                                                    className="space-y-2"
                                                    onSubmit={handleSaveLine}
                                                >
                                                    <input
                                                        name="name"
                                                        value={
                                                            editingLineForm.name
                                                        }
                                                        onChange={(event) =>
                                                            setEditingLineForm(
                                                                (prev) =>
                                                                    prev
                                                                        ? {
                                                                              ...prev,
                                                                              name: event
                                                                                  .target
                                                                                  .value,
                                                                          }
                                                                        : prev
                                                            )
                                                        }
                                                        className="w-full rounded border px-2 py-1"
                                                        required
                                                        disabled={
                                                            mutationPending
                                                        }
                                                    />
                                                    <textarea
                                                        name="description"
                                                        value={
                                                            editingLineForm.description
                                                        }
                                                        onChange={(event) =>
                                                            setEditingLineForm(
                                                                (prev) =>
                                                                    prev
                                                                        ? {
                                                                              ...prev,
                                                                              description:
                                                                                  event
                                                                                      .target
                                                                                      .value,
                                                                          }
                                                                        : prev
                                                            )
                                                        }
                                                        className="w-full rounded border px-2 py-1"
                                                        rows={2}
                                                        disabled={
                                                            mutationPending
                                                        }
                                                    />
                                                    <div className="grid gap-2 sm:grid-cols-2">
                                                        <input
                                                            name="capacityPerShift"
                                                            type="number"
                                                            min="1"
                                                            step="1"
                                                            value={
                                                                editingLineForm.capacityPerShift
                                                            }
                                                            onChange={(event) =>
                                                                setEditingLineForm(
                                                                    (prev) =>
                                                                        prev
                                                                            ? {
                                                                                  ...prev,
                                                                                  capacityPerShift:
                                                                                      event
                                                                                          .target
                                                                                          .value,
                                                                              }
                                                                            : prev
                                                                )
                                                            }
                                                            className="rounded border px-2 py-1"
                                                            required
                                                            disabled={
                                                                mutationPending
                                                            }
                                                        />
                                                        <input
                                                            name="shiftSchedule"
                                                            value={
                                                                editingLineForm.shiftSchedule
                                                            }
                                                            onChange={(event) =>
                                                                setEditingLineForm(
                                                                    (prev) =>
                                                                        prev
                                                                            ? {
                                                                                  ...prev,
                                                                                  shiftSchedule:
                                                                                      event
                                                                                          .target
                                                                                          .value,
                                                                              }
                                                                            : prev
                                                                )
                                                            }
                                                            className="rounded border px-2 py-1"
                                                            required
                                                            disabled={
                                                                mutationPending
                                                            }
                                                        />
                                                    </div>
                                                    <label className="flex items-center gap-2 text-sm">
                                                        <input
                                                            type="checkbox"
                                                            checked={
                                                                editingLineForm.isActive ??
                                                                true
                                                            }
                                                            onChange={(event) =>
                                                                setEditingLineForm(
                                                                    (prev) =>
                                                                        prev
                                                                            ? {
                                                                                  ...prev,
                                                                                  isActive:
                                                                                      event
                                                                                          .target
                                                                                          .checked,
                                                                              }
                                                                            : prev
                                                                )
                                                            }
                                                            disabled={
                                                                mutationPending
                                                            }
                                                        />
                                                        Active
                                                    </label>
                                                    <div>
                                                        <p className="text-xs font-semibold text-gray-600 mb-1">
                                                            Products running on
                                                            this line
                                                        </p>
                                                        <div className="flex flex-wrap gap-3">
                                                            {products.map(
                                                                (product) => (
                                                                    <label
                                                                        key={
                                                                            product.id
                                                                        }
                                                                        className="flex items-center gap-1 text-sm"
                                                                    >
                                                                        <input
                                                                            type="checkbox"
                                                                            checked={editingLineForm.productIds.includes(
                                                                                product.id
                                                                            )}
                                                                            onChange={() =>
                                                                                toggleEditingLineProductSelection(
                                                                                    product.id
                                                                                )
                                                                            }
                                                                            disabled={
                                                                                mutationPending
                                                                            }
                                                                        />
                                                                        {
                                                                            product.name
                                                                        }
                                                                    </label>
                                                                )
                                                            )}
                                                        </div>
                                                    </div>
                                                    <div className="flex gap-2">
                                                        <button
                                                            type="submit"
                                                            className="rounded bg-blue-600 px-3 py-1 text-sm font-semibold text-white disabled:bg-blue-300"
                                                            disabled={
                                                                mutationPending
                                                            }
                                                        >
                                                            Save
                                                        </button>
                                                        <button
                                                            type="button"
                                                            className="rounded border px-3 py-1 text-sm"
                                                            onClick={
                                                                cancelEditLine
                                                            }
                                                            disabled={
                                                                mutationPending
                                                            }
                                                        >
                                                            Cancel
                                                        </button>
                                                    </div>
                                                </form>
                                            ) : (
                                                <div className="flex flex-col gap-2">
                                                    <div className="flex flex-col gap-1 sm:flex-row sm:items-center sm:justify-between">
                                                        <div>
                                                            <div className="font-semibold">
                                                                {line.name}
                                                            </div>
                                                            <div className="text-sm text-gray-500">
                                                                {line.description ||
                                                                    "No description"}
                                                            </div>
                                                            {!line.isActive && (
                                                                <span className="text-xs uppercase text-orange-600">
                                                                    Inactive
                                                                </span>
                                                            )}
                                                        </div>
                                                        <div className="text-sm">
                                                            Capacity per shift:{" "}
                                                            <span className="font-mono">
                                                                {
                                                                    line.capacityPerShift
                                                                }
                                                            </span>
                                                        </div>
                                                    </div>
                                                    <div className="text-xs text-gray-600">
                                                        Shift schedule:{" "}
                                                        {line.shiftSchedule ||
                                                            "Not defined"}
                                                    </div>
                                                    <div className="text-xs text-gray-600">
                                                        Products:{" "}
                                                        {line.productIds
                                                            .length === 0
                                                            ? "None"
                                                            : line.productIds
                                                                  .map(
                                                                      (id) =>
                                                                          productNameById.get(
                                                                              id
                                                                          ) ??
                                                                          id
                                                                  )
                                                                  .join(", ")}
                                                    </div>
                                                    <div className="flex gap-2">
                                                        <button
                                                            className="rounded border px-2 py-1 text-xs"
                                                            onClick={() =>
                                                                beginEditLine(
                                                                    line
                                                                )
                                                            }
                                                            disabled={
                                                                mutationPending
                                                            }
                                                        >
                                                            Edit
                                                        </button>
                                                        <button
                                                            className="rounded border border-red-300 px-2 py-1 text-xs text-red-700"
                                                            onClick={() =>
                                                                handleDeleteLine(
                                                                    line.id
                                                                )
                                                            }
                                                            disabled={
                                                                mutationPending
                                                            }
                                                        >
                                                            Delete
                                                        </button>
                                                    </div>
                                                </div>
                                            )}
                                        </li>
                                    ))}
                                </ul>
                            )}
                        </div>
                    </section>
                )}
            </main>
        </div>
    );
}
