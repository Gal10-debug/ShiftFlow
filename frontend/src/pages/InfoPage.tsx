import { Link } from "react-router-dom";
import { he } from "../he";
export default function InfoPage({ missing = false }: { missing?: boolean }) {
  return (
    <main className="panel info-page">
      <h1>{missing ? he.notFound : he.unauthorized}</h1>
      <p>{missing ? he.notFoundBody : he.unauthorizedBody}</p>
      <Link to="/profile">{he.back}</Link>
    </main>
  );
}
