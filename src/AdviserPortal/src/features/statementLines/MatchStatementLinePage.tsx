import { type FormEvent, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { Messages, Muted, Page, PageHeading, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { type Account, useGetAccountsQuery } from "@/features/accounts/accountsApi";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";
import { type Customer, useGetCustomerQuery, useGetCustomersQuery } from "@/features/customers/customersApi";
import { changedSentence } from "@/features/statementLines/StatementLineDetailPage";
import { problemMessages, problemStatus } from "@/features/statementLines/statementLineLabels";
import { useGetStatementLineQuery, useMatchStatementLineMutation } from "@/features/statementLines/statementLinesApi";
import { formatMoney } from "@/shared/format/money";

type Mode = "reference" | "customer";

/**
 * D36: match an unmatched receipt by hand. The account is found by its deposit reference, or by a Customer and
 * then the Customer's Open Custodial accounts in the receipt's currency.
 */
export function MatchStatementLinePage() {
  const { id = "" } = useParams();
  const navigate = useNavigate();
  const line = useGetStatementLineQuery(id, { skip: !id });
  const currencies = useGetCurrenciesQuery();
  const [mode, setMode] = useState<Mode>("reference");
  const [referenceInput, setReferenceInput] = useState("");
  const [reference, setReference] = useState<string | null>(null);
  const [customerInput, setCustomerInput] = useState("");
  const [customerSearch, setCustomerSearch] = useState<string | null>(null);
  const [customer, setCustomer] = useState<Customer | null>(null);
  const [selected, setSelected] = useState<Account | null>(null);
  const [note, setNote] = useState("");
  const [messages, setMessages] = useState<string[]>([]);
  const [accountError, setAccountError] = useState<string[]>([]);
  const [match, { isLoading }] = useMatchStatementLineMutation();

  const byReference = useGetAccountsQuery({ depositReference: reference ?? "", pageSize: 20 }, { skip: !reference });
  const customers = useGetCustomersQuery({ search: customerSearch ?? "" }, { skip: customerSearch === null });
  const customerAccounts = useGetAccountsQuery(
    { customerId: customer?.id, custodyMode: "Custodial", pageSize: 100 },
    { skip: !customer },
  );
  const selectedCustomer = useGetCustomerQuery(selected?.customerId ?? "", { skip: !selected || selected.customerId === customer?.id });

  if (!line.data) {
    return <Muted>{line.isError ? "Not found" : "Loading…"}</Muted>;
  }

  const receipt = line.data;
  const places = currencies.data?.find((item) => item.code === receipt.currency)?.decimalPlaces;
  const candidates = mode === "reference"
    ? byReference.data?.items ?? []
    : (customerAccounts.data?.items ?? []).filter((item) => item.status === "Open" && item.currency === receipt.currency);
  const ownerName = selected?.customerId === customer?.id ? customer?.name : selectedCustomer.data?.name;

  function choose(next: Mode) {
    setMode(next);
    setSelected(null);
    setAccountError([]);
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!selected) {
      return;
    }
    try {
      await match({
        id: receipt.id,
        accountId: selected.id,
        rowVersion: receipt.rowVersion,
        note: note.trim().length > 0 ? note.trim() : null,
      }).unwrap();
      void navigate(`/statement-lines/${receipt.id}`);
    } catch (problem: unknown) {
      if (problemStatus(problem) === 409) {
        setMessages([changedSentence]);
        void line.refetch();
        return;
      }
      const result = problemMessages(problem);
      setAccountError(result.fields.accountid ?? []);
      setMessages([...result.messages, ...Object.entries(result.fields).filter(([key]) => key !== "accountid").flatMap(([, values]) => values)]);
    }
  }

  return (
    <Page className="max-w-lg">
      <PageHeading title="Match receipt" />
      <p className="text-sm">{`${formatMoney(receipt.amount, receipt.currency, places)} from ${receipt.payerName ?? "unknown payer"}, reference ${receipt.reference ?? "none"}`}</p>
      <Messages messages={messages} />
      <div className="flex gap-2">
        <Button size="sm" variant={mode === "reference" ? "default" : "outline"} onClick={() => choose("reference")}>
          By deposit reference
        </Button>
        <Button size="sm" variant={mode === "customer" ? "default" : "outline"} onClick={() => choose("customer")}>
          By customer
        </Button>
      </div>
      <Card>
        <CardContent className="flex flex-col gap-4">
          {mode === "reference" ? (
            <div className="flex items-end gap-2">
              <TextField
                id="match-reference"
                label="Deposit reference"
                value={referenceInput}
                onChange={(event) => setReferenceInput(event.target.value)}
              />
              <Button type="button" size="sm" onClick={() => { setSelected(null); setReference(referenceInput.trim() || null); }}>
                Find
              </Button>
            </div>
          ) : (
            <>
              <div className="flex items-end gap-2">
                <TextField
                  id="match-customer"
                  label="Customer"
                  value={customerInput}
                  onChange={(event) => setCustomerInput(event.target.value)}
                />
                <Button type="button" size="sm" onClick={() => { setCustomer(null); setSelected(null); setCustomerSearch(customerInput.trim()); }}>
                  Search
                </Button>
              </div>
              {!customer ? (
                <div className="flex flex-col items-start gap-1">
                  {(customers.data?.items ?? []).map((item) => (
                    <Button key={item.id} type="button" size="sm" variant="ghost" onClick={() => setCustomer(item)}>
                      {`${item.name} (${item.email})`}
                    </Button>
                  ))}
                </div>
              ) : (
                <p className="text-sm">{`Customer: ${customer.name}`}</p>
              )}
            </>
          )}
          {mode === "reference" && reference && byReference.isSuccess && candidates.length === 0 ? (
            <Muted>No account has this deposit reference.</Muted>
          ) : null}
          {mode === "customer" && customer && customerAccounts.isSuccess && candidates.length === 0 ? (
            <Muted>{`This customer has no open Custodial account in ${receipt.currency}.`}</Muted>
          ) : null}
          <div className="flex flex-col items-start gap-1">
            {candidates.map((item) => (
              <Button
                key={item.id}
                type="button"
                size="sm"
                variant={selected?.id === item.id ? "default" : "outline"}
                onClick={() => { setSelected(item); setAccountError([]); }}
              >
                {`${item.name} · ${item.currency} · ${item.depositReference ?? ""}`}
              </Button>
            ))}
          </div>
          {accountError.length > 0 ? <p className="text-sm text-destructive">{accountError.join(" ")}</p> : null}
          {selected ? (
            <form className="flex flex-col gap-4" onSubmit={(event) => void onSubmit(event)}>
              <TextField id="match-note" label="Note" value={note} maxLength={200} onChange={(event) => setNote(event.target.value)} />
              <p className="text-sm font-medium">
                {`Credit ${formatMoney(receipt.amount, receipt.currency, places)} to ${selected.name} of ${ownerName ?? "…"}.`}
              </p>
              <Button type="submit" disabled={isLoading}>
                Confirm match
              </Button>
            </form>
          ) : null}
        </CardContent>
      </Card>
    </Page>
  );
}
