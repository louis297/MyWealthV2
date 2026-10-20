import { type FormEvent, useState } from "react";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { DetailGrid, DetailItem, Muted, Page, PageHeading, StatusBadge, TextField } from "@/components/portal";
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
      <Card>
        <CardContent>
          <DetailGrid>
            <DetailItem label="Email">{profile.email}</DetailItem>
            <DetailItem label="Role">{profile.role}</DetailItem>
            <DetailItem label="Status">
              <StatusBadge status={profile.status} />
            </DetailItem>
            <DetailItem label="Tenant">{profile.tenantCode ?? "—"}</DetailItem>
            <DetailItem label="Id">{profile.id}</DetailItem>
          </DetailGrid>
        </CardContent>
      </Card>
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
              maxLength={200}
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
              minLength={8}
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
