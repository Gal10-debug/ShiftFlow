import { useState } from "react";
import { Link, NavLink, Outlet } from "react-router-dom";
import { api, resetCsrf } from "../api";
import { home, useAuth } from "../auth";
import { av } from "../availability";
import { he, message } from "../he";
import { Notice } from "./forms";
export default function Shell() {
  const { user, setUser } = useAuth();
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  async function logout() {
    setBusy(true);
    try {
      await api("/auth/logout", "POST");
      resetCsrf();
      setUser(null);
    } catch (error) {
      setError(message(error));
    } finally {
      setBusy(false);
    }
  }
  return (
    <div className="app-shell">
      <aside className="sidebar">
        <Link to={home(user!)} className="brand">
          <span className="brand-symbol" aria-hidden="true">
            ש
          </span>
          {he.brand}
        </Link>
        <span className="nav-label">{he.workspace}</span>
        <nav aria-label={he.workspace}>
          {user!.roles.includes("ADMIN") && (
            <>
              <NavLink to="/users">{he.users}</NavLink>
              <NavLink to="/availability-admin">{av.adminTitle}</NavLink>
            </>
          )}
          {user!.roles.includes("COORDINATOR") && (
            <NavLink to="/coordinator">{he.coordinator}</NavLink>
          )}
          {user!.roles.includes("EMPLOYEE") && (
            <NavLink to="/employee">{av.title}</NavLink>
          )}
          <NavLink to="/profile">{he.profile}</NavLink>
        </nav>
        <div className="sidebar-foot">
          <span className="avatar">
            {user!.firstName.slice(0, 1)}
            {user!.lastName.slice(0, 1)}
          </span>
          <div>
            <strong>
              {user!.firstName} {user!.lastName}
            </strong>
            <small>
              {user!.roles.map((r) => he.rolesLabel[r]).join(" · ")}
            </small>
          </div>
        </div>
      </aside>
      <div className="workspace">
        <header className="topbar">
          <span>
            {he.greeting} <strong>{user!.firstName}</strong>
          </span>
          <button onClick={logout} disabled={busy}>
            {he.logout}
          </button>
        </header>
        <main className="main-content">
          <Notice error={error} />
          <Outlet />
        </main>
      </div>
    </div>
  );
}
