export interface CurrentUser {
  id: string;
  name: string;
  email: string;
  role: string;
  status: string;
  tenantId: string | null;
  tenantCode: string | null;
  adviserId: string | null;
  rowVersion: string;
}

export interface TenantSummary {
  id: string;
  name: string;
  code: string;
}
