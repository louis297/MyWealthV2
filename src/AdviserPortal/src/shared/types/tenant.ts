export type Tenant = {
  id: string;
  name: string;
  code: string;
  reportingCurrency: string;
  // tenant-custody-modes R5: always in the order External, Custodial.
  allowedCustodyModes: ("External" | "Custodial")[];
  // client-money-accounts R23: currencies with an active client money account, A–Z.
  clientMoneyCurrencies: string[];
  isActive: boolean;
  rowVersion: string;
  created: string;
};
