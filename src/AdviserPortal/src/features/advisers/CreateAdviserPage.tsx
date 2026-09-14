import { type FormEvent, useState } from "react";
import { useNavigate } from "react-router";
import { useCreateAdviserMutation } from "@/features/advisers/advisersApi";

export function CreateAdviserPage() {
  const navigate = useNavigate();
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [createAdviser] = useCreateAdviserMutation();

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (password !== confirmPassword) {
      setError("Passwords do not match.");
      return;
    }

    const created = await createAdviser({
      name: name.trim(),
      email: email.trim(),
      password,
    }).unwrap();
    void navigate(`/advisers/${created.id}`);
  }

  return (
    <div className="max-w-lg space-y-4">
      <h1 className="text-xl font-medium">New adviser</h1>
      {error ? <p>{error}</p> : null}
      <form className="space-y-3" onSubmit={(event) => void onSubmit(event)}>
        <div>
          <label htmlFor="adviser-name" className="block text-sm">
            Name
          </label>
          <input
            id="adviser-name"
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={name}
            onChange={(event) => setName(event.target.value)}
            required
          />
        </div>
        <div>
          <label htmlFor="adviser-email" className="block text-sm">
            Email
          </label>
          <input
            id="adviser-email"
            type="email"
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            required
          />
        </div>
        <div>
          <label htmlFor="adviser-password" className="block text-sm">
            Password
          </label>
          <input
            id="adviser-password"
            type="password"
            minLength={8}
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            required
          />
        </div>
        <div>
          <label htmlFor="adviser-confirm-password" className="block text-sm">
            Confirm password
          </label>
          <input
            id="adviser-confirm-password"
            type="password"
            minLength={8}
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={confirmPassword}
            onChange={(event) => setConfirmPassword(event.target.value)}
            required
          />
        </div>
        <button type="submit" className="border border-slate-300 bg-white px-3 py-1 text-sm">
          Create adviser
        </button>
      </form>
    </div>
  );
}
