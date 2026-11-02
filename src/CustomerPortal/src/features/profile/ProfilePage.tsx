import { type FormEvent, useState } from "react";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { Muted, Page, PageHeading, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
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
    return <Muted>Loading session…</Muted>;
  }

  const profile = me;

  async function onSaveName(event: FormEvent) {
    event.preventDefault();
    await updateMe({ name: name.trim(), rowVersion: profile.rowVersion }).unwrap();
  }

  async function onChangePassword(event: FormEvent) {
    event.preventDefault();
    await updateMePassword({ currentPassword, newPassword }).unwrap();
    dispatch(clearSession());
    startLogin();
  }

  return (
    <Page className="max-w-lg">
      <PageHeading title="Profile" />
      <p>{profile.email}</p>
      <Card>
        <CardHeader>
          <CardTitle>Name</CardTitle>
        </CardHeader>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={(event) => void onSaveName(event)}>
            <TextField
              id="profile-name"
              label="Name"
              value={name}
              onChange={(event) => setName(event.target.value)}
            />
            <Button type="submit">Save name</Button>
          </form>
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle>Password</CardTitle>
        </CardHeader>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={(event) => void onChangePassword(event)}>
            <TextField
              id="profile-current-password"
              label="Current password"
              type="password"
              value={currentPassword}
              onChange={(event) => setCurrentPassword(event.target.value)}
            />
            <TextField
              id="profile-new-password"
              label="New password"
              type="password"
              value={newPassword}
              onChange={(event) => setNewPassword(event.target.value)}
            />
            <Button type="submit">Change password</Button>
          </form>
        </CardContent>
      </Card>
    </Page>
  );
}
