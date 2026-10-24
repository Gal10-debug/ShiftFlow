import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import App from "./App";
import { resetCsrf, type User } from "./api";
import { he } from "./he";
import indexHtml from "../index.html?raw";

const employee: User = {
  id: "employee-id",
  firstName: "דנה",
  lastName: "כהן",
  username: "dana",
  roles: ["EMPLOYEE"],
  isActive: true,
  mustChangePassword: false,
  createdAt: "2026-10-24T08:00:00Z",
};
const admin: User = {
  ...employee,
  id: "admin-id",
  username: "manager",
  roles: ["ADMIN"],
};
let current: User | null;
let loginFailure: boolean;
let writes: {
  path: string;
  body: Record<string, unknown>;
  headers: Record<string, string>;
}[];
function json(value: unknown, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}
beforeEach(() => {
  resetCsrf();
  current = null;
  loginFailure = false;
  writes = [];
  vi.stubGlobal(
    "fetch",
    vi.fn(async (url: string, options: RequestInit = {}) => {
      const path = String(url);
      const method = options.method ?? "GET";
      if (path === "/api/auth/csrf") return json({ requestToken: "test-csrf" });
      if (method !== "GET")
        writes.push({
          path,
          body: options.body ? JSON.parse(options.body as string) : {},
          headers: options.headers as Record<string, string>,
        });
      if (path === "/api/auth/me")
        return current ? json(current) : json({ code: "UNAUTHENTICATED" }, 401);
      if (path === "/api/auth/login")
        return loginFailure
          ? json(
              {
                code: "INVALID_CREDENTIALS",
                message: "Do not show this English message",
              },
              401,
            )
          : json((current = { ...employee }));
      if (path === "/api/auth/change-password")
        return json((current = { ...current!, mustChangePassword: false }));
      if (path === "/api/auth/logout") {
        current = null;
        return new Response(null, { status: 204 });
      }
      if (path.startsWith("/api/access/"))
        return new Response(null, { status: 204 });
      if (path.startsWith("/api/users") && method === "GET")
        return json({
          items: [employee],
          page: 1,
          pageSize: 20,
          totalCount: 1,
        });
      return json(
        employee,
        method === "POST" && path === "/api/users" ? 201 : 200,
      );
    }),
  );
});
function app(path = "/login") {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <App />
    </MemoryRouter>,
  );
}

