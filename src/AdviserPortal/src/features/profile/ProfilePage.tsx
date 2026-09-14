import { type FormEvent, useState } from "react";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { useUpdateMeMutation, useUpdateMePasswordMutation } from "@/features/profile/profileApi";
import { startAuthorize } from "@/features/session/oidc";
import { clearSession } from "@/features/session/sessionSlice";

export function ProfilePage() {
  const me = useAppSelector((state) => state.session.currentUser);
  const dispatch = useAppDispatch();
  const [name, setName] = useState(me?.name ?? "");
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [updateMe] = useUpdateMeMutation();
  const [updateMePassword] = useUpdateMePasswordMutation();

  if (!me) {
    return <p>Loading session…</p>;
  }

  async function onSaveName(event: FormEvent) {
    event.preventDefault();
    await updateMe({ name: name.trim(), rowVersion: me.rowVersion }).unwrap();
  }

  async function onChangePassword(event: FormEvent) {
    event.preventDefault();
    await updateMePassword({ currentPassword, newPassword }).unwrap();
    dispatch(clearSession());
    await startAuthorize();
  }

  return (
    <div className="max-w-lg space-y-8">
      <h1 className="text-xl font-medium">Profile</h1>
      <dl className="space-y-2 text-sm">
        <div>
          <dt className="text-slate-500">Email</dt>
          <dd>{me.email}</dd>
        </div>
        <div>
          <dt className="text-slate-500">Role</dt>
          <dd>{me.role}</dd>
        </div>
        <div>
          <dt className="text-slate-500">Status</dt>
          <dd>{me.status}</dd>
        </div>
        <div>
          <dt className="text-slate-500">Tenant</dt>
          <dd>{me.tenantCode ?? "—"}</dd>
        </div>
        <div>
          <dt className="text-slate-500">Id</dt>
          <dd>{me.id}</dd>
        </div>
      </dl>
      <form className="space-y-3" onSubmit={(event) => void onSaveName(event)}>
        <div>
          <label htmlFor="profile-name" className="block text-sm">
            Name
          </label>
          <input
            id="profile-name"
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={name}
            maxLength={200}
            onChange={(event) => setName(event.target.value)}
          />
        </div>
        <button type="submit" className="border border-slate-300 bg-white px-3 py-1 text-sm">
          Save name
        </button>
      </form>
      <form className="space-y-3" onSubmit={(event) => void onChangePassword(event)}>
        <div>
          <label htmlFor="profile-current-password" className="block text-sm">
            Current password
          </label>
          <input
            id="profile-current-password"
            type="password"
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={currentPassword}
            onChange={(event) => setCurrentPassword(event.target.value)}
          />
        </div>
        <div>
          <label htmlFor="profile-new-password" className="block text-sm">
            New password
          </label>
          <input
            id="profile-new-password"
            type="password"
            minLength={8}
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={newPassword}
            onChange={(event) => setNewPassword(event.target.value)}
          />
        </div>
        <button type="submit" className="border border-slate-300 bg-white px-3 py-1 text-sm">
          Change password
        </button>
      </form>
    </div>
  );
}
