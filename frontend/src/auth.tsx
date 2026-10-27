import {
  createContext,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from "react";
import { Navigate, Outlet, useLocation } from "react-router-dom";
import { api, resetCsrf, type User } from "./api";
import { he, message, type Role } from "./he";
interface Auth {
  user: User | null;
  setUser: (user: User | null) => void;
  loading: boolean;
}
const Context = createContext<Auth | null>(null);
export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  useEffect(() => {
    let alive = true;
    api<User>("/auth/me")
      .then((value) => {
        if (alive) setUser(value);
      })
      .catch((error) => {
        if (alive && (error as Error).message !== "UNAUTHENTICATED")
          setError(message(error));
      })
      .finally(() => {
        if (alive) setLoading(false);
      });
    const expired = () => {
      resetCsrf();
      setUser(null);
    };
    window.addEventListener("session-expired", expired);
    return () => {
      alive = false;
      window.removeEventListener("session-expired", expired);
    };
  }, []);
  return (
    <Context.Provider value={{ user, setUser, loading }}>
      {error && (
        <p className="global-alert" role="alert">
          {error}
        </p>
      )}
      {children}
    </Context.Provider>
  );
}
export function useAuth() {
  const value = useContext(Context);
  if (!value) throw new Error("AuthProvider required");
  return value;
}
export function home(user: User) {
  return user.mustChangePassword
    ? "/change-password"
    : user.roles.includes("ADMIN")
      ? "/users"
      : user.canManageTeamAvailability
        ? "/team-availability"
        : user.roles.includes("COORDINATOR")
        ? "/coordinator"
        : user.roles.includes("EMPLOYEE")
          ? "/employee"
          : "/profile";
}
export function Protected({
  role,
  allowPasswordChange = false,
}: {
  role?: Role;
  allowPasswordChange?: boolean;
}) {
  const { user, loading } = useAuth();
  const location = useLocation();
  if (loading)
    return (
      <p className="loading" role="status">
        {he.loading}
      </p>
    );
  if (!user)
    return <Navigate to="/login" state={{ from: location.pathname }} replace />;
  if (user.mustChangePassword && !allowPasswordChange)
    return <Navigate to="/change-password" replace />;
  if (role && !user.roles.includes(role))
    return <Navigate to="/unauthorized" replace />;
  return <Outlet />;
}
