import { type FormEvent, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { ConflictNotice, Muted, Page, PageHeading, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { useGetAdvisersQuery } from "@/features/advisers/advisersApi";
import { useGetCustomerQuery, useUpdateCustomerMutation } from "@/features/customers/customersApi";
import { roles } from "@/features/session/roles";

function isConflict(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 409;
}

export function EditCustomerPage() {
  const { id } = useParams();
  const me = useAppSelector((state) => state.session.currentUser);
  const isTenantAdmin = me?.role === roles.tenantAdmin;
  const navigate = useNavigate();
  const { data, error, isLoading, refetch } = useGetCustomerQuery(id ?? "", { skip: !id });
  const { data: advisers } = useGetAdvisersQuery(
    { page: 1, pageSize: 100, enabledOnly: true },
    { skip: !isTenantAdmin },
  );
  const [updateCustomer] = useUpdateCustomerMutation();
  const [name, setName] = useState("");
  const [adviserId, setAdviserId] = useState("");
  const [conflict, setConflict] = useState(false);

  useEffect(() => {
    if (data) {
      setName(data.name);
      setAdviserId(data.adviserId);
    }
  }, [data]);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!data) {
      return;
    }

    try {
      await updateCustomer({
        id: data.id,
        name: name.trim(),
        rowVersion: data.rowVersion,
        ...(isTenantAdmin ? { adviserId } : {}),
      }).unwrap();
      void navigate(`/customers/${data.id}`);
    } catch (reason: unknown) {
      if (isConflict(reason)) {
        setConflict(true);
        void refetch();
      }
    }
  }

  if (error && "status" in error && error.status === 404) {
    return <Muted>Not found</Muted>;
  }

  if (isLoading || !data) {
    return <Muted>Loading…</Muted>;
  }

  return (
    <Page className="max-w-lg">
      <PageHeading title="Edit customer" />
      {conflict ? <ConflictNotice /> : null}
      <Card>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={(event) => void onSubmit(event)}>
            <TextField
              id="edit-customer-name"
              label="Name"
              value={name}
              onChange={(event) => setName(event.target.value)}
              required
            />
            {isTenantAdmin ? (
              <SelectField
                id="edit-customer-adviser"
                label="Adviser"
                value={adviserId}
                onChange={(event) => setAdviserId(event.target.value)}
                required
              >
                {(advisers?.items ?? [])
                  .filter((item) => item.status === "active" || item.id === data.adviserId)
                  .map((item) => (
                    <option key={item.id} value={item.id}>
                      {item.name}
                    </option>
                  ))}
              </SelectField>
            ) : null}
            <Button type="submit">Save</Button>
          </form>
        </CardContent>
      </Card>
    </Page>
  );
}
