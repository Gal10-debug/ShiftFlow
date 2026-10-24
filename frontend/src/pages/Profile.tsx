import { useAuth } from "../auth";
import { formatDate, he } from "../he";
import { Roles } from "../components/forms";
import PasswordPage from "./PasswordPage";
export default function Profile() {
  const { user } = useAuth();
  return (
    <>
      <div className="page-heading">
        <div>
          <span className="eyebrow">{he.accountDetails}</span>
          <h1>{he.profile}</h1>
        </div>
      </div>
      <div className="profile-grid">
        <section className="panel">
          <div className="profile-identity">
            <span className="avatar large">
              {user!.firstName[0]}
              {user!.lastName[0]}
            </span>
            <h2>
              {user!.firstName} {user!.lastName}
            </h2>
          </div>
          <dl>
            <dt>{he.username}</dt>
            <dd dir="auto">{user!.username}</dd>
            <dt>{he.roles}</dt>
            <dd>
              <Roles roles={user!.roles} />
            </dd>
            <dt>{he.created}</dt>
            <dd>
              <time dateTime={user!.createdAt}>
                {formatDate(user!.createdAt)}
              </time>
            </dd>
          </dl>
        </section>
        <PasswordPage />
      </div>
    </>
  );
}
