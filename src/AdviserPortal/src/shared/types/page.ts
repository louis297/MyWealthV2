export type PagedList<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
};
