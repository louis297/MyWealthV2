import { startEndSession } from "@/features/session/session";

export function ForbiddenPage() {
  return (
    <section>
      <h1>Forbidden</h1>
      <p>You do not have access to this page.</p>
      <button type="button" onClick={() => void startEndSession()}>Sign out</button>
    </section>
  );
}
