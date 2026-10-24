import { he, roleNames, type Role } from "../he";
export function Field({
  label,
  name,
  type = "text",
  value,
  onChange,
  autoComplete,
  maxLength = 100,
  autoFocus = false,
}: {
  label: string;
  name: string;
  type?: string;
  value: string;
  onChange: (value: string) => void;
  autoComplete?: string;
  maxLength?: number;
  autoFocus?: boolean;
}) {
  return (
    <label className="field">
      {label}
      <input
        name={name}
        type={type}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        autoComplete={autoComplete}
        maxLength={maxLength}
        required
        dir={type === "password" ? "ltr" : "auto"}
        autoFocus={autoFocus}
      />
    </label>
  );
}
export function Notice({
  error,
  success,
}: {
  error?: string;
  success?: string;
}) {
  return (
    <>
      {error && (
        <p role="alert" className="notice error">
          {error}
        </p>
      )}
      {success && (
        <p role="status" className="notice success">
          {success}
        </p>
      )}
    </>
  );
}
export function Roles({ roles }: { roles: Role[] }) {
  return (
    <div className="badges">
      {roles.map((role) => (
        <span className="badge" key={role}>
          {he.rolesLabel[role]}
        </span>
      ))}
    </div>
  );
}
export function RolePicker({
  value,
  change,
}: {
  value: Role[];
  change: (value: Role[]) => void;
}) {
  return (
    <fieldset>
      <legend>{he.roles}</legend>
      <div className="role-picker">
        {roleNames.map((role) => (
          <label key={role}>
            <input
              type="checkbox"
              checked={value.includes(role)}
              onChange={(e) =>
                change(
                  e.target.checked
                    ? [...value, role]
                    : value.filter((r) => r !== role),
                )
              }
            />
            {he.rolesLabel[role]}
          </label>
        ))}
      </div>
    </fieldset>
  );
}