it("defines Hebrew language and RTL on the document root", () => {
  expect(indexHtml).toContain('lang="he" dir="rtl"');
});
it("renders the Hebrew login form with LTR password and no registration", async () => {
  app();
  expect(await screen.findByRole("button", { name: he.login })).toBeVisible();
  expect(screen.getByLabelText(he.username)).toBeVisible();
  expect(screen.getByLabelText(he.password)).toHaveAttribute("dir", "ltr");
  expect(screen.queryByText("הרשמה")).not.toBeInTheDocument();
});
it("validates missing login fields in Hebrew", async () => {
  const user = userEvent.setup();
  app();
  await user.click(await screen.findByRole("button", { name: he.login }));
  expect(screen.getByRole("alert")).toHaveTextContent(he.required);
  expect(writes).toHaveLength(0);
});
it("maps login errors to Hebrew without showing backend English and sends CSRF", async () => {
  loginFailure = true;
  const user = userEvent.setup();
  app();
  await user.type(await screen.findByLabelText(he.username), "dana");
  await user.type(screen.getByLabelText(he.password), "incorrect");
  await user.click(screen.getByRole("button", { name: he.login }));
  expect(await screen.findByRole("alert")).toHaveTextContent(
    he.errors.INVALID_CREDENTIALS,
  );
  expect(screen.queryByText(/Do not show/)).not.toBeInTheDocument();
  expect(writes[0].headers["X-CSRF-TOKEN"]).toBe("test-csrf");
});
it("redirects anonymous users from protected routes to login", async () => {
  app("/users");
  expect(await screen.findByRole("button", { name: he.login })).toBeVisible();
});
it("rejects employee access to admin pages and hides admin navigation", async () => {
  current = employee;
  app("/users");
  expect(
    await screen.findByRole("heading", { name: he.unauthorized }),
  ).toBeVisible();
  expect(
    screen.queryByRole("link", { name: he.users }),
  ).not.toBeInTheDocument();
});
it("forces first-login password change and completes the flow", async () => {
  current = { ...employee, mustChangePassword: true };
  const user = userEvent.setup();
  app("/profile");
  expect(
    await screen.findByRole("heading", { name: he.firstPassword }),
  ).toBeVisible();
  await user.type(
    screen.getByLabelText(he.currentPassword),
    "TemporaryPass!2026",
  );
  await user.type(screen.getByLabelText(he.newPassword), "PersonalSecret!2026");
  await user.type(
    screen.getByLabelText(he.confirmPassword),
    "PersonalSecret!2026",
  );
  await user.click(screen.getByRole("button", { name: he.changePassword }));
  expect(
    await screen.findByRole("heading", { name: he.employee }),
  ).toBeVisible();
  expect(writes[0].path).toBe("/api/auth/change-password");
});
it("rejects password confirmation mismatch before calling the server", async () => {
  current = { ...employee, mustChangePassword: true };
  const user = userEvent.setup();
  app("/change-password");
  await user.type(
    await screen.findByLabelText(he.currentPassword),
    "TemporaryPass!2026",
  );
  await user.type(screen.getByLabelText(he.newPassword), "PersonalSecret!2026");
  await user.type(
    screen.getByLabelText(he.confirmPassword),
    "DifferentSecret!2026",
  );
  await user.click(screen.getByRole("button", { name: he.changePassword }));
  expect(screen.getByRole("alert")).toHaveTextContent(he.mismatch);
  expect(writes).toHaveLength(0);
});
it("creates an employee account using the Hebrew admin dialog", async () => {
  current = admin;
  const user = userEvent.setup();
  app("/users");
  await user.click(await screen.findByRole("button", { name: he.createUser }));
  const dialog = screen.getByRole("dialog");
  await user.type(within(dialog).getByLabelText(he.firstName), "יעל");
  await user.type(within(dialog).getByLabelText(he.lastName), "לוי");
  await user.type(within(dialog).getByLabelText(he.username), "yael");
  await user.type(
    within(dialog).getByLabelText(he.initialPassword),
    "TemporaryPass!2026",
  );
  await user.click(within(dialog).getByRole("button", { name: he.create }));
  expect(await screen.findByRole("status")).toHaveTextContent(he.createdUser);
  expect(writes[0].body).toMatchObject({
    firstName: "יעל",
    roles: ["EMPLOYEE"],
    isActive: true,
  });
});
it("edits profile details and role assignments without exposing credentials", async () => {
  current = admin;
  const user = userEvent.setup();
  app("/users");
  await user.click(await screen.findByRole("button", { name: he.editUser }));
  let dialog = screen.getByRole("dialog");
  await user.clear(within(dialog).getByLabelText(he.firstName));
  await user.type(within(dialog).getByLabelText(he.firstName), "יעל");
  expect(
    within(dialog).queryByLabelText(he.initialPassword),
  ).not.toBeInTheDocument();
  await user.click(within(dialog).getByRole("button", { name: he.save }));
  await waitFor(() =>
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
  );
  await user.click(screen.getByRole("button", { name: he.editRoles }));
  dialog = screen.getByRole("dialog");
  await user.click(within(dialog).getByLabelText(he.rolesLabel.COORDINATOR));
  await user.click(within(dialog).getByRole("button", { name: he.save }));
  await waitFor(() => expect(writes).toHaveLength(2));
  expect(writes[1].body.roles).toEqual(["EMPLOYEE", "COORDINATOR"]);
});
it("resets passwords and confirms deactivation in Hebrew", async () => {
  current = admin;
  const user = userEvent.setup();
  const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
  app("/users");
  await user.click(
    await screen.findByRole("button", { name: he.resetPassword }),
  );
  const dialog = screen.getByRole("dialog");
  await user.type(
    within(dialog).getByLabelText(he.initialPassword),
    "TemporaryPass!2026",
  );
  await user.click(within(dialog).getByRole("button", { name: he.save }));
  await waitFor(() =>
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument(),
  );
  await user.click(screen.getByRole("button", { name: he.deactivate }));
  await waitFor(() => expect(writes).toHaveLength(2));
  expect(confirm).toHaveBeenCalledWith(he.confirmDeactivate);
  expect(writes[0].path).toContain("reset-password");
  expect(writes[1].body).toEqual({ isActive: false });
});
it("handles session expiry by returning to login", async () => {
  current = employee;
  app("/profile");
  await screen.findByRole("heading", { name: he.profile });
  window.dispatchEvent(new Event("session-expired"));
  expect(await screen.findByRole("button", { name: he.login })).toBeVisible();
});
it("shows a Hebrew not-found page", async () => {
  app("/missing-page");
  expect(
    await screen.findByRole("heading", { name: he.notFound }),
  ).toBeVisible();
});
