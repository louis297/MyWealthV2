export const roles = {
  systemAdmin: "systemAdmin",
  tenantAdmin: "tenantAdmin",
  adviser: "adviser",
  customer: "customer",
} as const;

export type Role = (typeof roles)[keyof typeof roles];

export function isRole(value: string | null | undefined): value is Role {
  return value === roles.systemAdmin
    || value === roles.tenantAdmin
    || value === roles.adviser
    || value === roles.customer;
}

export function canUseAdviserPortal(role: string | null | undefined) {
  return role === roles.tenantAdmin || role === roles.adviser;
}

export function shouldLoadTenant(role: string | null | undefined, tenantCode: string | null | undefined) {
  return Boolean(tenantCode) && (role === roles.tenantAdmin || role === roles.adviser);
}

export function canAccessCustomers(role: string | null | undefined) {
  return role === roles.tenantAdmin || role === roles.adviser;
}

export function canAccessDashboard(role: string | null | undefined) {
  return role === roles.tenantAdmin || role === roles.adviser;
}

export function canAccessReports(role: string | null | undefined) {
  return role === roles.tenantAdmin || role === roles.adviser;
}

export function canAccessPortfolios(role: string | null | undefined) {
  return role === roles.tenantAdmin || role === roles.adviser;
}

export function canAccessAccounts(role: string | null | undefined) {
  return role === roles.tenantAdmin || role === roles.adviser;
}

export function canAccessTransactions(role: string | null | undefined) {
  return role === roles.tenantAdmin || role === roles.adviser;
}

export function canAccessAdvisers(role: string | null | undefined) {
  return role === roles.tenantAdmin;
}

export function canAccessProfile(role: string | null | undefined) {
  return role === roles.tenantAdmin || role === roles.adviser;
}

export function canAccessFees(role: string | null | undefined) {
  return role === roles.tenantAdmin || role === roles.adviser;
}

/** fees.manage: create, edit and delete trade fee schedules (adviser-portal-fees F4). */
export function canManageFees(role: string | null | undefined) {
  return role === roles.tenantAdmin;
}

/** statement-lines.manage: the Bank receipts pages (deposit-matching D34). */
export function canManageStatementLines(role: string | null | undefined) {
  return role === roles.tenantAdmin;
}

/** bank-reconciliation D51, D52: the Bank reconciliation pages and the firm reports are for a TenantAdmin. */
export function canManageBankReconciliations(role: string | null | undefined) {
  return role === roles.tenantAdmin;
}

/** nominated-bank-accounts.verify: the Bank account checks page (withdrawals D42). */
export function canCheckBankAccounts(role: string | null | undefined) {
  return role === roles.tenantAdmin;
}

/** withdrawals.read for staff: the Withdrawals pages (withdrawals D46). */
export function canAccessWithdrawals(role: string | null | undefined) {
  return role === roles.tenantAdmin || role === roles.adviser;
}
