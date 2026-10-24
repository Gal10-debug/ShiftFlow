import { useState, type FormEvent } from "react";
import { Navigate } from "react-router-dom";
import { api, resetCsrf, type User } from "../api";
import { home, useAuth } from "../auth";
import { he, message } from "../he";
import { Field, Notice } from "../components/forms";
export default function Login() {
  const { user, setUser, loading } = useAuth();
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  if (loading) return <p className="loading">{he.loading}</p>;
  if (user) return <Navigate to={home(user)} replace />;
  async function submit(e: FormEvent) {
    e.preventDefault();
    setError("");
    if (!username.trim() || !password) {
      setError(he.required);
      return;
    }
    setBusy(true);
    try {
      const result = await api<User>("/auth/login", "POST", {
        username,
        password,
      });
      resetCsrf();
      setUser(result);
    } catch (error) {
      setError(message(error));
    } finally {
      setBusy(false);
      setPassword("");
    }
  }
  return (
    <main className="login-layout">
      <aside className="login-story">
        <div className="brand">
          <span className="brand-symbol" aria-hidden="true">
            ש
          </span>
          {he.brand}
        </div>
        <div>
          <span className="eyebrow">{he.workspace}</span>
          <h1>{he.loginAsideTitle}</h1>
          <p>{he.loginAsideBody}</p>
          <div className="team-art" aria-hidden="true">
            <span>א</span>
            <span>מ</span>
            <span>ש</span>
            <span>ד</span>
            <span>נ</span>
          </div>
        </div>
        <p className="story-foot">{he.secure}</p>
      </aside>
      <section className="login-main">
        <div className="login-card">
          <span className="eyebrow">{he.tagline}</span>
          <h2>{he.loginTitle}</h2>
          <p className="muted">{he.loginSubtitle}</p>
          <form noValidate onSubmit={submit}>
            <Field
              label={he.username}
              name="username"
              value={username}
              onChange={setUsername}
              autoComplete="username"
              autoFocus
            />
            <Field
              label={he.password}
              name="password"
              type="password"
              value={password}
              onChange={setPassword}
              maxLength={128}
              autoComplete="current-password"
            />
            <Notice error={error} />
            <button className="primary wide" disabled={busy}>
              {busy ? he.connecting : he.login}
            </button>
          </form>
          <p className="login-foot">{he.private}</p>
        </div>
      </section>
    </main>
  );
}
