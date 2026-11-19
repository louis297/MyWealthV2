import { type FormEvent, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { Messages, Muted, Page, PageHeading, SelectField, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { SelectItem } from "@/components/ui/select";
import { type CustodyMode, useCreateAccountMutation } from "@/features/accounts/accountsApi";
import { useGetCurrenciesQuery } from "@/features/currencies/currenciesApi";

const accountTypes = ["Bank", "Cash", "Brokerage", "Other"] as const;

// D17: nothing is preselected. A Custodial account is always Brokerage (custody-modes R3).
// D21: only the firm's allowed custody modes are offered; one allowed mode is already selected.
const custodyOptions: { value: CustodyMode; label: string }[] = [
  { value: "External", label: "External — held elsewhere" },
  { value: "Custodial", label: "Custodial — held on the platform" },
];

type ProblemMessages = { custody: string[]; currency: string[]; other: string[] };

const noMessages: ProblemMessages = { custody: [], currency: [], other: [] };

// D33: shown instead of the currencies while Custodial is chosen and the firm has no client money account.
const noClientMoneyAccount = "Your firm has no client money account, so a Custodial account cannot be opened.";

// A refused custody mode (tenant-custody-modes R7) is shown at the Custody choice, a refused currency
// (client-money-accounts D33) at the currency picker; every other message on top.
function problemMessages(error: unknown): ProblemMessages {
  const none = noMessages;
  if (typeof error !== "object" || error === null || !("data" in error)) {
    return none;
  }
  const data = (error as { data?: { errors?: Record<string, string[]>; detail?: string } }).data;
  if (data?.errors) {
    const entries = Object.entries(data.errors);
    const field = (name: string) => ([key]: [string, string[]]) => key.toLowerCase() === name;
    const isCustody = field("custodymode");
    const isCurrency = field("currency");
    return {
      custody: entries.filter(isCustody).flatMap(([, values]) => values),
      currency: entries.filter(isCurrency).flatMap(([, values]) => values),
      other: entries.filter((entry) => !isCustody(entry) && !isCurrency(entry)).flatMap(([, values]) => values),
    };
  }
  if (data?.detail) {
    return { ...none, other: [data.detail] };
  }
  return none;
}

export function CreateAccountPage() {
  const { id: customerId } = useParams();
  const navigate = useNavigate();
  const [name, setName] = useState("");
  const [type, setType] = useState<string>(accountTypes[0]);
  const [custodyMode, setCustodyMode] = useState<CustodyMode | "">("");
  const [currency, setCurrency] = useState("");
  const [messages, setMessages] = useState<ProblemMessages>(noMessages);
  const [createAccount] = useCreateAccountMutation();
  const { data: currencies } = useGetCurrenciesQuery();
  const firm = useAppSelector((state) => state.session.tenant);

  // Until the tenant item has loaded, no custody option is offered.
  const options = firm ? custodyOptions.filter((option) => firm.allowedCustodyModes.includes(option.value)) : [];
  const [onlyOption] = options.length === 1 ? options : [];
  const selectedCustody: CustodyMode | "" = onlyOption
    ? onlyOption.value
    : options.some((option) => option.value === custodyMode)
      ? custodyMode
      : "";
  const selectedType = selectedCustody === "Custodial" ? "Brokerage" : type;
  // D33: a Custodial account is offered only the enabled currencies in the firm's clientMoneyCurrencies.
  const offeredCurrencies = (currencies ?? []).filter(
    (item) => selectedCustody !== "Custodial" || (firm?.clientMoneyCurrencies ?? []).includes(item.code),
  );
  const selectedCurrency = offeredCurrencies.some((item) => item.code === currency) ? currency : "";
  const custodialBlocked = selectedCustody === "Custodial" && offeredCurrencies.length === 0;

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!customerId || selectedCustody === "" || custodialBlocked) {
      return;
    }

    try {
      const created = await createAccount({
        customerId,
        name: name.trim(),
        type: selectedType,
        custodyMode: selectedCustody,
        currency: selectedCurrency,
      }).unwrap();
      void navigate(`/accounts/${created.id}`);
    } catch (error: unknown) {
      setMessages(problemMessages(error));
    }
  }

  return (
    <Page className="max-w-lg">
      <PageHeading title="New account" />
      <Messages messages={messages.other} />
      <Card>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={(event) => void onSubmit(event)}>
            <TextField id="account-name" label="Name" value={name} onChange={(event) => setName(event.target.value)} required />
            <fieldset className="flex flex-col gap-2">
              <legend className="mb-2 text-sm font-medium">Custody</legend>
              <div role="radiogroup" aria-label="Custody" aria-required="true" className="flex flex-col gap-2">
                {options.map((option) => (
                  <label key={option.value} className="flex items-center gap-2 text-sm">
                    <input
                      type="radio"
                      name="account-custody"
                      value={option.value}
                      checked={selectedCustody === option.value}
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
              <Messages messages={messages.custody} />
            </fieldset>
            <SelectField
              id="account-type"
              label="Type"
              value={selectedType}
              onChange={(event) => setType(event.target.value)}
              required
              disabled={selectedCustody === "Custodial"}
            >
              {accountTypes.map((item) => (
                <SelectItem key={item} value={item}>
                  {item}
                </SelectItem>
              ))}
            </SelectField>
            <div className="flex flex-col gap-2" data-field="currency">
              <SelectField
                id="account-currency"
                label="Currency"
                value={selectedCurrency}
                onChange={(event) => setCurrency(event.target.value)}
                required
              >
                <SelectItem value="">Select a currency</SelectItem>
                {offeredCurrencies.map((item) => (
                  <SelectItem key={item.code} value={item.code}>
                    {item.code}
                  </SelectItem>
                ))}
              </SelectField>
              {custodialBlocked ? <Muted>{noClientMoneyAccount}</Muted> : null}
              <Messages messages={messages.currency} />
            </div>
            <Button type="submit" disabled={selectedCustody === "" || custodialBlocked}>
              Create account
            </Button>
          </form>
        </CardContent>
      </Card>
    </Page>
  );
}
