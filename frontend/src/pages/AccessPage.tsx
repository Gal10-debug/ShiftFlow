import { useEffect, useState } from "react";
import { api } from "../api";
import { he, message } from "../he";
import { Notice } from "../components/forms";
export default function AccessPage({
  role,
}: {
  role: "COORDINATOR" | "EMPLOYEE";
}) {
  const [allowed, setAllowed] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => {
    let alive = true;
    api(`/access/${role.toLowerCase()}`)
      .then(() => {
        if (alive) setAllowed(true);
      })
      .catch((error) => {
        if (alive) setError(message(error));
      });
    return () => {
      alive = false;
    };
  }, [role]);
  return (
    <section className="panel empty-page">
      <span className="eyebrow">{he.workspace}</span>
      <h1>{role === "COORDINATOR" ? he.coordinator : he.employee}</h1>
      <Notice error={error} />
      {allowed ? (
        <p className="muted">
          {role === "COORDINATOR" ? he.coordinatorBody : he.employeeBody}
        </p>
      ) : (
        !error && <p>{he.loading}</p>
      )}
    </section>
  );
}
