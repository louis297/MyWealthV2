import { type FormEvent, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { Messages, Page, PageHeading, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { useAddNominatedBankAccountMutation } from "@/features/nominatedBankAccounts/nominatedBankAccountsApi";
import { problemMessages } from "@/features/statementLines/statementLineLabels";

function FieldError({ messages }: { messages?: string[] }) {
  return messages?.length ? <p className="text-sm text-destructive">{messages.join(" ")}</p> : null;
}

/** D41: add a nominated bank account; a TenantAdmin checks it before money can be paid to it. */
export function AddBankAccountPage() {
  const { id: customerId = "" } = useParams();
  const navigate = useNavigate();
  const currencies = useGetCurrenciesQuery();
  const [add, { isLoading }] = useAddNominatedBankAccountMutation();
  const [bankName, setBankName] = useState("");
  const [accountName, setAccountName] = useState("");
  const [accountNumber, setAccountNumber] = useState("");
  const [currency, setCurrency] = useState("");
  const [fields, setFields] = useState<Record<string, string[]>>({});
  const [messages, setMessages] = useState<string[]>([]);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    try {
      await add({ customerId, currency, bankName, accountName, accountNumber }).unwrap();
      void navigate(`/customers/${customerId}`);
    } catch (error: unknown) {
      const result = problemMessages(error);
      setFields(result.fields);
      setMessages(result.messages);
    }
  }

  return (
    <Page className="max-w-lg">
      <PageHeading title="Add bank account" />
      <p className="text-sm">
        Add only an account in the customer's own name. A firm administrator checks it before money can be paid to it.
      </p>
      <Messages messages={messages} />
      <Card>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={(event) => void onSubmit(event)}>
            <TextField id="bank-name" label="Bank" value={bankName} maxLength={100} required
              onChange={(event) => setBankName(event.target.value)} />
            <FieldError messages={fields.bankname} />
            <TextField id="account-name" label="Account name" value={accountName} maxLength={140} required
              onChange={(event) => setAccountName(event.target.value)} />
            <FieldError messages={fields.accountname} />
            <TextField id="account-number" label="Account number" value={accountNumber} maxLength={34} required
              onChange={(event) => setAccountNumber(event.target.value)} />
            <FieldError messages={fields.accountnumber} />
            <SelectField id="bank-account-currency" label="Currency" value={currency} required
              onChange={(event) => setCurrency(event.target.value)}>
              <SelectItem value="">Select a currency</SelectItem>
              {(currencies.data ?? []).map((item) => (
                <SelectItem key={item.code} value={item.code}>
                  {item.code} {item.name}
                </SelectItem>
              ))}
            </SelectField>
            <FieldError messages={fields.currency} />
            <Button type="submit" disabled={isLoading}>
              Add bank account
            </Button>
          </form>
        </CardContent>
      </Card>
    </Page>
  );
}
