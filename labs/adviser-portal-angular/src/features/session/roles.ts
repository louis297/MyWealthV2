export const roles = {
  systemAdmin: 'systemAdmin',
  tenantAdmin: 'tenantAdmin',
  adviser: 'adviser',
  customer: 'customer',
} as const;

export function canAccessCustomers(role: string | null | undefined): boolean {
  return role === roles.tenantAdmin || role === roles.adviser;
}

export function canAccessAdvisers(role: string | null | undefined): boolean {
  return role === roles.tenantAdmin;
}

export function canAccessProfile(role: string | null | undefined): boolean {
  return role === roles.tenantAdmin || role === roles.adviser || role === roles.systemAdmin;
}
