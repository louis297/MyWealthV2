import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { startEndSession } from "@/features/session/session";

export function ForbiddenPage() {
  return (
    <div className="mx-auto flex min-h-[50vh] w-full max-w-md items-center">
      <Card className="w-full">
        <CardHeader>
          <CardTitle>
            <h1 className="text-lg font-semibold tracking-tight">Forbidden</h1>
          </CardTitle>
          <CardDescription>You do not have access to this page.</CardDescription>
        </CardHeader>
        <CardContent>
          <Button type="button" variant="outline" onClick={() => void startEndSession()}>
            Sign out
          </Button>
        </CardContent>
      </Card>
    </div>
  );
}
