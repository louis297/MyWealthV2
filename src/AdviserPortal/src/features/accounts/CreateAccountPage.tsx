import { type FormEvent, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { Messages, Page, PageHeading, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { useCreateAccountMutation } from "@/features/accounts/accountsApi";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";

const accountTypes = ["Bank", "Cash", "Brokerage", "Other"] as const;

function problemMessages(error: unknown): string[] {
  if (typeof error !== "object" || error === null || !("data" in error)) {
    return [];
  }
  const data = (error as { data?: { errors?: Record<string, string[]>; detail?: string } }).data;
  if (data?.errors) {
    return Object.values(data.errors).flat();
  }
  if (data?.detail) {
    return [data.detail];
  }
  return [];
}

export function CreateAccountPage() {
  const { id: customerId } = useParams();
  const navigate = useNavigate();
  const [name, setName] = useState("");
  const [type, setType] = useState<string>(accountTypes[0]);
  const [currency, setCurrency] = useState("");
  const [messages, setMessages] = useState<string[]>([]);
  const [createAccount] = useCreateAccountMutation();
  const { data: currencies } = useGetCurrenciesQuery();

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!customerId) {
      return;
    }

    try {
      const created = await createAccount({
        customerId,
        name: name.trim(),
        type,
        currency,
      }).unwrap();
      void navigate(`/accounts/${created.id}`);
    } catch (error: unknown) {
      setMessages(problemMessages(error));
    }
  }

  return (
    <Page className="max-w-lg">
      <PageHeading title="New account" />
      <Messages messages={messages} />
      <Card>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={(event) => void onSubmit(event)}>
            <TextField id="account-name" label="Name" value={name} onChange={(event) => setName(event.target.value)} required />
            <SelectField id="account-type" label="Type" value={type} onChange={(event) => setType(event.target.value)} required>
              {accountTypes.map((item) => (
                <option key={item} value={item}>
                  {item}
                </option>
              ))}
            </SelectField>
            <SelectField
              id="account-currency"
              label="Currency"
              value={currency}
              onChange={(event) => setCurrency(event.target.value)}
              required
            >
              <option value="">Select a currency</option>
              {(currencies ?? []).map((item) => (
                <option key={item.code} value={item.code}>
                  {item.code}
                </option>
              ))}
            </SelectField>
            <Button type="submit">Create account</Button>
          </form>
        </CardContent>
      </Card>
    </Page>
  );
}
