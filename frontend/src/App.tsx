import { Navigate, Route, Routes } from "react-router-dom";
import { AuthProvider, home, Protected, useAuth } from "./auth";
import { he } from "./he";
import Shell from "./components/Shell";
import Login from "./pages/Login";
import PasswordPage from "./pages/PasswordPage";
import Profile from "./pages/Profile";
import Users from "./pages/Users";
import AccessPage from "./pages/AccessPage";
import Availability from "./pages/Availability";
import AvailabilityAdmin from "./pages/AvailabilityAdmin";
import ShiftExchanges from "./pages/ShiftExchanges";
import MySchedule from "./pages/MySchedule";
import SchedulingBoard from "./pages/SchedulingBoard";
import Teams from "./pages/Teams";
import TeamAvailability from "./pages/TeamAvailability";
import Absences from "./pages/Absences";
import InfoPage from "./pages/InfoPage";
function PersonalAvailability() {
  const { user } = useAuth();
  if (user?.roles.includes("ADMIN")) return <section dir="rtl"><h1>הזמינות שלי</h1><p>מנהל המערכת אינו משתתף בסידור העבודה ואינו נדרש להגיש זמינות אישית.</p></section>;
  return user?.isTeamLeader
    ? <section dir="rtl"><h1>הזמינות שלי</h1><p>ראש צוות אינו משתתף בסידור האוטומטי ואינו נדרש להגיש זמינות אישית.</p></section>
    : <Availability />;
}
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
          <Route path="/team-availability" element={<TeamAvailability />} />
          <Route path="/team-absences" element={<Absences manage />} />
          <Route path="/profile" element={<Profile />} />
          <Route element={<Protected role="ADMIN" />}>
            <Route path="/exchange-management" element={<ShiftExchanges admin />} />
            <Route path="/teams" element={<Teams />} />
            <Route path="/absence-management" element={<Absences manage />} />
            <Route path="/users" element={<Users />} />
            <Route path="/scheduling" element={<SchedulingBoard />} />
            <Route path="/availability-admin" element={<AvailabilityAdmin />} />
          </Route>
          <Route element={<Protected role="COORDINATOR" />}>
            <Route
              path="/coordinator"
              element={<AccessPage role="COORDINATOR" />}
            />
          </Route>
          <Route element={<Protected role="EMPLOYEE" />}>
            <Route path="/shift-exchanges" element={<ShiftExchanges />} />
            <Route path="/my-schedule" element={<MySchedule />} />
            <Route path="/absences" element={<Absences />} />
            <Route path="/employee" element={<PersonalAvailability />} />
            <Route path="/availability" element={<PersonalAvailability />} />
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
