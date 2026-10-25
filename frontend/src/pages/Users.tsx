import { useEffect, useState, type FormEvent } from "react";
import { api, type User, type UserPage } from "../api";
import { he, message, type Role } from "../he";
import { Field, Notice, Roles, RolePicker } from "../components/forms";
interface EditorState {
  mode: "create" | "edit" | "roles" | "reset";
  user?: User;
}
function UserEditor({
  editor,
  close,
  saved,
}: {
  editor: EditorState;
  close: () => void;
  saved: (text: string) => void;
}) {
  const [firstName, setFirst] = useState(editor.user?.firstName ?? "");
  const [lastName, setLast] = useState(editor.user?.lastName ?? "");
  const [username, setUsername] = useState(editor.user?.username ?? "");
  const [password, setPassword] = useState("");
  const [roles, setRoles] = useState<Role[]>(
    editor.user?.roles ?? ["EMPLOYEE"],
  );
  const [active, setActive] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const titles = {
    create: he.createUser,
    edit: he.editUser,
    roles: he.editRoles,
    reset: he.resetPassword,
  };
  useEffect(() => {
    const old = document.activeElement as HTMLElement | null;
    const dialog = document.querySelector<HTMLDialogElement>("dialog");
    dialog?.showModal();
    return () => {
      dialog?.close();
      old?.focus();
    };
  }, []);
  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    // Read displayed values at submission time, including password-manager autofill.
    const form = new FormData(e.currentTarget);
    const submittedFirst = String(form.get("firstName") ?? "");
    const submittedLast = String(form.get("lastName") ?? "");
    const submittedUsername = String(form.get("username") ?? "");
    const submittedPassword = String(form.get("temporaryPassword") ?? "");
    setError("");
    if (
      ["create", "edit"].includes(editor.mode) &&
      (!submittedFirst.trim() ||
        !submittedLast.trim() ||
        !submittedUsername.trim())
    ) {
      setError(he.required);
      return;
    }
    if (["create", "roles"].includes(editor.mode) && !roles.length) {
      setError(he.chooseRole);
      return;
    }
    if (["create", "reset"].includes(editor.mode) && !submittedPassword) {
      setError(he.required);
      return;
    }
    if (["create", "edit"].includes(editor.mode)) {
      if (submittedFirst.length > 100 || submittedLast.length > 100) {
        setError(he.errors.INVALID_NAME);
        return;
      }
      if (
        submittedUsername.length > 100 ||
        !/^[\p{L}\p{N}._@+\-]+$/u.test(submittedUsername)
      ) {
        setError(he.errors.INVALID_USERNAME);
        return;
      }
    }
    if (
      ["create", "reset"].includes(editor.mode) &&
      (submittedPassword.length < 12 || submittedPassword.length > 128)
    ) {
      setError(he.errors.INVALID_PASSWORD);
      return;
    }
    setBusy(true);
    try {
      if (editor.mode === "create")
        await api("/users", "POST", {
          firstName: submittedFirst,
          lastName: submittedLast,
          username: submittedUsername,
          initialPassword: submittedPassword,
          roles,
          isActive: active,
        });
      if (editor.mode === "edit")
        await api(`/users/${editor.user!.id}`, "PUT", {
          firstName: submittedFirst,
          lastName: submittedLast,
          username: submittedUsername,
        });
      if (editor.mode === "roles")
        await api(`/users/${editor.user!.id}/roles`, "PUT", { roles });
      if (editor.mode === "reset")
        await api(`/users/${editor.user!.id}/reset-password`, "POST", {
          temporaryPassword: submittedPassword,
        });
      setPassword("");
      saved(
        editor.mode === "create"
          ? he.createdUser
          : editor.mode === "reset"
            ? he.resetDone
            : he.saved,
      );
    } catch (error) {
      setError(message(error));
    } finally {
      setBusy(false);
    }
  }
  return (
    <dialog
      className="editor panel"
      aria-labelledby="editor-title"
      onCancel={(e) => {
        e.preventDefault();
        if (!busy) close();
      }}
    >
      <h2 id="editor-title">{titles[editor.mode]}</h2>
      {editor.user && (
        <p className="muted">
          {editor.user.firstName} {editor.user.lastName}
        </p>
      )}
      <form noValidate onSubmit={submit}>
        {["create", "edit"].includes(editor.mode) && (
          <>
            <div className="form-row">
              <Field
                label={he.firstName}
                name="firstName"
                value={firstName}
                onChange={setFirst}
                autoFocus
              />
              <Field
                label={he.lastName}
                name="lastName"
                value={lastName}
                onChange={setLast}
              />
            </div>
            <Field
              label={he.username}
              name="username"
              value={username}
              onChange={setUsername}
              autoComplete="off"
            />
            <p className="help">{he.usernameHelp}</p>
          </>
        )}
        {["create", "roles"].includes(editor.mode) && (
          <RolePicker value={roles} change={setRoles} />
        )}
        {["create", "reset"].includes(editor.mode) && (
          <>
            <Field
              label={he.initialPassword}
              name="temporaryPassword"
              type="password"
              value={password}
              onChange={setPassword}
              maxLength={128}
              autoComplete="new-password"
              autoFocus={editor.mode === "reset"}
            />
            <p className="help">
              {he.passwordHelp} {he.temporaryHelp}
            </p>
          </>
        )}
        {editor.mode === "create" && (
          <label className="check">
            <input
              type="checkbox"
              checked={active}
              onChange={(e) => setActive(e.target.checked)}
            />
            {he.active}
          </label>
        )}
        <Notice error={error} />
        <div className="form-actions">
          <button className="primary" disabled={busy}>
            {busy ? he.saving : editor.mode === "create" ? he.create : he.save}
          </button>
          <button type="button" disabled={busy} onClick={close}>
            {he.cancel}
          </button>
        </div>
      </form>
    </dialog>
  );
}
export default function Users() {
  const [data, setData] = useState<UserPage | null>(null);
  const [search, setSearch] = useState("");
  const [query, setQuery] = useState("");
  const [page, setPage] = useState(1);
  const [version, setVersion] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [editor, setEditor] = useState<EditorState | null>(null);
  useEffect(() => {
    let alive = true;
    setBusy(true);
    setError("");
    api<UserPage>(
      `/users?page=${page}&pageSize=20&search=${encodeURIComponent(query)}`,
    )
      .then((value) => {
        if (alive) setData(value);
      })
      .catch((error) => {
        if (alive) setError(message(error));
      })
      .finally(() => {
        if (alive) setBusy(false);
      });
    return () => {
      alive = false;
    };
  }, [page, query, version]);
  async function toggle(user: User) {
    if (
      !window.confirm(user.isActive ? he.confirmDeactivate : he.confirmActivate)
    )
      return;
    setBusy(true);
    setError("");
    setSuccess("");
    try {
      await api(`/users/${user.id}/status`, "PATCH", {
        isActive: !user.isActive,
      });
      setSuccess(he.saved);
      setVersion((v) => v + 1);
    } catch (error) {
      setError(message(error));
    } finally {
      setBusy(false);
    }
  }
  return (
    <>
      <div className="page-heading">
        <div>
          <span className="eyebrow">{he.accountAccess}</span>
          <h1>{he.users}</h1>
          <p className="muted">{he.usersIntro}</p>
        </div>
        <button
          className="primary"
          onClick={() => {
            setSuccess("");
            setEditor({ mode: "create" });
          }}
        >
          <span aria-hidden="true">＋ </span>
          {he.createUser}
        </button>
      </div>
      <Notice error={error} success={success} />
      <section className="panel directory">
        <div className="directory-tools">
          <form
            role="search"
            onSubmit={(e) => {
              e.preventDefault();
              setPage(1);
              setQuery(search);
            }}
          >
            <input
              aria-label={he.searchLabel}
              placeholder={he.searchPlaceholder}
              value={search}
              maxLength={100}
              onChange={(e) => setSearch(e.target.value)}
            />
            <button>{he.search}</button>
          </form>
          <span className="muted">
            {data?.totalCount ?? "—"} {he.total}
          </span>
        </div>
        {busy && (
          <p className="loading" role="status">
            {he.loading}
          </p>
        )}
        <div className="table-scroll">
          <table>
            <thead>
              <tr>
                <th>{he.fullName}</th>
                <th>{he.username}</th>
                <th>{he.roles}</th>
                <th>{he.status}</th>
                <th>{he.actions}</th>
              </tr>
            </thead>
            <tbody>
              {data?.items.map((user) => (
                <tr key={user.id}>
                  <td>
                    <div className="table-name">
                      <span className="avatar small">
                        {user.firstName[0]}
                        {user.lastName[0]}
                      </span>
                      <strong>
                        {user.firstName} {user.lastName}
                      </strong>
                    </div>
                  </td>
                  <td dir="auto">{user.username}</td>
                  <td>
                    <Roles roles={user.roles} />
                  </td>
                  <td>
                    <span className={`status ${user.isActive ? "on" : "off"}`}>
                      {user.isActive ? he.active : he.inactive}
                    </span>
                  </td>
                  <td>
                    <div className="row-actions">
                      <button
                        disabled={busy}
                        onClick={() => setEditor({ mode: "edit", user })}
                      >
                        {he.editUser}
                      </button>
                      <button
                        disabled={busy}
                        onClick={() => setEditor({ mode: "roles", user })}
                      >
                        {he.editRoles}
                      </button>
                      <button
                        disabled={busy}
                        onClick={() => setEditor({ mode: "reset", user })}
                      >
                        {he.resetPassword}
                      </button>
                      <button disabled={busy} onClick={() => toggle(user)}>
                        {user.isActive ? he.deactivate : he.activate}
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {!busy && data?.items.length === 0 && (
          <p className="empty">{he.noUsers}</p>
        )}
        <div className="pagination">
          <span>
            {he.page} {page}
          </span>
          <div>
            <button
              disabled={page === 1 || busy}
              onClick={() => setPage(page - 1)}
            >
              {he.previous}
            </button>
            <button
              disabled={
                !data || page * data.pageSize >= data.totalCount || busy
              }
              onClick={() => setPage(page + 1)}
            >
              {he.next}
            </button>
          </div>
        </div>
      </section>
      {editor && (
        <UserEditor
          editor={editor}
          close={() => setEditor(null)}
          saved={(text) => {
            setEditor(null);
            setSuccess(text);
            setVersion((v) => v + 1);
          }}
        />
      )}
    </>
  );
}
