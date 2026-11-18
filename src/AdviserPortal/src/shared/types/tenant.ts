export type Tenant = {
  id: string;
  name: string;
  code: string;
  reportingCurrency: string;
  // tenant-custody-modes R5: always in the order External, Custodial.
  allowedCustodyModes: ("External" | "Custodial")[];
  isActive: boolean;
  rowVersion: string;
  created: string;
};
