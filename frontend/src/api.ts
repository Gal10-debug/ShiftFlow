import type { Role } from "./he";
export interface User {
  id: string;
  firstName: string;
  lastName: string;
  username: string;
  isActive: boolean;
  mustChangePassword: boolean;
  createdAt: string;
  roles: Role[];
}
export interface UserPage {
  items: User[];
  totalCount: number;
  page: number;
  pageSize: number;
}
let token: string | undefined;
export function resetCsrf() {
  token = undefined;
}
export async function api<T>(
  path: string,
  method = "GET",
  body?: unknown,
): Promise<T> {
  try {
    const headers: Record<string, string> = {};
    if (method !== "GET") {
      if (!token) {
        const response = await fetch("/api/auth/csrf", {
          credentials: "same-origin",
          cache: "no-store",
        });
        if (!response.ok) throw new Error("CSRF_INVALID");
        token = ((await response.json()) as { requestToken: string })
          .requestToken;
      }
      headers["X-CSRF-TOKEN"] = token!;
      if (body !== undefined) headers["Content-Type"] = "application/json";
    }
    const response = await fetch(`/api${path}`, {
      method,
      credentials: "same-origin",
      cache: "no-store",
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (!response.ok) {
      const error = (await response
        .json()
        .catch(() => ({ code: "INTERNAL_ERROR" }))) as { code: string };
      if (response.status === 401 && path !== "/auth/login") {
        resetCsrf();
        window.dispatchEvent(new Event("session-expired"));
      }
      if (error.code === "CSRF_INVALID") resetCsrf();
      throw new Error(error.code || "INTERNAL_ERROR");
    }
    return response.status === 204
      ? (undefined as T)
      : ((await response.json()) as T);
  } catch (error) {
    if (error instanceof TypeError) throw new Error("NETWORK_ERROR");
    throw error;
  }
}
