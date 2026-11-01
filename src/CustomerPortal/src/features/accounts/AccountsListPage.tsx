import { Link } from "react-router";
import { useGetAccountsQuery } from "@/features/accounts/accountsApi";

export function AccountsListPage() {
  const { data, isLoading, isError } = useGetAccountsQuery();

  return (
    <section>
      <h1>Accounts</h1>
      {isLoading ? <p>Loading session…</p> : null}
      {isError ? <p>Accounts are not available.</p> : null}
      {data && data.items.length === 0 ? <p>No accounts.</p> : null}
      {data && data.items.length > 0 ? (
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Type</th>
              <th>Currency</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {data.items.map((account) => (
              <tr key={account.id}>
                <td><Link to={`/accounts/${account.id}`}>{account.name}</Link></td>
                <td>{account.type}</td>
                <td>{account.currency}</td>
                <td>{account.status}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}
    </section>
  );
}
