import { type FormEvent, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { Messages, Page, PageHeading, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { type CustodyMode, useCreateAccountMutation } from "@/features/accounts/accountsApi";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";

const accountTypes = ["Bank", "Cash", "Brokerage", "Other"] as const;

// D17: nothing is preselected. A Custodial account is always Brokerage (custody-modes R3).
const custodyOptions: { value: CustodyMode; label: string }[] = [
  { value: "External", label: "External — held elsewhere" },
  { value: "Custodial", label: "Custodial — held on the platform" },
];

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
  const [custodyMode, setCustodyMode] = useState<CustodyMode | "">("");
  const [currency, setCurrency] = useState("");
  const [messages, setMessages] = useState<string[]>([]);
  const [createAccount] = useCreateAccountMutation();
  const { data: currencies } = useGetCurrenciesQuery();

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!customerId || custodyMode === "") {
      return;
    }

    try {
      const created = await createAccount({
        customerId,
        name: name.trim(),
        type,
        custodyMode,
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
            <fieldset className="flex flex-col gap-2">
              <legend className="mb-2 text-sm font-medium">Custody</legend>
              <div role="radiogroup" aria-label="Custody" aria-required="true" className="flex flex-col gap-2">
                {custodyOptions.map((option) => (
                  <label key={option.value} className="flex items-center gap-2 text-sm">
                    <input
                      type="radio"
                      name="account-custody"
                      value={option.value}
                      checked={custodyMode === option.value}
                      onChange={() => {
                        setCustodyMode(option.value);
                        if (option.value === "Custodial") {
                          setType("Brokerage");
                        }
                      }}
                    />
                    {option.label}
                  </label>
                ))}
              </div>
            </fieldset>
            <SelectField
              id="account-type"
              label="Type"
              value={type}
              onChange={(event) => setType(event.target.value)}
              required
              disabled={custodyMode === "Custodial"}
            >
              {accountTypes.map((item) => (
                <SelectItem key={item} value={item}>
                  {item}
                </SelectItem>
              ))}
            </SelectField>
            <SelectField
              id="account-currency"
              label="Currency"
              value={currency}
              onChange={(event) => setCurrency(event.target.value)}
              required
            >
              <SelectItem value="">Select a currency</SelectItem>
              {(currencies ?? []).map((item) => (
                <SelectItem key={item.code} value={item.code}>
                  {item.code}
                </SelectItem>
              ))}
            </SelectField>
            <Button type="submit" disabled={custodyMode === ""}>
              Create account
            </Button>
          </form>
        </CardContent>
      </Card>
    </Page>
  );
}
