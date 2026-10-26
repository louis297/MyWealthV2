import { type FormEvent, useState } from "react";
import { useNavigate } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { ErrorNotice, Page, PageHeading, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { useGetAdvisersQuery } from "@/features/advisers/advisersApi";
import { useCreateCustomerMutation } from "@/features/customers/customersApi";
import { roles } from "@/features/session/roles";

export function CreateCustomerPage() {
  const me = useAppSelector((state) => state.session.currentUser);
  const isTenantAdmin = me?.role === roles.tenantAdmin;
  const navigate = useNavigate();
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [adviserId, setAdviserId] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [createCustomer] = useCreateCustomerMutation();
  const { data: advisers } = useGetAdvisersQuery(
    { page: 1, pageSize: 100, enabledOnly: true },
    { skip: !isTenantAdmin },
  );

  const activeAdvisers = (advisers?.items ?? []).filter((item) => item.status === "active");

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (password !== confirmPassword) {
      setError("Passwords do not match.");
      return;
    }

    const body = {
      name: name.trim(),
      email: email.trim(),
      password,
      ...(isTenantAdmin ? { adviserId } : {}),
    };

    const created = await createCustomer(body).unwrap();
    void navigate(`/customers/${created.id}`);
  }

  return (
    <Page className="max-w-lg">
      <PageHeading title="New customer" />
      {error ? <ErrorNotice>{error}</ErrorNotice> : null}
      <Card>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={(event) => void onSubmit(event)}>
            <TextField id="customer-name" label="Name" value={name} onChange={(event) => setName(event.target.value)} required />
            <TextField
              id="customer-email"
              label="Email"
              type="email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              required
            />
            <TextField
              id="customer-password"
              label="Password"
              type="password"
              minLength={8}
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              required
            />
            <TextField
              id="customer-confirm-password"
              label="Confirm password"
              type="password"
              minLength={8}
              value={confirmPassword}
              onChange={(event) => setConfirmPassword(event.target.value)}
              required
            />
            {isTenantAdmin ? (
              <SelectField
                id="customer-adviser"
                label="Adviser"
                value={adviserId}
                onChange={(event) => setAdviserId(event.target.value)}
                required
              >
                <SelectItem value="">Select an adviser</SelectItem>
                {activeAdvisers.map((item) => (
                  <SelectItem key={item.id} value={item.id}>
                    {item.name}
                  </SelectItem>
                ))}
              </SelectField>
            ) : null}
            <Button type="submit">Create customer</Button>
          </form>
        </CardContent>
      </Card>
    </Page>
  );
}
