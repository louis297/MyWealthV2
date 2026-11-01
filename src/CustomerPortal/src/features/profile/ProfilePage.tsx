import { type FormEvent, useState } from "react";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { useUpdateMeMutation, useUpdateMePasswordMutation } from "@/features/profile/profileApi";
import { startLogin } from "@/features/session/session";
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
    if (!me) {
      return;
    }
    await updateMe({ name: name.trim(), rowVersion: me.rowVersion }).unwrap();
  }

  async function onChangePassword(event: FormEvent) {
    event.preventDefault();
    await updateMePassword({ currentPassword, newPassword }).unwrap();
    dispatch(clearSession());
    startLogin();
  }

  return (
    <section>
      <h1>Profile</h1>
      <p>{me.email}</p>
      <form onSubmit={(event) => void onSaveName(event)}>
        <label>
          Name
          <input aria-label="Name" value={name} onChange={(event) => setName(event.target.value)} />
        </label>
        <button type="submit">Save name</button>
      </form>
      <form onSubmit={(event) => void onChangePassword(event)}>
        <label>
          Current password
          <input
            aria-label="Current password"
            type="password"
            value={currentPassword}
            onChange={(event) => setCurrentPassword(event.target.value)}
          />
        </label>
        <label>
          New password
          <input
            aria-label="New password"
            type="password"
            value={newPassword}
            onChange={(event) => setNewPassword(event.target.value)}
          />
        </label>
        <button type="submit">Change password</button>
      </form>
    </section>
  );
}
