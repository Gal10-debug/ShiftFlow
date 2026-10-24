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
  const [reveal, setReveal] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    // Password managers can update DOM values without firing React change events.
    const element = e.currentTarget;
    const form = new FormData(element);
    const submittedCurrent = String(form.get("currentPassword") ?? "");
    const submittedNew = String(form.get("newPassword") ?? "");
    const submittedConfirm = String(form.get("confirmPassword") ?? "");
    setError("");
    setSuccess("");
    if (!submittedCurrent || !submittedNew || !submittedConfirm) {
      setError(he.required);
      return;
    }
    if (submittedNew !== submittedConfirm) {
      setError(he.mismatch);
      return;
    }
    setBusy(true);
    try {
      const updated = await api<User>("/auth/change-password", "POST", {
        currentPassword: submittedCurrent,
        newPassword: submittedNew,
      });
      resetCsrf();
      setUser(updated);
      element.reset();
      setReveal(false);
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
            reveal={reveal}
            autoComplete="current-password"
            maxLength={128}
          />
          <Field
            label={he.newPassword}
            name="newPassword"
            type="password"
            reveal={reveal}
            autoComplete="new-password"
            maxLength={128}
          />
          <p className="help">{he.passwordHelp}</p>
          <Field
            label={he.confirmPassword}
            name="confirmPassword"
            type="password"
            reveal={reveal}
            autoComplete="new-password"
            maxLength={128}
          />
          <label className="check">
            <input
              type="checkbox"
              checked={reveal}
              onChange={(event) => setReveal(event.target.checked)}
            />
            {he.showPasswords}
          </label>
          <Notice error={error} success={success} />
          <button className="primary" disabled={busy}>
            {busy ? he.saving : he.changePassword}
          </button>
        </form>
      </div>
    </section>
  );
}
