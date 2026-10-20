import { type FormEvent, useState } from "react";
import { useNavigate } from "react-router";
import { ErrorNotice, Page, PageHeading, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
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
    <Page className="max-w-lg">
      <PageHeading title="New adviser" />
      {error ? <ErrorNotice>{error}</ErrorNotice> : null}
      <Card>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={(event) => void onSubmit(event)}>
            <TextField id="adviser-name" label="Name" value={name} onChange={(event) => setName(event.target.value)} required />
            <TextField
              id="adviser-email"
              label="Email"
              type="email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              required
            />
            <TextField
              id="adviser-password"
              label="Password"
              type="password"
              minLength={8}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              required
            />
            <TextField
              id="adviser-confirm-password"
              label="Confirm password"
              type="password"
              minLength={8}
              value={confirmPassword}
              onChange={(event) => setConfirmPassword(event.target.value)}
              required
            />
            <Button type="submit">Create adviser</Button>
          </form>
        </CardContent>
      </Card>
    </Page>
  );
}
