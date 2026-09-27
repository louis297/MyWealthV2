export interface Customer {
  id: string;
  tenantId: string;
  adviserId: string;
  name: string;
  email: string;
  status: string;
  rowVersion: string;
  created: string;
}

export interface CustomerList {
  items: Customer[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface AdviserOption {
  id: string;
  name: string;
  email: string;
  status: string;
  rowVersion: string;
  tenantId: string;
  created: string;
}

export interface AdviserList {
  items: AdviserOption[];
  page: number;
  pageSize: number;
  totalCount: number;
}
