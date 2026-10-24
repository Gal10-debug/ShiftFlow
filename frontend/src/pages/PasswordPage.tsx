import { useState, type FormEvent } from "react";
import { Navigate, useNavigate } from "react-router-dom";
import { api, resetCsrf, type User } from "../api";
import { home, useAuth } from "../auth";
import { he, message } from "../he";
import { Field, Notice } from "../components/forms";
export default function PasswordPage({
  initial = false,
}: {
  initial?: boolean;
}) {
  const { user, setUser } = useAuth();
  const navigate = useNavigate();
  const [currentPassword, setCurrent] = useState("");
  const [newPassword, setNew] = useState("");
  const [confirm, setConfirm] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  async function submit(e: FormEvent) {
    e.preventDefault();
    setError("");
    setSuccess("");
    if (!currentPassword || !newPassword || !confirm) {
      setError(he.required);
      return;
    }
    if (newPassword !== confirm) {
      setError(he.mismatch);
      return;
    }
    setBusy(true);
    try {
      const updated = await api<User>("/auth/change-password", "POST", {
        currentPassword,
        newPassword,
      });
      resetCsrf();
      setUser(updated);
      setCurrent("");
      setNew("");
      setConfirm("");
      setSuccess(he.passwordChanged);
      if (initial) navigate(home(updated), { replace: true });
    } catch (error) {
      setError(message(error));
    } finally {
      setBusy(false);
    }
  }
  if (initial && !user!.mustChangePassword)
    return <Navigate to={home(user!)} replace />;
  return (
    <section className={initial ? "first-password" : ""}>
      <div className="panel password-panel">
        <span className="eyebrow">{he.secure}</span>
        <h1>{initial ? he.firstPassword : he.changePassword}</h1>
        {initial && <p className="muted">{he.firstPasswordIntro}</p>}
        <form noValidate onSubmit={submit}>
          <Field
            label={he.currentPassword}
            name="currentPassword"
            type="password"
            value={currentPassword}
            onChange={setCurrent}
            autoComplete="current-password"
            maxLength={128}
          />
          <Field
            label={he.newPassword}
            name="newPassword"
            type="password"
            value={newPassword}
            onChange={setNew}
            autoComplete="new-password"
            maxLength={128}
          />
          <p className="help">{he.passwordHelp}</p>
          <Field
            label={he.confirmPassword}
            name="confirmPassword"
            type="password"
            value={confirm}
            onChange={setConfirm}
            autoComplete="new-password"
            maxLength={128}
          />
          <Notice error={error} success={success} />
          <button className="primary" disabled={busy}>
            {busy ? he.saving : he.changePassword}
          </button>
        </form>
      </div>
    </section>
  );
}
