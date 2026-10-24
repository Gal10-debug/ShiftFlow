import { Navigate, Route, Routes } from "react-router-dom";
import { AuthProvider, home, Protected, useAuth } from "./auth";
import { he } from "./he";
import Shell from "./components/Shell";
import Login from "./pages/Login";
import PasswordPage from "./pages/PasswordPage";
import Profile from "./pages/Profile";
import Users from "./pages/Users";
import AccessPage from "./pages/AccessPage";
import InfoPage from "./pages/InfoPage";
function Start() {
  const { user, loading } = useAuth();
  return loading ? (
    <p className="loading">{he.loading}</p>
  ) : (
    <Navigate to={user ? home(user) : "/login"} replace />
  );
}
export function AppRoutes() {
  return (
    <Routes>
      <Route path="/" element={<Start />} />
      <Route path="/login" element={<Login />} />
      <Route element={<Protected allowPasswordChange />}>
        <Route path="/change-password" element={<PasswordPage initial />} />
      </Route>
      <Route element={<Protected />}>
        <Route element={<Shell />}>
          <Route path="/profile" element={<Profile />} />
          <Route element={<Protected role="ADMIN" />}>
            <Route path="/users" element={<Users />} />
          </Route>
          <Route element={<Protected role="COORDINATOR" />}>
            <Route
              path="/coordinator"
              element={<AccessPage role="COORDINATOR" />}
            />
          </Route>
          <Route element={<Protected role="EMPLOYEE" />}>
            <Route path="/employee" element={<AccessPage role="EMPLOYEE" />} />
          </Route>
          <Route path="/unauthorized" element={<InfoPage />} />
        </Route>
      </Route>
      <Route path="*" element={<InfoPage missing />} />
    </Routes>
  );
}
export default function App() {
  return (
    <AuthProvider>
      <AppRoutes />
    </AuthProvider>
  );
}
